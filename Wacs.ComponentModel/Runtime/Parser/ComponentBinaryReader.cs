// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Span-backed reader for Component Model binary payloads —
    /// advances a position pointer through a <c>byte[]</c>
    /// emitting LEB128 varuints, length-prefixed UTF-8 names,
    /// and fixed-width integers. Shared across every section
    /// decoder so the encoding conventions land in one place.
    ///
    /// <para>Component encoding mirrors core wasm: unsigned LEB128
    /// for indices and sizes, length-prefixed bytes for names
    /// and strings. Signed LEB128 appears in type descriptors
    /// (negative type codes for primitives).</para>
    /// </summary>
    public sealed class ComponentBinaryReader
    {
        private readonly byte[] _bytes;
        private int _pos;

        public ComponentBinaryReader(byte[] bytes)
        {
            _bytes = bytes;
            _pos = 0;
        }

        /// <summary>Current byte offset — useful for reporting
        /// positions in error messages and for tests that need
        /// to assert the reader fully consumed a payload.</summary>
        public int Position => _pos;

        /// <summary>True once the reader has consumed every
        /// byte — the natural loop terminator after reading the
        /// section's element count.</summary>
        public bool AtEnd => _pos >= _bytes.Length;

        /// <summary>Read one byte, advancing the position.</summary>
        public byte ReadByte()
        {
            if (_pos >= _bytes.Length)
                throw new FormatException("Premature end of component section payload.");
            return _bytes[_pos++];
        }

        /// <summary>Peek the next byte without advancing.</summary>
        public byte PeekByte()
        {
            if (_pos >= _bytes.Length)
                throw new FormatException("Premature end of component section payload.");
            return _bytes[_pos];
        }

        /// <summary>Read an LEB128-encoded unsigned 32-bit int.</summary>
        public uint ReadVarU32()
        {
            uint value = 0;
            int shift = 0;
            while (true)
            {
                var b = ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
                if (shift > 32)
                    throw new FormatException(
                        "LEB128 varuint32 exceeds 5-byte limit.");
            }
        }

        /// <summary>Read an LEB128-encoded signed 33-bit int
        /// (sufficient for all component type codes — negative
        /// encodings for primitives and positive for
        /// type-section indices).</summary>
        public long ReadVarI33()
        {
            long value = 0;
            int shift = 0;
            byte b;
            do
            {
                b = ReadByte();
                value |= (long)(b & 0x7F) << shift;
                shift += 7;
                if (shift > 33)
                    throw new FormatException(
                        "LEB128 varint33 exceeds 5-byte limit.");
            } while ((b & 0x80) != 0);
            // Sign-extend if the last payload bit was set.
            if (shift < 64 && (b & 0x40) != 0)
                value |= -1L << shift;
            return value;
        }

        /// <summary>Read a <c>varuint32</c>-prefixed UTF-8 name
        /// (interface name, export name, type name, …).</summary>
        public string ReadName()
        {
            var len = ReadVarU32();
            if (_pos + len > _bytes.Length)
                throw new FormatException(
                    "Name length exceeds remaining payload.");
            var s = Encoding.UTF8.GetString(_bytes, _pos, (int)len);
            _pos += (int)len;
            return s;
        }

        /// <summary>Copy <paramref name="count"/> bytes out as a
        /// fresh array. For capturing opaque per-type data blobs
        /// during structural decoding.</summary>
        public byte[] ReadBytes(int count)
        {
            if (_pos + count > _bytes.Length)
                throw new FormatException(
                    $"Requested {count} bytes, only {_bytes.Length - _pos} "
                    + "remain in payload.");
            var result = new byte[count];
            Buffer.BlockCopy(_bytes, _pos, result, 0, count);
            _pos += count;
            return result;
        }
    }
}
