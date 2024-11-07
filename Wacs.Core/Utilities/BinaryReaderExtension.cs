using System;
using System.Collections.Generic;
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
                    throw new FormatException("LEB128_s32 had too many bits");
                
                shift += 7;
            } while ((byteValue & 0x80) != 0);

            if (shift > 35)
                throw new FormatException("LEB128_u32 had too many bytes");
            
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

        public static string ReadUtf8String(this BinaryReader reader)
        {
            uint length = ReadLeb128_u32(reader);
            byte[] bytes = reader.ReadBytes((int)length);
            if (bytes == null)
                throw new FormatException($"No bytes in utf-8 string");

            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch (ArgumentException exc)
            {
                throw new FormatException($"Failed to decode utf-8 string: {exc.Message}");
            }
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