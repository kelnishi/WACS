using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Spec.Test.WastJson
{
    public class Argument
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("lane_type")]
        public string LaneType { get; set; }

        [JsonPropertyName("value")]
        public object Value { get; set; }


        public Value AsValue =>
            Type switch
            {
                "i32" => new Value(ValType.I32, Value.ToString()),
                "i64" => new Value(ValType.I64, Value.ToString()),
                "f32" => new Value(ValType.F32, Value.ToString()),
                "f64" => new Value(ValType.F64, Value.ToString()),
                "v128" => ParseV128(Value),
                "funcref" => new Value(ValType.Funcref, Value.ToString()),
                "externref" => new Value(ValType.Externref, Value.ToString()),
                _ => throw new ArgumentException($"Cannot parse value {Value} of type {Type}")
            };

        private Value ParseV128(object value)
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                var arrayValues = new List<uint>();
                foreach (var item in jsonElement.EnumerateArray())
                {
                    if (item.TryGetUInt32(out uint i32))
                    {
                        arrayValues.Add(i32);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid value in v128 array: {item}");
                    }
                }
                if (arrayValues.Count != 4)
                    throw new ArgumentException($"Invalid value in v128 array: {value}");
                
                return new Value(arrayValues[0],arrayValues[1],arrayValues[2],arrayValues[3]);
            }

            throw new ArgumentException($"Invalid value for v128: {value}");
        }

        public override string ToString() => $"{Type}={Value}";
    }
}