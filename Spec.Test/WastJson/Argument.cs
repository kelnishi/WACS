using System;
using System.Text.Json.Serialization;
using Wacs.Core.Runtime;

namespace Spec.Test.WastJson
{
    public class Argument
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        public Value AsValue =>
            Type switch
            {
                "i32" => int.Parse(Value),
                "i64" => long.Parse(Value),
                "f32" => float.Parse(Value),
                "f64" => double.Parse(Value),
                _ => throw new ArgumentException($"Cannot parse value {Value} of type {Type}")
            };
    }
}