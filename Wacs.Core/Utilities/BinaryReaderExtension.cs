// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Wacs.Core.Utilities
{
    public static class BinaryReaderExtension
    {
        public delegate void PostProcess<in T>(int index, T parsedObj);

        // Helper methods for parsing LEB128, strings, etc.
        public static uint ReadLeb128_u32(this BinaryReader reader)
        {
            uint result = 0;
            int shift = 0;
            byte byteValue;
            do
            {
                byteValue = reader.ReadByte();
                result |= (uint)(byteValue & 0x7F) << shift;
                
                if (shift == 28 && (byteValue & 0x70) != 0)
                    throw new FormatException("LEB128_u32 had too many bits");
                
                shift += 7;
            } while ((byteValue & 0x80) != 0);

            if (shift > 35)
                throw new FormatException("LEB128_u32 had too many bytes");
            
            return result;
        }

        public static ulong ReadLeb128_u64(this BinaryReader reader)
        {
            ulong result = 0;
            int shift = 0;
            byte byteValue;

            do
            {
                byteValue = reader.ReadByte();
                result |= (ulong)(byteValue & 0x7F) << shift;
                
                if (shift == 63 && (byteValue & 0x7E) != 0)
                    throw new FormatException("LEB128_u64 had too many bits");
                
                shift += 7;
            } while ((byteValue & 0x80) != 0);

            if (shift > 70)
                throw new FormatException("LEB128_u64 had too many bytes");
            
            return result;
        }

        public static int ReadLeb128_s32(this BinaryReader reader)
        {
            int result = 0;
            int shift = 0;
            byte byteValue;

            do
            {
                byteValue = reader.ReadByte();
                result |= (byteValue & 0x7F) << shift; //low-order 7 bits

                if (shift > 32)
                {
                    throw new FormatException($"LEB128_s32 had too many bytes");
                }
                if (shift > 32 - 8)
                {
                    int extraBits = (0xFF << (32 - shift)) & 0x7F;
                    int checkBits = byteValue & extraBits;
                    switch (result >= 0)
                    {
                        case true when checkBits != 0: 
                            throw new FormatException("$LEB128_s32 had overflow bits");
                        case false when checkBits != extraBits:
                            throw new FormatException($"LEB128_s32 did not have overflow bits for negative");
                    }
                }

                shift += 7;
            } while ((byteValue & 0x80) != 0); //high-order bit != 0

            // If the sign bit of the last byte read is set, extend the sign
            if (shift < 32 && (byteValue & 0x40) != 0)
            {
                // Sign extend the result
                result |= -(1 << shift); //~0 << shift;
            }

            return result;
        }

        public static long ReadLeb128_s64(this BinaryReader reader)
        {
            long result = 0;
            int shift = 0;
            byte byteValue;

            do
            {
                byteValue = reader.ReadByte();
                result |= (byteValue & 0x7FL) << shift; //low-order 7 bits

                if (shift > 64)
                {
                    throw new FormatException($"LEB128_s{64} had too many bytes");
                }
                if (shift > 64 - 8)
                {
                    int extraBits = (0xFF << (64 - shift)) & 0x7F;
                    int checkBits = byteValue & extraBits;
                    switch (result >= 0)
                    {
                        case true when checkBits != 0: 
                            throw new FormatException("$LEB128_s{width} had overflow bits");
                        case false when checkBits != extraBits:
                            throw new FormatException($"LEB128_s{64} did not have overflow bits for negative");
                    }
                }

                shift += 7;
            } while ((byteValue & 0x80) != 0); //high-order bit != 0

            // If the sign bit of the last byte read is set, extend the sign
            if (shift < 64 && (byteValue & 0x40) != 0)
            {
                // Sign extend the result
                result |= -(1L << shift); //~0 << shift;
            }

            return result;
        }


        /// <summary>
        /// Decode an s33 (u32) index
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="byteValue">We tried decoding the first byte as a type, but ended up here.</param>
        /// <returns>A u32 type index</returns>
        /// <exception cref="FormatException">We're s33 encoded, but negative values are invalid.</exception>
        public static uint ContinueReading_s33(this BinaryReader reader, byte byteValue)
        {
            //Continue parsing as LEB128_S33
            long result = 0;
            int shift = 0;
            bool moreBytes = true;

            while (moreBytes)
            {
                // Extract the lower 7 bits and add them to the result
                byte lower7Bits = (byte)(byteValue & 0x7F);
                result |= (long)lower7Bits << shift;

                // Increment the shift for the next 7 bits
                shift += 7;

                // Check if this is the last byte
                moreBytes = (byteValue & 0x80) != 0;

                // If it's the last byte, check the sign bit and perform sign extension if necessary
                if (!moreBytes)
                {
                    // If the sign bit of the last byte is set and shift is less than 33, sign-extend the result
                    if ((byteValue & 0x40) != 0 && shift < 33)
                    {
                        result |= -1L << shift;
                    }

                    break;
                }

                // Prevent shift overflow
                if (shift >= 40)
                    throw new FormatException("Shift count exceeds 40 bits while decoding s33.");

                byteValue = reader.ReadByte();
                if (byteValue == 0xFF)
                    throw new FormatException("Unexpected end of stream while decoding s33.");
            }

            if (result < 0)
                throw new FormatException($"HeapType Index {result:x8} was negative");
            
            //Just take the U32 bits since the unset sign bit is 33.
            return (uint)(result & 0xFFFF_FFFF);
        }


        public static float Read_f32(this BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4)
                throw new FormatException("Not enough bytes to read a float.");
            
            int bits = BitConverter.ToInt32(bytes, 0);
            return BitConverter.Int32BitsToSingle(bits);
        }

        public static double Read_f64(this BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(8);
            if (bytes.Length < 8)
                throw new FormatException("Not enough bytes to read a double.");
            
            long bits = BitConverter.ToInt64(bytes, 0);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return true;

            int expectedContinuationBytes = 0;
            int accumulator = 0;
            int minExpectedValue = 0;

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                if (expectedContinuationBytes > 0)
                {
                    // Check if this is a valid continuation byte (10xxxxxx)
                    if ((b & 0xC0) != 0x80)
                        return false;

                    // Accumulate the value
                    accumulator = (accumulator << 6) | (b & 0x3F);
                    expectedContinuationBytes--;

                    // If this was the last continuation byte, validate the complete sequence
                    if (expectedContinuationBytes == 0)
                    {
                        // Check for over-long encoding
                        if (accumulator < minExpectedValue)
                            return false;

                        // Check for invalid Unicode code points
                        if (accumulator > 0x10FFFF)
                            return false;

                        // Check for UTF-16 surrogate pairs range
                        if (accumulator >= 0xD800 && accumulator <= 0xDFFF)
                            return false;
                    }
                }
                else
                {
                    // Start of a new character
                    if ((b & 0x80) == 0) // Single byte (0xxxxxxx)
                    {
                        expectedContinuationBytes = 0;
                        accumulator = b;
                        minExpectedValue = 0;
                    }
                    else if ((b & 0xE0) == 0xC0) // Two bytes (110xxxxx)
                    {
                        expectedContinuationBytes = 1;
                        accumulator = b & 0x1F;
                        minExpectedValue = 0x80;  // Minimum value that requires 2 bytes
                    }
                    else if ((b & 0xF0) == 0xE0) // Three bytes (1110xxxx)
                    {
                        expectedContinuationBytes = 2;
                        accumulator = b & 0x0F;
                        minExpectedValue = 0x800;  // Minimum value that requires 3 bytes
                    }
                    else if ((b & 0xF8) == 0xF0) // Four bytes (11110xxx)
                    {
                        expectedContinuationBytes = 3;
                        accumulator = b & 0x07;
                        minExpectedValue = 0x10000;  // Minimum value that requires 4 bytes
                    }
                    else // Invalid start byte
                    {
                        return false;
                    }
                }
            }

            // Check if we're still expecting continuation bytes at the end
            return expectedContinuationBytes == 0;
        }

        public static string ReadUtf8String(this BinaryReader reader)
        {
            uint length = ReadLeb128_u32(reader);
            ReadOnlySpan<byte> bytes = reader.ReadBytes((int)length);
            if (bytes == null)
                throw new FormatException($"No bytes in utf-8 string");
            
            if (!IsValidUtf8(bytes))
                throw new FormatException("Badly formed utf-8 string");
            
            return Encoding.UTF8.GetString(bytes);
        }

        public static T[] ParseVector<T>(this BinaryReader reader, Func<BinaryReader, T> elementParser, PostProcess<T>? postProcess = null)
        {
            uint count = reader.ReadLeb128_u32();
            var vector = new T[count]; // Initialize the array to hold vector elements

            for (int i = 0; i < count; i++)
            {
                vector[i] = elementParser(reader); // Use the provided lambda function to parse each element
                postProcess?.Invoke(i, vector[i]);
            }

            return vector;
        }

        public static List<T> ParseList<T>(this BinaryReader reader, Func<BinaryReader, T> elementParser, PostProcess<T>? postProcess = null)
        {
            uint count = reader.ReadLeb128_u32();
            var vector = new List<T>((int)count);

            for (int i = 0; i < count; i++)
            {
                var element = elementParser(reader);
                postProcess?.Invoke(i, element);
                vector.Add(element);
            }

            return vector;
        }

        [SuppressMessage("ReSharper.DPA", "DPA0000: DPA issues")]
        public static List<T> ParseUntil<T>(this BinaryReader reader, Func<BinaryReader, T?> elementParser,
            Func<T, bool> predicate)
            where T : class
        {
            var to = new List<T>();
            do
            {
                var element = elementParser(reader);
                if (element == null)
                    break;

                //Add all the elements, including one that may terminate the list
                to.Add(element);

                if (predicate(element))
                    break;
            } while (true);

            return to;
        }

        public static BinaryReader GetSubsectionTo(this BinaryReader reader, int endPosition) =>
            reader.GetSubsection((int)(endPosition - reader.BaseStream.Position));

        public static BinaryReader GetSubsection(this BinaryReader reader, int length)
        {
            var customSectionStream = new MemoryStream();
            // var payloadEnd = reader.BaseStream.Position + length;
            byte[] buffer = new byte[length];
            int bytesRead = reader.Read(buffer, 0, length);
            if (bytesRead != length)
                throw new InvalidDataException($"BinaryReader did not contain enough bytes for subsection");

            customSectionStream.Write(buffer, 0, bytesRead);
            customSectionStream.Position = 0;
            return new BinaryReader(customSectionStream);
        }

        public static bool HasMoreBytes(this BinaryReader reader)
        {
            var stream = reader.BaseStream;

            // Check if the stream supports seeking
            if (stream.CanSeek)
            {
                // Check if there are more bytes to read
                return stream.Position < stream.Length;
            }

            // If the stream does not support seeking, we can't determine
            return true; // Assuming there might be more bytes, as we can't check
        }
    }
}