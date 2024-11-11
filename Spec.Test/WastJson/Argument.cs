using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var strVals = jsonElement.EnumerateArray().Select(js => js.ToString()).ToList();

                switch (strVals.Count)
                {
                    case 2: return Parse2(strVals);
                    case 4: return Parse4(strVals);
                    case 8: return new Value(new V128(
                        (ushort)BitBashInt(strVals[0]),
                        (ushort)BitBashInt(strVals[1]),
                        (ushort)BitBashInt(strVals[2]),
                        (ushort)BitBashInt(strVals[3]),
                        (ushort)BitBashInt(strVals[4]),
                        (ushort)BitBashInt(strVals[5]),
                        (ushort)BitBashInt(strVals[6]),
                        (ushort)BitBashInt(strVals[7])));
                    case 16: return new Value(new V128(
                        (byte)BitBashInt(strVals[0x0]),
                        (byte)BitBashInt(strVals[0x1]),
                        (byte)BitBashInt(strVals[0x2]),
                        (byte)BitBashInt(strVals[0x3]),
                        (byte)BitBashInt(strVals[0x4]),
                        (byte)BitBashInt(strVals[0x5]),
                        (byte)BitBashInt(strVals[0x6]),
                        (byte)BitBashInt(strVals[0x7]),
                        (byte)BitBashInt(strVals[0x8]),
                        (byte)BitBashInt(strVals[0x9]),
                        (byte)BitBashInt(strVals[0xA]),
                        (byte)BitBashInt(strVals[0xB]),
                        (byte)BitBashInt(strVals[0xC]),
                        (byte)BitBashInt(strVals[0xD]),
                        (byte)BitBashInt(strVals[0xE]),
                        (byte)BitBashInt(strVals[0xF])));
                    default:
                        throw new ArgumentException($"Invalid value in v128 array: {value}");
                }
            }
            throw new ArgumentException($"Invalid value for v128: {value}");
        }

        private Value Parse2(List<string> vals)
        {
            //Doubles
            if (LaneType=="f64" || vals.Any(v=> v.Contains(":") || v.Contains(".")))
            {
                return new V128(BitBashDouble(vals[0]), BitBashDouble(vals[1]));
            }
            return new V128(BitBashLong(vals[0]), BitBashLong(vals[1]));
        }

        private Value Parse4(List<string> vals)
        {
            if (LaneType=="f32" || vals.Any(v=> v.Contains(":") || v.Contains(".")))
            {
                return new V128(BitBashFloat(vals[0]), BitBashFloat(vals[1]),BitBashFloat(vals[2]), BitBashFloat(vals[3]));
            }
            
            return new V128(BitBashInt(vals[0]), BitBashInt(vals[1]),BitBashInt(vals[2]), BitBashInt(vals[3]));
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

        private static float BitBashFloat(string intval)
        {
            if (intval.StartsWith("nan:"))
                return float.NaN;
            
            uint v = uint.Parse(intval);
            return BitConverter.ToSingle(BitConverter.GetBytes(v));
        }

        private static ulong BitBashLong(string longval)
        {
            return ulong.Parse(longval);
        }

        private static double BitBashDouble(string longval)
        {
            if (longval.StartsWith("nan:"))
                return double.NaN;
            
            ulong v = ulong.Parse(longval);
            return BitConverter.ToDouble(BitConverter.GetBytes(v));
        }

        public override string ToString() => $"{Type}={Value}";
    }
}