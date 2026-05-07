// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Decoded component custom section. Custom sections carry
    /// tooling-specific metadata — <c>producers</c>, <c>name</c>,
    /// <c>component-type:xxx</c> (the embedded WIT text that
    /// <c>wasm-tools component embed</c> / <c>componentize-dotnet</c>
    /// write), and user-defined annotations. The parser recognizes
    /// the name + payload bytes; interpretation is deferred to
    /// consumers that care about specific kinds.
    /// </summary>
    public sealed class CustomSection
    {
        /// <summary>UTF-8 name string that identifies the custom
        /// section's kind (e.g. <c>producers</c>,
        /// <c>component-type:tiny</c>).</summary>
        public string Name { get; }

        /// <summary>Opaque payload following the name. Kind-specific
        /// decoders unpack this into structured metadata.</summary>
        public byte[] Data { get; }

        public CustomSection(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        /// <summary>Decode a <see cref="RawComponentSection"/>
        /// whose <see cref="RawComponentSection.Id"/> is
        /// <see cref="ComponentSectionId.Custom"/>. Format:
        /// <c>{name-len: varuint32} {name-utf8} {data...}</c>.
        /// </summary>
        public static CustomSection FromRaw(RawComponentSection raw)
        {
            if (raw.Id != ComponentSectionId.Custom)
                throw new System.ArgumentException(
                    "Not a custom section: " + raw.Id, nameof(raw));

            using var ms = new MemoryStream(raw.Payload);
            var reader = new BinaryReader(ms);
            var nameLen = ComponentBinaryParser.ReadLeb128U32(reader);
            if (nameLen > raw.Payload.Length)
                throw new FormatException(
                    "Custom section name length overruns payload.");
            var nameBytes = reader.ReadBytes((int)nameLen);
            var name = Encoding.UTF8.GetString(nameBytes);
            var data = reader.ReadBytes(raw.Payload.Length - (int)ms.Position);
            return new CustomSection(name, data);
        }
    }
}
