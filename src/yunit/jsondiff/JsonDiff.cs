// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HtmlAgilityPack;

namespace Yunit
{
    /// <summary>
    /// Visualize test validation result by producing a JSON diff.
    /// </summary>
    public class JsonDiff
    {
        private static readonly JsonSerializerOptions s_serializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };

        private readonly JsonDiffNormalize[] _rules = Array.Empty<JsonDiffNormalize>();

        public JsonDiff() { }

        internal JsonDiff(JsonDiffNormalize[] rules) => _rules = rules;

        /// <summary>
        /// Validate that the actual object matches expected object using the rules
        /// configured in this <see cref="JsonDiff"/> object.
        /// Throws <see cref="JsonDiffException"/> if the validation failed.
        /// </summary>
        public void Verify(object expected, object actual, string summary = null)
        {
            var diff = Diff(expected, actual);
            if (!string.IsNullOrEmpty(diff))
            {
                throw new JsonDiffException(summary, diff);
            }
        }

        /// <summary>
        /// Produces a diff from the JSON representation of expected object and
        /// JSON representation of actual object using the rules
        /// configured in this <see cref="JsonDiff"/> object.
        /// </summary>
        /// <returns>
        /// An empty string if there is no difference.
        /// </returns>
        public string Diff(object expected, object actual)
        {
            var expectedJson = ToJsonNode(expected);
            var actualJson = ToJsonNode(actual);

            var (expectedNorm, actualNorm) = Normalize(expectedJson, actualJson);
            var expectedText = Prettify(expectedNorm);
            var actualText = Prettify(actualNorm);

            var diff = new InlineDiffBuilder(new Differ())
                .BuildDiffModel(expectedText, actualText, ignoreWhitespace: true);

            var diffText = new StringBuilder();
            var hasDiff = false;

            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        diffText.Append('+');
                        hasDiff = true;
                        break;
                    case ChangeType.Deleted:
                        diffText.Append('-');
                        hasDiff = true;
                        break;
                    default:
                        diffText.Append(' ');
                        break;
                }

                diffText.AppendLine(line.Text);
            }

            return hasDiff ? diffText.ToString() : "";
        }

        /// <summary>
        /// Normalizes the expected <see cref="JsonNode"/> and actual <see cref="JsonNode"/>
        /// before performing a text diff, using the rules configured in this <see cref="JsonDiff"/> object.
        /// </summary>
        public (JsonNode, JsonNode) Normalize(JsonNode expected, JsonNode actual)
        {
            return NormalizeCore(expected, actual, "");
        }

        /// <summary>
        /// Normalizes an HTML text into a diff friendly representation.
        /// </summary>
        public static string NormalizeHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sb = new StringBuilder();
            NormalizeHtml(sb, doc.DocumentNode, 0);
            return sb.ToString().Trim();
        }

        private (JsonNode, JsonNode) NormalizeCore(JsonNode expected, JsonNode actual, string name)
        {
            ApplyRules(ref expected, ref actual, name);

            switch (expected)
            {
                case JsonObject expectedObj when actual is JsonObject actualObj:
                    var expectedProps = new Dictionary<string, JsonNode>(expectedObj.Count);
                    var actualProps = new Dictionary<string, JsonNode>(actualObj.Count);

                    foreach (var prop in expectedObj)
                    {
                        if (actualObj.ContainsKey(prop.Key))
                        {
                            var actualValue = actualObj[prop.Key];
                            var (expectedProp, actualProp) = NormalizeCore(prop.Value, actualValue, prop.Key);

                            if (expectedProp != null)
                            {
                                expectedProps.Add(prop.Key, expectedProp.Clone());
                            }
                            if (actualProp != null)
                            {
                                actualProps.Add(prop.Key, actualProp.Clone());
                            }
                        }
                        else
                        {
                            expectedProps.Add(prop.Key, prop.Value.Clone());
                        }
                    }

                    foreach (var additionalProperty in actualObj.AsEnumerable())
                    {
                        if (!expectedObj.ContainsKey(additionalProperty.Key))
                        {
                            actualProps.Add(additionalProperty.Key, additionalProperty.Value.Clone());
                        }
                    }

                    return (new JsonObject(expectedProps), new JsonObject(actualProps));

                case JsonArray expectedArray when actual is JsonArray actualArray:
                    var expectedArrayResult = expectedArray.Clone();
                    var actualArrayResult = actualArray.Clone();
                    var length = Math.Min(expectedArray.Count, actualArray.Count);

                    for (var i = 0; i < length; i++)
                    {
                        var (expectedNorm, actualNorm) = NormalizeCore(expectedArray[i], actualArray[i], "");

                        expectedArrayResult[i] = expectedNorm.Clone();
                        actualArrayResult[i] = actualNorm.Clone();
                    }
                    return (expectedArrayResult, actualArrayResult);

                default:
                    return (expected, actual);
            }
        }

        private void ApplyRules(ref JsonNode expected, ref JsonNode actual, string name)
        {
            foreach (var rule in _rules)
            {
                (expected, actual) = rule(expected, actual, name, this);
            }
        }

        private static string Prettify(JsonNode token)
        {
            return token.ToJsonString(s_serializerOptions)
                        .Replace(@"\r", "")
                        .Replace(@"\n", "\n")
                        .Replace("{}", "{\n}");
        }

        private static JsonNode ToJsonNode(object obj)
        {
            switch (obj)
            {
                case null:
                    return JsonValue.Create<object>(null);

                case JsonNode token:
                    return token;

                default:
                    return JsonNode.Parse(JsonSerializer.Serialize(obj, s_serializerOptions));
            }
        }

        private static void NormalizeHtml(StringBuilder sb, HtmlNode node, int level)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Document:
                    foreach (var child in node.ChildNodes)
                    {
                        NormalizeHtml(sb, child, level);
                    }
                    break;

                case HtmlNodeType.Text:
                    var line = TrimWhiteSpace(node.InnerHtml);
                    if (!string.IsNullOrEmpty(line))
                    {
                        Indent();
                        sb.Append(line);
                        sb.Append("\n");
                    }
                    break;

                case HtmlNodeType.Element:
                    Indent();
                    sb.Append("<");
                    sb.Append(node.Name);

                    foreach (var attr in node.Attributes.OrderBy(a => a.Name))
                    {
                        sb.Append($" {attr.Name}=\"{TrimWhiteSpace(attr.Value)}\"");
                    }
                    sb.Append(">\n");

                    foreach (var child in node.ChildNodes)
                    {
                        NormalizeHtml(sb, child, level + 1);
                    }

                    Indent();
                    sb.Append($"</{node.Name}>\n");
                    break;
            }

            void Indent()
            {
                for (var i = 0; i < level; i++)
                    sb.Append("  ");
            }

            string TrimWhiteSpace(string text)
            {
                return Regex.Replace(text, @"\s+", " ").Trim();
            }
        }
    }
}
