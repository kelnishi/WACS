// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Component Model binary format preamble section identifier.
    /// Every top-level section in a component binary starts with
    /// one of these 1-byte tags, followed by a u32 LEB128 size and
    /// the section payload.
    ///
    /// <para>Spec: <c>component-model/design/mvp/Binary.md</c>.
    /// Values are stable across the MVP / 0.2.x line.</para>
    /// </summary>
    public enum ComponentSectionId : byte
    {
        Custom = 0,
        CoreModule = 1,
        CoreInstance = 2,
        CoreType = 3,
        Component = 4,
        Instance = 5,
        Alias = 6,
        Type = 7,
        Canon = 8,
        Start = 9,
        Import = 10,
        Export = 11,
    }

    /// <summary>
    /// Opaque raw section captured during a parse pass — holds the
    /// section tag plus the unparsed byte payload. Section-specific
    /// readers consume this to populate structured <c>ComponentModule</c>
    /// fields; raw sections are retained in full so custom sections
    /// (producers, name, wit-text metadata) can be inspected later.
    /// </summary>
    public sealed class RawComponentSection
    {
        public ComponentSectionId Id { get; }
        public byte[] Payload { get; }

        public RawComponentSection(ComponentSectionId id, byte[] payload)
        {
            Id = id;
            Payload = payload;
        }

        public int Size => Payload.Length;
    }

    /// <summary>
    /// Parses Component Model binary format (the `.component.wasm`
    /// file produced by <c>wasm-tools component new</c>,
    /// <c>componentize-dotnet</c>, etc.).
    ///
    /// <para><b>Preamble</b>: 8 bytes. Magic <c>\0asm</c> (4 bytes),
    /// version (2 bytes, little-endian u16), layer (2 bytes, u16).
    /// <c>layer == 0x0001</c> is the discriminator — 0 means core
    /// module, 1 means component. Current component-binary version
    /// is <c>0x000d</c>.</para>
    ///
    /// <para>After the preamble comes a sequence of sections,
    /// each <c>{byte id, u32-LEB128 size, payload...}</c>. We
    /// collect the raw sections in order; structured decoding of
    /// each section lands incrementally in sibling readers.</para>
    /// </summary>
    public static class ComponentBinaryParser
    {
        /// <summary>Component magic + version/layer preamble —
        /// the bytes that unambiguously identify a component
        /// binary. Matches wasm-tools / componentize-dotnet
        /// output as of MVP (component-model 0.2.x).</summary>
        public const uint ComponentMagic = 0x6D736100; // '\0asm' LE
        public const ushort ComponentVersion = 0x000d;
        public const ushort ComponentLayer = 0x0001;
        public const ushort CoreModuleLayer = 0x0000;

        /// <summary>
        /// Quick check: does the first 8 bytes of <paramref name="header"/>
        /// identify a component binary? Used by the caller side
        /// (Wacs.Core/Modules/Module.cs routing) to dispatch between
        /// core-module and component parsers without fully consuming
        /// the stream.
        /// </summary>
        public static bool IsComponentHeader(ReadOnlySpan<byte> header)
        {
            if (header.Length < 8) return false;
            if (BitConverter.ToUInt32(header.Slice(0, 4)) != ComponentMagic)
                return false;
            // Bytes 6..8 form the layer word; component = 0x0001.
            var layer = (ushort)(header[6] | (header[7] << 8));
            return layer == ComponentLayer;
        }

        /// <summary>
        /// Parse a component binary from <paramref name="stream"/>,
        /// returning a <see cref="ComponentModule"/> populated with
        /// the raw section list. Structured decoding of per-section
        /// payloads is a later pass; right now the parser just
        /// validates the preamble and iterates the section headers
        /// so consumers know what's in the file.
        /// </summary>
        public static ComponentModule Parse(Stream stream)
        {
            var reader = new BinaryReader(stream);
            ReadPreamble(reader);

            var sections = new List<RawComponentSection>();
            while (stream.Position < stream.Length)
            {
                var id = (ComponentSectionId)reader.ReadByte();
                var size = ReadLeb128U32(reader);
                if (size > int.MaxValue)
                    throw new FormatException(
                        $"Component section size {size} exceeds int range.");
                var payload = reader.ReadBytes((int)size);
                if (payload.Length != (int)size)
                    throw new FormatException(
                        "Premature end of stream inside component section.");
                sections.Add(new RawComponentSection(id, payload));
            }

            return new ComponentModule(sections);
        }

        private static void ReadPreamble(BinaryReader reader)
        {
            var magic = reader.ReadUInt32();
            if (magic != ComponentMagic)
                throw new FormatException(
                    "Invalid magic number: not a WebAssembly binary.");

            var version = reader.ReadUInt16();
            var layer = reader.ReadUInt16();
            if (layer != ComponentLayer)
                throw new FormatException(
                    $"Expected component layer 0x{ComponentLayer:X4}, got 0x{layer:X4}. "
                    + "Use core-module parser for layer 0x0000 binaries.");
            if (version != ComponentVersion)
                throw new FormatException(
                    $"Unsupported component binary version 0x{version:X4}. "
                    + $"Expected 0x{ComponentVersion:X4} (MVP).");
        }

        /// <summary>
        /// Decode an unsigned LEB128 varuint32. Component sections
        /// use the same varuint encoding as core wasm sections — 7
        /// data bits per byte, high bit marks continuation.
        /// </summary>
        internal static uint ReadLeb128U32(BinaryReader reader)
        {
            uint value = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
                if (shift > 32)
                    throw new FormatException(
                        "LEB128 varuint32 exceeds 5-byte limit.");
            }
        }
    }
}
