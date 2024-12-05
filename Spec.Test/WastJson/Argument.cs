// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Spec.Test.WastJson
{
    public class Argument
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("lane_type")]
        public string? LaneType { get; set; }

        [JsonPropertyName("value")]
        public object? Value { get; set; }


        public Value AsValue =>
            Type switch
            {
                "i32" => new Value(ValType.I32, Value?.ToString()??""),
                "i64" => new Value(ValType.I64, Value?.ToString()??""),
                "f32" => new Value(ValType.F32, Value?.ToString()??""),
                "f64" => new Value(ValType.F64, Value?.ToString()??""),
                "v128" => ParseV128(Value),
                "funcref" => new Value(ValType.Func, Value?.ToString()??"null"),
                "externref" => new Value(ValType.Extern, Value?.ToString()??"null"),
                _ => throw new ArgumentException($"Cannot parse value {Value} of type {Type}")
            };


        private Value ParseV128(object? value)
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

        private int BitBashInt(string intVal)
        {
            decimal value = decimal.Parse(intVal);
            if (value > uint.MaxValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
            if (value < int.MinValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
                
            if (value > int.MaxValue && value <= uint.MaxValue)
            {
                return (int)(uint)value;
            }
            return (int)value;
        }

        private static float BitBashFloat(string intval)
        {
            if (intval.StartsWith("nan:"))
                return float.NaN;
            decimal value = decimal.Parse(intval);
            if (value > int.MaxValue && value <= uint.MaxValue)
            {
                uint v = (uint)value;
                return MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref v, 1))[0];
            }

            return BitConverter.Int32BitsToSingle((int)value);
        }

        private static ulong BitBashLong(string longval)
        {
            return ulong.Parse(longval);
        }

        private static double BitBashDouble(string longval)
        {
            if (longval.StartsWith("nan:"))
                return double.NaN;
            decimal value = decimal.Parse(longval);
            if (value > long.MaxValue && value <= ulong.MaxValue)
            {
                ulong v = (ulong)value;
                return MemoryMarshal.Cast<ulong, double>(MemoryMarshal.CreateSpan(ref v, 1))[0];
            }

            return BitConverter.Int64BitsToDouble((long)value);
        }

        public override string ToString() => $"{Type}={Value}";
    }
}