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
                "i32" => new Value(uint.Parse(Value)),
                "i64" => new Value(ulong.Parse(Value)),
                "f32" => new Value(BitBashFloat(Value)),
                "f64" => new Value(BitBashDouble(Value)),
                "funcref" => new Value(ValType.Funcref, int.Parse(Value)),
                "externref" => new Value(ValType.Externref, int.Parse(Value)),
                _ => throw new ArgumentException($"Cannot parse value {Value} of type {Type}")
            };

        private float BitBashFloat(string intval)
        {
            uint v = uint.Parse(intval);
            return BitConverter.ToSingle(BitConverter.GetBytes(v));
        }

        private double BitBashDouble(string longval)
        {
            // if (longval.StartsWith("nan"))
            //     return double.NaN;
            
            ulong v = ulong.Parse(longval);
            return BitConverter.ToDouble(BitConverter.GetBytes(v));
        }

        public override string ToString() => $"{Type}={Value}";
    }
}