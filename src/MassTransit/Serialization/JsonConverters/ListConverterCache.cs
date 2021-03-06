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
namespace MassTransit.Serialization.JsonConverters
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;


    public static class ListConverterCache
    {
        static ListConverter GetOrAdd(Type type)
        {
            return InstanceCache.Cached.GetOrAdd(type, _ =>
                (ListConverter)Activator.CreateInstance(typeof(CachedConverter<>).MakeGenericType(type)));
        }

        public static object GetList(JsonContract contract, JsonReader reader, JsonSerializer serializer)
        {
            if (!(contract is JsonArrayContract arrayContract))
                throw new JsonSerializationException("Object is not an array contract");

            return GetOrAdd(arrayContract.CollectionItemType).GetList(arrayContract, reader, serializer);
        }


        interface ListConverter
        {
            object GetList(JsonArrayContract contract, JsonReader reader, JsonSerializer serializer);
        }


        class CachedConverter<T> :
            ListConverter
        {
            public object GetList(JsonArrayContract contract, JsonReader reader, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                    return null;

                IList<T> list = contract.DefaultCreator != null
                    ? contract.DefaultCreator() as IList<T>
                    : new List<T>();

                if (reader.TokenType == JsonToken.StartArray)
                    serializer.Populate(reader, list);
                else
                {
                    var item = (T)serializer.Deserialize(reader, typeof(T));
                    list.Add(item);
                }

                if (contract.CreatedType.IsArray)
                    return list.ToArray();

                return list;
            }
        }


        static class InstanceCache
        {
            internal static readonly ConcurrentDictionary<Type, ListConverter> Cached = new ConcurrentDictionary<Type, ListConverter>();
        }
    }
}