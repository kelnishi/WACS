// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Wacs.Core.Attributes;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Reverse lookup from WAT mnemonic string to <see cref="ByteCode"/>. Built
    /// once at type-init time by reflecting over the five real opcode enums
    /// (<see cref="OpCode"/>, <see cref="GcCode"/>, <see cref="ExtCode"/>,
    /// <see cref="SimdCode"/>, <see cref="AtomCode"/>) — the same enums whose
    /// <c>[OpCode("mnemonic")]</c> attributes already drive the render direction
    /// via <c>OpCodeExtensions.GetMnemonic</c>.
    ///
    /// <para>Exclusions:</para>
    /// <list type="bullet">
    ///   <item><see cref="WacsCode"/> is omitted — those are internal super-ops
    ///     produced by runtime rewriters, not part of the WebAssembly source
    ///     language.</item>
    ///   <item>SIMD entries with <c>Category == "prototype"</c> are omitted;
    ///     they are legacy opcode aliases that share their mnemonic with the
    ///     canonical variant.</item>
    ///   <item>Display-only GC mnemonics containing a space or paren (e.g.
    ///     <c>"ref.test (ref null)"</c>) are omitted; grammatically the WAT
    ///     form is <c>(ref.test (ref null $t))</c> — dispatch is driven by
    ///     operand shape, not a multi-word mnemonic.</item>
    /// </list>
    ///
    /// <para>Collisions:</para>
    /// <list type="bullet">
    ///   <item><c>select</c> / <c>select</c> (0x1B vs 0x1C) — both declare the
    ///     same mnemonic. The registry stores <see cref="OpCode.Select"/>; the
    ///     parser promotes to <see cref="OpCode.SelectT"/> when an inline
    ///     <c>(result …)</c> annotation is present.</item>
    /// </list>
    /// </summary>
    public static class Mnemonics
    {
        private static readonly Dictionary<string, ByteCode> Map;
        private static readonly Dictionary<string, (string Enum, string Field)> Source;

        static Mnemonics()
        {
            // Pre-size conservatively: ~700 real mnemonics across all enums.
            Map = new Dictionary<string, ByteCode>(1024, StringComparer.Ordinal);
            Source = new Dictionary<string, (string, string)>(1024, StringComparer.Ordinal);

            AddEnum<OpCode>(v => new ByteCode(v));
            AddEnum<GcCode>(v => new ByteCode(v));
            AddEnum<ExtCode>(v => new ByteCode(v));
            AddEnum<AtomCode>(v => new ByteCode(v));
            AddEnum<SimdCode>(v => new ByteCode(v));
            // WacsCode intentionally omitted.
        }

        /// <summary>
        /// True if <paramref name="mnemonic"/> is a known WAT opcode mnemonic.
        /// Collision-handling rules are documented on <see cref="Mnemonics"/>.
        /// </summary>
        public static bool TryLookup(string mnemonic, out ByteCode code) =>
            Map.TryGetValue(mnemonic, out code);

        /// <summary>
        /// The total number of registered mnemonics. Useful in tests to assert
        /// coverage as new proposals land.
        /// </summary>
        public static int Count => Map.Count;

        /// <summary>
        /// Diagnostic: which enum field a mnemonic resolved to. Returns null
        /// if the mnemonic is unknown.
        /// </summary>
        public static string? GetSourceLabel(string mnemonic)
        {
            if (Source.TryGetValue(mnemonic, out var s))
                return $"{s.Enum}.{s.Field}";
            return null;
        }

        private static void AddEnum<T>(Func<T, ByteCode> factory) where T : struct, Enum
        {
            var enumType = typeof(T);
            var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<OpCodeAttribute>();
                if (attr == null) continue;
                var mnemonic = attr.Mnemonic;
                // Filter SIMD prototype duplicates.
                if (!string.IsNullOrEmpty(attr.Category) && attr.Category == "prototype")
                    continue;
                // Filter display-only mnemonics (e.g. "ref.test (ref null)").
                if (mnemonic.IndexOf(' ') >= 0 || mnemonic.IndexOf('(') >= 0)
                    continue;
                // First-write-wins on collisions; matches the select/select-T
                // documented behaviour.
                if (Map.ContainsKey(mnemonic)) continue;

                var value = (T)field.GetValue(null)!;
                Map.Add(mnemonic, factory(value));
                Source.Add(mnemonic, (enumType.Name, field.Name));
            }
        }
    }
}
