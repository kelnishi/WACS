// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;

namespace Wacs.Core.Text
{
    // AST for a parsed .wast file. A .wast is a sequence of top-level
    // commands: module definitions, module registrations, actions (invoke /
    // get), and assertions over those actions or modules.
    //
    // This layer is intentionally framework-agnostic: it produces structured
    // commands without attempting to execute them. A future spec-test runner
    // (Phase 3) adapts these to the existing `Spec.Test.Data.WastJson.ICommand`
    // execution path, or a fresh runner reads them directly.

    public abstract class ScriptCommand
    {
        /// <summary>1-based source line of the command's opening paren.</summary>
        public int Line;
        public int Column;
    }

    public enum ScriptModuleKind
    {
        /// <summary>(module …) — parsed by TextModuleParser into a Module.</summary>
        Text,
        /// <summary>(module binary "…") — raw binary bytes.</summary>
        Binary,
        /// <summary>(module quote "…") — reparse of a quoted text module.</summary>
        Quote,
        /// <summary>
        /// (module instance $alias $src) — component-model instantiation.
        /// Not a standalone module definition; shares content with its
        /// source module. Excluded from .wasm-file pairing.
        /// </summary>
        Instance,
    }

    public sealed class ScriptModule : ScriptCommand
    {
        /// <summary>Optional <c>$id</c> declared on the (module …) form.</summary>
        public string? Id;

        public ScriptModuleKind Kind;

        /// <summary>
        /// Populated for <see cref="ScriptModuleKind.Text"/>. For Quote kind
        /// the module is also parsed (from the quoted text) — this lets the
        /// runner access the parsed module uniformly.
        /// </summary>
        public Module? Module;

        /// <summary>
        /// Populated for <see cref="ScriptModuleKind.Binary"/> and
        /// <see cref="ScriptModuleKind.Quote"/>. Binary: the decoded bytes.
        /// Quote: the concatenated quoted-source bytes (UTF-8).
        /// </summary>
        public byte[]? Bytes;

        /// <summary>
        /// Populated when the script parser encountered an exception while
        /// parsing the inner module of an enclosing assert_invalid /
        /// assert_malformed / assert_unlinkable assertion (the inner module
        /// is *expected* to fail — see
        /// <c>TextScriptParser.ParseAssertModuleFailure</c>). The runner
        /// uses this to short-circuit the assertion as satisfied without
        /// re-parsing.
        /// </summary>
        public System.Exception? ParseError;
    }

    public sealed class ScriptRegister : ScriptCommand
    {
        /// <summary>The registered module-instance name (first operand).</summary>
        public string ExportName = "";

        /// <summary>
        /// Optional <c>$id</c> of the module being registered. Null ⇒ the
        /// latest declared module.
        /// </summary>
        public string? ModuleId;
    }

    public abstract class ScriptAction : ScriptCommand
    {
        /// <summary>Optional <c>$id</c> naming the source module.</summary>
        public string? ModuleId;

        /// <summary>Name of the export being invoked / inspected.</summary>
        public string ExportName = "";
    }

    public sealed class ScriptInvoke : ScriptAction
    {
        public List<ScriptValue> Args { get; } = new List<ScriptValue>();
    }

    public sealed class ScriptGet : ScriptAction { }

    // --- Assertions ----

    public sealed class ScriptAssertReturn : ScriptCommand
    {
        public ScriptAction Action = null!;
        public List<ScriptValue> Expected { get; } = new List<ScriptValue>();
    }

    public sealed class ScriptAssertTrap : ScriptCommand
    {
        /// <summary>
        /// Either an action (invoke/get) or a module (for trap-during-start)
        /// — <see cref="Action"/> is populated for the former, <see cref="Module"/>
        /// for the latter.
        /// </summary>
        public ScriptAction? Action;
        public ScriptModule? Module;
        public string ExpectedMessage = "";
    }

    public sealed class ScriptAssertExhaustion : ScriptCommand
    {
        public ScriptAction Action = null!;
        public string ExpectedMessage = "";
    }

    public sealed class ScriptAssertInvalid : ScriptCommand
    {
        public ScriptModule Module = null!;
        public string ExpectedMessage = "";
    }

    public sealed class ScriptAssertMalformed : ScriptCommand
    {
        public ScriptModule Module = null!;
        public string ExpectedMessage = "";
    }

    public sealed class ScriptAssertUnlinkable : ScriptCommand
    {
        public ScriptModule Module = null!;
        public string ExpectedMessage = "";
    }

    public sealed class ScriptAssertException : ScriptCommand
    {
        public ScriptAction Action = null!;
    }

    // --- Values ----

    public enum ScriptValueKind
    {
        I32, I64, F32, F64, V128,
        /// <summary>(ref.null <heaptype>) in argument/expected position.</summary>
        RefNull,
        /// <summary>(ref.extern N) — opaque external reference.</summary>
        RefExtern,
        /// <summary>(ref.func $f) — funcref.</summary>
        RefFunc,
        /// <summary>(ref.array) / (ref.struct) / (ref.any) / (ref.i31) / (ref.eq) — generic reftype patterns.</summary>
        RefGeneric,
        /// <summary>(either v1 v2 …) — assertion form for results that may legitimately be any of several values.</summary>
        Either,
    }

    public enum ScriptFloatPattern
    {
        None,
        /// <summary>nan:canonical — IEEE-754 canonical quiet NaN.</summary>
        NanCanonical,
        /// <summary>nan:arithmetic — any NaN-valued result.</summary>
        NanArithmetic,
    }

    public sealed class ScriptValue
    {
        public ScriptValueKind Kind;
        public int Line;
        public int Column;

        public int    I32;
        public long   I64;
        public float  F32;
        public double F64;

        /// <summary>
        /// Exact IEEE-754 bit pattern for a parsed f32 literal. Always
        /// populated alongside <see cref="F32"/>; the bits are
        /// authoritative when the literal carries information that
        /// can't survive a round-trip through <see cref="float"/>
        /// (NaN payloads, hex-float precision edges).
        /// </summary>
        public uint F32Bits;

        /// <summary>
        /// Exact IEEE-754 bit pattern for a parsed f64 literal. Same
        /// role as <see cref="F32Bits"/> for double precision.
        /// </summary>
        public ulong F64Bits;

        public byte[]? V128;

        /// <summary>
        /// Lane shape for a parsed (v128.const …) literal — one of
        /// "i8", "i16", "i32", "i64", "f32", "f64". Used by the
        /// runner adapter to emit the correct lane_type.
        /// </summary>
        public string? V128LaneType;

        /// <summary>
        /// String-encoded lane values for a parsed (v128.const …)
        /// literal, in the same form the JSON pipeline emits
        /// (decimal integers; floats as raw bit-pattern decimals;
        /// "nan:canonical"/"nan:arithmetic" for NaN patterns).
        /// </summary>
        public List<string>? V128Lanes;

        /// <summary>
        /// Alternatives for an <see cref="ScriptValueKind.Either"/>
        /// expected-value form. Each alternative is itself a fully
        /// typed ScriptValue.
        /// </summary>
        public List<ScriptValue>? EitherAlternatives;

        /// <summary>Heap-type token for <see cref="ScriptValueKind.RefNull"/> and <see cref="ScriptValueKind.RefGeneric"/>.</summary>
        public string? RefHeapType;

        /// <summary>Extern / func index for RefExtern / RefFunc.</summary>
        public string? RefId;

        /// <summary>
        /// Pattern marker for floats. When non-None, the <see cref="F32"/>
        /// / <see cref="F64"/> fields are unused — the assertion matches any
        /// value in the pattern class.
        /// </summary>
        public ScriptFloatPattern FloatPattern;
    }
}
