// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yunit
{
    internal static class JsonNodeExtensions
    {
        internal static object ToObject(this JsonNode node, Type objectType)
        {
            return typeof(JsonNode).GetMethod("GetValue").MakeGenericMethod(objectType).Invoke(node, Array.Empty<object>());
        }

        internal static T Clone<T>(this T node, JsonSerializerOptions options = null) where T : JsonNode
        {
            return JsonNode.Parse(node.ToJsonString(options)) as T;
        }
    }
}
