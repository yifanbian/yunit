// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Yunit
{
    internal partial class YamlUtility
    {
        internal static JsonNode ToJsonNode(
            string input,
            Action<Scalar> onKeyDuplicate = null,
            Func<JsonNode, ParsingEvent, JsonNode> onConvert = null)
        {
            return ToJsonNode(new StringReader(input), onKeyDuplicate, onConvert);
        }

        internal static JsonNode ToJsonNode(
            TextReader input,
            Action<Scalar> onKeyDuplicate = null,
            Func<JsonNode, ParsingEvent, JsonNode> onConvert = null)
        {
            JsonNode result = null;

            onKeyDuplicate ??= (_ => { });
            onConvert ??= ((token, _) => token);

            var parser = new Parser(input);
            parser.Consume<StreamStart>();
            if (!parser.TryConsume<StreamEnd>(out var _))
            {
                parser.Consume<DocumentStart>();
                result = ToJsonNode(parser, onKeyDuplicate, onConvert);
                parser.Consume<DocumentEnd>();
            }

            return result;
        }

        private static JsonNode ToJsonNode(
            IParser parser,
            Action<Scalar> onKeyDuplicate,
            Func<JsonNode, ParsingEvent, JsonNode> onConvert)
        {
            switch (parser.Consume<NodeEvent>())
            {
                case Scalar scalar:
                    if (scalar.Style == ScalarStyle.Plain)
                    {
                        return onConvert(ParseScalarAsJsonNode(scalar.Value), scalar);
                    }
                    return onConvert(JsonValue.Create(scalar.Value), scalar);

                case SequenceStart seq:
                    var array = new JsonArray();
                    while (!parser.TryConsume<SequenceEnd>(out var _))
                    {
                        array.Add(ToJsonNode(parser, onKeyDuplicate, onConvert));
                    }
                    return onConvert(array, seq);

                case MappingStart map:
                    var obj = new JsonObject();
                    while (!parser.TryConsume<MappingEnd>(out var _))
                    {
                        var key = parser.Consume<Scalar>();
                        var value = ToJsonNode(parser, onKeyDuplicate, onConvert);

                        if (obj.ContainsKey(key.Value))
                        {
                            onKeyDuplicate(key);
                        }

                        obj[key.Value] = value;
                        onConvert(obj[key.Value], key);
                    }
                    return onConvert(obj, map);

                default:
                    throw new NotSupportedException($"Yaml node '{parser.Current.GetType().Name}' is not supported");
            }
        }

        private static JsonNode ParseScalarAsJsonNode(string value)
        {
            // https://yaml.org/spec/1.2/2009-07-21/spec.html
            //
            //  Regular expression       Resolved to tag
            //
            //    null | Null | NULL | ~                          tag:yaml.org,2002:null
            //    /* Empty */                                     tag:yaml.org,2002:null
            //    true | True | TRUE | false | False | FALSE      tag:yaml.org,2002:bool
            //    [-+]?[0 - 9]+                                   tag:yaml.org,2002:int(Base 10)
            //    0o[0 - 7] +                                     tag:yaml.org,2002:int(Base 8)
            //    0x[0 - 9a - fA - F] +                           tag:yaml.org,2002:int(Base 16)
            //    [-+] ? ( \. [0-9]+ | [0-9]+ ( \. [0-9]* )? ) ( [eE][-+]?[0 - 9]+ )?   tag:yaml.org,2002:float (Number)
            //    [-+]? ( \.inf | \.Inf | \.INF )                 tag:yaml.org,2002:float (Infinity)
            //    \.nan | \.NaN | \.NAN                           tag:yaml.org,2002:float (Not a number)
            //    *                                               tag:yaml.org,2002:str(Default)
            if (string.IsNullOrEmpty(value) || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create<object>(null);
            }
            if (bool.TryParse(value, out var b))
            {
                return JsonValue.Create(b);
            }
            if (long.TryParse(value, out var l))
            {
                return JsonValue.Create(l);
            }
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
                !double.IsNaN(d) && !double.IsPositiveInfinity(d) && !double.IsNegativeInfinity(d))
            {
                return JsonValue.Create(d);
            }
            if (value.Equals(".nan", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(double.NaN);
            }
            if (value.Equals(".inf", StringComparison.OrdinalIgnoreCase) || value.Equals("+.inf", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(double.PositiveInfinity);
            }
            if (value.Equals("-.inf", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(double.NegativeInfinity);
            }
            return JsonValue.Create(value);
        }
    }
}
