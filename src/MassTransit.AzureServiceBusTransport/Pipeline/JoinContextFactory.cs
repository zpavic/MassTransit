// Copyright 2007-2018 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.AzureServiceBusTransport.Pipeline
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using GreenPipes;
    using GreenPipes.Agents;
    using Logging;


    public abstract class JoinContextFactory<TLeft, TRight, TContext> :
        IPipeContextFactory<TContext>
        where TLeft : class, PipeContext
        where TRight : class, PipeContext
        where TContext : class, PipeContext
    {
        static readonly ILog _log = Logger.Get<JoinContextFactory<TLeft, TRight, TContext>>();
        readonly IPipe<TLeft> _leftPipe;
        readonly IPipeContextSource<TLeft> _leftSource;
        readonly IPipe<TRight> _rightPipe;
        readonly IPipeContextSource<TRight> _rightSource;

        protected JoinContextFactory(IPipeContextSource<TLeft> leftSource, IPipe<TLeft> leftPipe, IPipeContextSource<TRight> rightSource,
            IPipe<TRight> rightPipe)
        {
            _rightSource = rightSource;
            _leftSource = leftSource;
            _rightPipe = rightPipe;
            _leftPipe = leftPipe;
        }

        IPipeContextAgent<TContext> IPipeContextFactory<TContext>.CreateContext(ISupervisor supervisor)
        {
            IAsyncPipeContextAgent<TContext> asyncContext = supervisor.AddAsyncContext<TContext>();

            Task<TContext> context = CreateJoinContext(asyncContext, supervisor.Stopped);

            return asyncContext;
        }

        IActivePipeContextAgent<TContext> IPipeContextFactory<TContext>.CreateActiveContext(ISupervisor supervisor, PipeContextHandle<TContext> context,
            CancellationToken cancellationToken)
        {
            return supervisor.AddActiveContext(context, CreateSharedContext(context.Context, cancellationToken));
        }

        async Task<TContext> CreateJoinContext(IAsyncPipeContextAgent<TContext> asyncContext, CancellationToken cancellationToken)
        {
            IAsyncPipeContextAgent<TLeft> leftAgent = new AsyncPipeContextAgent<TLeft>();
            IAsyncPipeContextAgent<TRight> rightAgent = new AsyncPipeContextAgent<TRight>();

            var leftPipe = new AsyncPipeContextPipe<TLeft>(leftAgent, _leftPipe);
            var leftTask = _leftSource.Send(leftPipe, cancellationToken);

            var rightPipe = new AsyncPipeContextPipe<TRight>(rightAgent, _rightPipe);
            var rightTask = _rightSource.Send(rightPipe, cancellationToken);

            async Task Join()
            {
                try
                {
                    var leftAny = await Task.WhenAny(leftAgent.Context, leftTask).ConfigureAwait(false);
                    if (leftAny == leftTask)
                        await leftTask.ConfigureAwait(false);

                    var rightAny = await Task.WhenAny(rightAgent.Context, rightTask).ConfigureAwait(false);
                    if (rightAny == rightTask)
                        await rightTask.ConfigureAwait(false);

                    var leftContext = await leftAgent.Context.ConfigureAwait(false);
                    var rightContext = await rightAgent.Context.ConfigureAwait(false);

                    var clientContext = CreateClientContext(leftContext, rightContext);

                    clientContext.GetOrAddPayload(() => rightContext);
                    clientContext.GetOrAddPayload(() => leftContext);

                    await asyncContext.Created(clientContext).ConfigureAwait(false);

                    await asyncContext.Completed.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await asyncContext.CreateCanceled().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await asyncContext.CreateFaulted(exception).ConfigureAwait(false);
                }
                finally
                {
                    await Task.WhenAll(leftAgent.Stop("Complete", cancellationToken), rightAgent.Stop("Complete", cancellationToken)).ConfigureAwait(false);
                }

                try
                {
                    await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (_log.IsWarnEnabled)
                        _log.Debug("Faulted Task", exception);
                }
            }

            await Task.WhenAny(asyncContext.Context, Join()).ConfigureAwait(false);

            return await asyncContext.Context.ConfigureAwait(false);
        }

        protected abstract TContext CreateClientContext(TLeft leftContext, TRight rightContext);

        protected abstract Task<TContext> CreateSharedContext(Task<TContext> context, CancellationToken cancellationToken);
    }
}