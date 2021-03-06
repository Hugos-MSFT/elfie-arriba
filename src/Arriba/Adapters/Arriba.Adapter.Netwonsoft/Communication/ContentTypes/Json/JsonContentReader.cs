﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace Arriba.Communication.ContentTypes
{
    /// <summary>
    /// Json content reader
    /// </summary>
    public sealed class JsonContentReader : IContentReader
    {
        private JsonSerializerSettings _settings;

        public JsonContentReader(IEnumerable<JsonConverter> converters)
        {
            _settings = ArribaSerializationConfig.GetConfiguredSettings(converters);
        }

        IEnumerable<string> IContentReader.ContentTypes
        {
            get
            {
                yield return "application/json";
                yield return "application/javascript";
                yield return "text/plain;charset=UTF-8";
            }
        }

        bool IContentReader.CanRead<T>()
        {
            // Supports any type. 
            return true;
        }

        async Task<T> IContentReader.ReadAsync<T>(Stream input)
        {
            using (var reader = new StreamReader(input))
            {
                string value = await reader.ReadToEndAsync();
                T result = JsonConvert.DeserializeObject<T>(value, _settings);
                return result;
            }
        }
    }
}
