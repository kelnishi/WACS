using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Wacs.Core.Utilities
{
    public static class BinaryReaderExtension
    {
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
                shift += 7;
            } while ((byteValue & 0x80) != 0);

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
            int byteValue;

            while (true)
            {
                byteValue = reader.ReadByte();
                if (byteValue == -1)
                    throw new EndOfStreamException("Unexpected end of stream while decoding s64.");

                byte lower7Bits = (byte)(byteValue & 0x7F);
                result |= (long)lower7Bits << shift;

                shift += 7;

                bool moreBytes = (byteValue & 0x80) != 0;

                if (!moreBytes)
                {
                    if ((byteValue & 0x40) != 0 && shift < 65)
                    {
                        result |= -1L << shift;
                    }
                    break;
                }

                if (shift >= 64)
                    throw new OverflowException("Shift count exceeds 64 bits while decoding s64.");
            }

            return result;
        }
        
        
        public static long ReadLeb128_s33(this BinaryReader reader)
        {
            long result = 0;
            int shift = 0;
            int byteValue;

            while (true)
            {
                byteValue = reader.ReadByte();
                if (byteValue == -1)
                    throw new EndOfStreamException("Unexpected end of stream while decoding s33.");

                // Extract the lower 7 bits and add them to the result
                byte lower7Bits = (byte)(byteValue & 0x7F);
                result |= (long)lower7Bits << shift;

                // Increment the shift for the next 7 bits
                shift += 7;

                // Check if this is the last byte
                bool moreBytes = (byteValue & 0x80) != 0;

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
                if (shift >= 64)
                    throw new OverflowException("Shift count exceeds 64 bits while decoding s33.");
            }

            // Mask the result to 33 bits to ensure it's within the s33 range
            long mask = (1L << 33) - 1;
            result &= mask;

            // Sign-extend to 64 bits if the sign bit is set
            if ((result & (1L << 32)) != 0)
            {
                result |= -1L << 33;
            }

            return result;
        }
        
        public static float Read_f32(this BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4)
                throw new EndOfStreamException("Not enough bytes to read a float.");

            uint intValue = BitConverter.ToUInt32(bytes, 0);
            int sign = (int)(intValue >> 31) & 0x01;
            int exponent = (int)((intValue >> 23) & 0xFF);
            uint fraction = intValue & 0x7FFFFF;

            if (exponent == 0 && fraction == 0)
                return 0.0f; // Handle the zero case

            float result;

            if (exponent == 255)
            {
                if (fraction != 0)
                    result = float.NaN; // NaN
                else
                    result = sign == 0 ? float.PositiveInfinity : float.NegativeInfinity; // Infinity
            }
            else
            {
                if (exponent == 0)
                {
                    // Denormalized number
                    exponent += 1; // Adjust exponent for denormalized numbers
                }
                else
                {
                    exponent -= 127; // Bias removal
                }

                // Convert to float
                result = (float)(Math.Pow(-1, sign) * Math.Pow(2, exponent) * (1 + fraction / (float)(1 << 23)));
            }

            return result;
        }
        
        public static double Read_f64(this BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(8);
            if (bytes.Length < 8)
                throw new EndOfStreamException("Not enough bytes to read a double.");

            ulong ulongValue = BitConverter.ToUInt64(bytes, 0);
            int sign = (int)(ulongValue >> 63) & 0x01;
            int exponent = (int)((ulongValue >> 52) & 0x7FF);
            ulong fraction = ulongValue & 0xFFFFFFFFFFFFF;

            if (exponent == 0 && fraction == 0)
                return 0.0; // Handle the zero case

            double result;

            if (exponent == 0x7FF)
            {
                if (fraction != 0)
                    result = double.NaN; // NaN
                else
                    result = sign == 0 ? double.PositiveInfinity : double.NegativeInfinity; // Infinity
            }
            else
            {
                if (exponent == 0)
                {
                    // Denormalized number
                    exponent += 1; // Adjust exponent for denormalized numbers
                }
                else
                {
                    exponent -= 1023; // Bias removal
                }

                // Convert to double
                result = (double)(Math.Pow(-1, sign) * Math.Pow(2, exponent) * (1 + (double)fraction / (double)(1 << 52)));
            }

            return result;
        }
        
        

        public static string ReadUTF8String(this BinaryReader reader)
        {
            uint length = ReadLeb128_u32(reader);
            byte[] bytes = reader.ReadBytes((int)length);
            return Encoding.UTF8.GetString(bytes);
        }

        public static T[] ParseVector<T>(this BinaryReader reader, Func<BinaryReader, T> elementParser)
        {
            uint count = reader.ReadLeb128_u32();
            T[] vector = new T[count]; // Initialize the array to hold vector elements

            for (uint i = 0; i < count; i++)
            {
                vector[i] = elementParser(reader); // Use the provided lambda function to parse each element
            }

            return vector;
        }

        public static List<T> ParseN<T>(this BinaryReader reader, Func<BinaryReader, T?> elementParser)
        where T : class
        {
            List<T> to = new List<T>();
            while (elementParser(reader) is { } value) {
                to.Add(value); // Add the non-null value to the list
            }
            return to;
        }

        public static BinaryReader GetSubsectionTo(this BinaryReader reader, int endPosition) => 
            reader.GetSubsection((int)(endPosition - reader.BaseStream.Position));
        
        public static BinaryReader GetSubsection(this BinaryReader reader, int length)
        {
            var customSectionStream = new MemoryStream();
            var payloadEnd = reader.BaseStream.Position + length;
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