using System;
using System.Text.Json.Serialization;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

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
                "i32" => new Value(ValType.I32, Value),
                "i64" => new Value(ValType.I64, Value),
                "f32" => new Value(ValType.F32, Value),
                "f64" => new Value(ValType.F64, Value),
                "funcref" => new Value(ValType.Funcref, Value),
                "externref" => new Value(ValType.Externref, Value),
                _ => throw new ArgumentException($"Cannot parse value {Value} of type {Type}")
            };

        public override string ToString() => $"{Type}={Value}";
    }
}