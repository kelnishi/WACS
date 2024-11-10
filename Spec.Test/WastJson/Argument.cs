using System;
using System.Collections.Generic;
using System.IO;
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
                var arrayValues = new List<decimal>();
                foreach (var item in jsonElement.EnumerateArray())
                {
                    arrayValues.Add(decimal.Parse(item.ToString()));
                }

                switch (arrayValues.Count)
                {
                    case 2: return new Value(new V128(
                        (ulong)arrayValues[0],
                        (ulong)arrayValues[1]));
                    case 4: return new Value(new V128(
                        (uint)arrayValues[0],
                        (uint)arrayValues[1],
                        (uint)arrayValues[2],
                        (uint)arrayValues[3]));
                    case 8: return new Value(new V128(
                        (ushort)arrayValues[0],
                        (ushort)arrayValues[1],
                        (ushort)arrayValues[2],
                        (ushort)arrayValues[3],
                        (ushort)arrayValues[4],
                        (ushort)arrayValues[5],
                        (ushort)arrayValues[6],
                        (ushort)arrayValues[7]));
                    case 16: return new Value(new V128(
                        (byte)arrayValues[0],
                        (byte)arrayValues[1],
                        (byte)arrayValues[2],
                        (byte)arrayValues[3],
                        (byte)arrayValues[4],
                        (byte)arrayValues[5],
                        (byte)arrayValues[6],
                        (byte)arrayValues[7],
                        (byte)arrayValues[8],
                        (byte)arrayValues[9],
                        (byte)arrayValues[10],
                        (byte)arrayValues[11],
                        (byte)arrayValues[12],
                        (byte)arrayValues[13],
                        (byte)arrayValues[14],
                        (byte)arrayValues[15]));
                    default:
                        throw new ArgumentException($"Invalid value in v128 array: {value}");
                }
            }
            throw new ArgumentException($"Invalid value for v128: {value}");
        }

        private static uint BitBashInt(string intVal)
        {
            decimal value = decimal.Parse(intVal);
            if (value > uint.MaxValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
            if (value < int.MinValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");

            return (uint)value;
        }

        public override string ToString() => $"{Type}={Value}";
    }
}