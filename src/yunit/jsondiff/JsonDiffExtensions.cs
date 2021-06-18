// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Yunit
{
    public static class JsonDiffExtensions
    {
        private static readonly JsonSerializerOptions s_serializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        /// <summary>
        /// Ignore the actual result of a property if the expected value is null
        /// </summary>
        /// <example>
        /// Given the expectation { "a": null }, { "a": "anything" } pass.
        /// </example>
        public static JsonDiffBuilder UseIgnoreNull(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(
                predicate,
                (expected, actual, name, diff) =>
                    expected.GetType() == typeof(object) &&
                    expected.GetValue<object>() == null &&
                    actual != null ? (expected, expected) : (expected, actual));
        }

        /// <summary>
        /// Assert the actual result must not be the expected result if the expected value starts with !
        /// </summary>
        /// <example>
        /// Given the expectation "!value", "a value" pass but "value" fail
        /// </example>
        public static JsonDiffBuilder UseNegate(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate, (expected, actual, name, diff) =>
            {
                if (expected.GetType() == typeof(string) && actual.GetType() == typeof(string) &&
                    expected.AsValue().TryGetValue<string>(out var str) && str.StartsWith("!"))
                {
                    if (str.Substring(1) != actual.GetValue<string>())
                    {
                        return (actual, actual);
                    }
                }
                return (expected, actual);
            });
        }

        /// <summary>
        /// Assert the actual value must match a regex if the expectation looks like /{regex}/
        /// </summary>
        /// <example>
        /// Given the expectation "/^a*$/", "a" pass but "b" fail.
        /// </example>
        public static JsonDiffBuilder UseRegex(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate, (expected, actual, name, diff) =>
            {
                if (expected.GetType() == typeof(string) && actual.GetType() == typeof(string) &&
                    expected.GetValue<string>() is string str &&
                    str.Length > 2 && str.StartsWith("/") && str.EndsWith("/"))
                {
                    var regex = str.Substring(1, str.Length - 2);
                    if (Regex.IsMatch(actual.GetValue<string>(), regex))
                    {
                        return (actual, actual);
                    }
                }
                return (expected, actual);
            });
        }

        /// <summary>
        /// Assert the actual value must match a wildcard if the expectation contains *
        /// </summary>
        /// <example>
        /// Given the expectation "a*", "aa" pass but "bb" fail.
        /// </example>
        public static JsonDiffBuilder UseWildcard(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate, (expected, actual, name, diff) =>
            {
                if (expected.GetType() == typeof(string) && actual.GetType() == typeof(string) &&
                    expected.GetValue<string>() is string str && str.Contains('*'))
                {
                    if (Regex.IsMatch(actual.GetValue<string>(), $"^{Regex.Escape(str).Replace("\\*", ".*")}$"))
                    {
                        return (actual, actual);
                    }
                }
                return (expected, actual);
            });
        }

        /// <summary>
        /// Ignore additonal properties in actual value that is missing in expected value.
        /// </summary>
        /// <example>
        /// Given the expectation { "a": 1 }, { "b": 1 } pass.
        /// </example>
        public static JsonDiffBuilder UseAdditionalProperties(
            this JsonDiffBuilder builder, JsonDiffPredicate predicate = null, Func<string, bool> isRequiredProperty = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate, (expected, actual, name, diff) =>
            {
                if (expected is JsonObject expectedObj && actual is JsonObject actualObj)
                {
                    var newActual = actualObj.Clone();
                    var unnecessaryKeys = actualObj.Where(x => !IsRequiredProperty(x)).Select(x => x.Key).ToArray();
                    foreach (var key in unnecessaryKeys)
                    {
                        newActual.Remove(key);
                    }

                    return (expected, newActual);
                }

                return (expected, actual);

                bool IsRequiredProperty(KeyValuePair<string, JsonNode> property)
                {
                    if (expectedObj.ContainsKey(property.Key))
                    {
                        return true;
                    }
                    if (isRequiredProperty != null && isRequiredProperty(property.Key))
                    {
                        return true;
                    }
                    return false;
                }
            });
        }

        /// <summary>
        /// Assert the actual value must be a JSON string that matches the expected JSON string.
        /// </summary>
        /// <example>
        /// Given the expectation "{ \"a\": 1 }", ""{ \"a\": 1 }"" pass but "{ \"a\": 2 }" fail.
        /// </example>
        public static JsonDiffBuilder UseJson(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null, JsonDiff jsonDiff = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate ?? IsFile(".json"), (expected, actual, name, diff) =>
            {
                if (expected is JsonValue ev && ev.TryGetValue<string>(out var expectedText) &&
                    actual is JsonValue av && av.TryGetValue<string>(out var actualText))
                {
                    var (expectedNorm, actualNorm) = (jsonDiff ?? diff).Normalize(
                        JsonNode.Parse(expectedText),
                        JsonNode.Parse(actualText));

                    return (expectedNorm.ToJsonString(s_serializerOptions), actualNorm.ToJsonString(s_serializerOptions));
                }

                return (expected, actual);
            });
        }

        /// <summary>
        /// Assert the actual value must be an HTML string that matches the expected HTML string.
        /// </summary>
        /// <example>
        /// Given the expectation "<div>text</div>", "<div> text </div>" pass but "<div>text 2</div>" fail.
        /// </example>
        public static JsonDiffBuilder UseHtml(this JsonDiffBuilder builder, JsonDiffPredicate predicate = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            return builder.Use(predicate ?? IsFile(".html", ".htm"), (expected, actual, name, diff) =>
            {
                if (expected is JsonValue ev && ev.TryGetValue<string>(out var expectedText) &&
                    actual is JsonValue av && av.TryGetValue<string>(out var actualText))
                {
                    return (JsonDiff.NormalizeHtml(expectedText), JsonDiff.NormalizeHtml(actualText));
                }

                return (expected, actual);
            });
        }

        private static JsonDiffPredicate IsFile(params string[] fileExtensions)
        {
            return (expected, actual, name) =>
                fileExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        }
    }
}
