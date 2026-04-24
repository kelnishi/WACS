// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    // Component-model type universe. The types in this file describe the
    // interface-type layer of the Component Model (spec:
    // https://github.com/WebAssembly/component-model/blob/main/design/mvp/
    // WIT.md). Both the WIT AST → Types conversion and the binary component
    // format parser produce values from this hierarchy; the canonical ABI,
    // the transpiler IL emitter, and the interpreter runtime all consume it.
    //
    // All component-model types are prefixed `Ct` to disambiguate from
    // `Wacs.Core.Types` — the transpiler's canonical-ABI emitters routinely
    // import both namespaces.
    //
    // Canonical ABI values are NOT representable in `Wacs.Core.Runtime.Value`
    // (which is a 12-byte union + GcRef, sized for core wasm primitives).
    // At runtime, component values are marshaled as boxed `object?` at the
    // host-binding boundary — same discipline as externref today. These
    // Types describe the *shape* of what's marshaled; runtime storage is
    // a separate concern.

    // ---- Primitive kinds -------------------------------------------------

    /// <summary>
    /// The 14 primitive component-model value types. Matches the
    /// <c>primitive-valtype</c> production of the WIT grammar.
    /// </summary>
    public enum CtPrim
    {
        Bool,
        S8, S16, S32, S64,
        U8, U16, U32, U64,
        F32, F64,
        Char,
        String,
    }

    // ---- Value type hierarchy --------------------------------------------

    /// <summary>
    /// Abstract base for every component-model interface-type term. Sealed
    /// subclasses cover the full valtype grammar. Visitor-style pattern
    /// matching is the intended consumption model — every consumer
    /// (canonical ABI, C# emitter, interpreter) dispatches on the subclass.
    /// </summary>
    public abstract class CtValType
    {
    }

    /// <summary>A primitive type (<c>bool</c>, <c>u32</c>, <c>string</c>, …).</summary>
    public sealed class CtPrimType : CtValType
    {
        public CtPrim Kind { get; }
        public CtPrimType(CtPrim kind) { Kind = kind; }

        // Shared singletons for the fixed primitive set — construct-free
        // and GC-friendly in the hot paths.
        public static readonly CtPrimType Bool = new CtPrimType(CtPrim.Bool);
        public static readonly CtPrimType S8   = new CtPrimType(CtPrim.S8);
        public static readonly CtPrimType S16  = new CtPrimType(CtPrim.S16);
        public static readonly CtPrimType S32  = new CtPrimType(CtPrim.S32);
        public static readonly CtPrimType S64  = new CtPrimType(CtPrim.S64);
        public static readonly CtPrimType U8   = new CtPrimType(CtPrim.U8);
        public static readonly CtPrimType U16  = new CtPrimType(CtPrim.U16);
        public static readonly CtPrimType U32  = new CtPrimType(CtPrim.U32);
        public static readonly CtPrimType U64  = new CtPrimType(CtPrim.U64);
        public static readonly CtPrimType F32  = new CtPrimType(CtPrim.F32);
        public static readonly CtPrimType F64  = new CtPrimType(CtPrim.F64);
        public static readonly CtPrimType Char = new CtPrimType(CtPrim.Char);
        public static readonly CtPrimType String = new CtPrimType(CtPrim.String);
    }

    /// <summary><c>list&lt;T&gt;</c> — homogeneous sequence.</summary>
    public sealed class CtListType : CtValType
    {
        public CtValType Element { get; }
        public CtListType(CtValType element) { Element = element; }
    }

    /// <summary><c>option&lt;T&gt;</c> — one-of {none, some(T)}.</summary>
    public sealed class CtOptionType : CtValType
    {
        public CtValType Inner { get; }
        public CtOptionType(CtValType inner) { Inner = inner; }
    }

    /// <summary>
    /// <c>result&lt;T, E&gt;</c> — one-of {ok(T), err(E)}. Either side can
    /// be elided: <c>result</c>, <c>result&lt;_, E&gt;</c>, and
    /// <c>result&lt;T&gt;</c> map to <see cref="Ok"/> / <see cref="Err"/>
    /// null for the missing side.
    /// </summary>
    public sealed class CtResultType : CtValType
    {
        public CtValType? Ok { get; }
        public CtValType? Err { get; }
        public CtResultType(CtValType? ok, CtValType? err) { Ok = ok; Err = err; }
    }

    /// <summary><c>tuple&lt;T1, T2, …&gt;</c> — fixed-arity heterogeneous product.</summary>
    public sealed class CtTupleType : CtValType
    {
        public IReadOnlyList<CtValType> Elements { get; }
        public CtTupleType(IReadOnlyList<CtValType> elements) { Elements = elements; }
    }

    // ---- User-defined aggregates -----------------------------------------

    /// <summary>
    /// A record field. <c>record point { x: u32, y: u32 }</c> → two fields.
    /// </summary>
    public sealed class CtField
    {
        public string Name { get; }
        public CtValType Type { get; }
        public CtField(string name, CtValType type) { Name = name; Type = type; }
    }

    /// <summary><c>record Name { field1: T1, field2: T2, … }</c></summary>
    public sealed class CtRecordType : CtValType
    {
        public string Name { get; }
        public IReadOnlyList<CtField> Fields { get; }
        public CtRecordType(string name, IReadOnlyList<CtField> fields)
        {
            Name = name;
            Fields = fields;
        }
    }

    /// <summary>
    /// A single case in a <c>variant</c>. Payload is <c>null</c> for the
    /// no-payload form (<c>case foo</c>).
    /// </summary>
    public sealed class CtVariantCase
    {
        public string Name { get; }
        public CtValType? Payload { get; }
        public CtVariantCase(string name, CtValType? payload) { Name = name; Payload = payload; }
    }

    /// <summary><c>variant Name { a, b(u32), c(string), … }</c></summary>
    public sealed class CtVariantType : CtValType
    {
        public string Name { get; }
        public IReadOnlyList<CtVariantCase> Cases { get; }
        public CtVariantType(string name, IReadOnlyList<CtVariantCase> cases)
        {
            Name = name;
            Cases = cases;
        }
    }

    /// <summary>
    /// <c>enum Name { a, b, c }</c>. Despecializes to <see cref="CtVariantType"/>
    /// with no payloads, but kept distinct for shape preservation.
    /// </summary>
    public sealed class CtEnumType : CtValType
    {
        public string Name { get; }
        public IReadOnlyList<string> Cases { get; }
        public CtEnumType(string name, IReadOnlyList<string> cases)
        {
            Name = name;
            Cases = cases;
        }
    }

    /// <summary><c>flags Name { a, b, c }</c> — bitfield.</summary>
    public sealed class CtFlagsType : CtValType
    {
        public string Name { get; }
        public IReadOnlyList<string> Flags { get; }
        public CtFlagsType(string name, IReadOnlyList<string> flags)
        {
            Name = name;
            Flags = flags;
        }
    }

    // ---- Resources -------------------------------------------------------

    /// <summary>
    /// How a resource method is invoked. Constructors and statics don't
    /// receive a <c>self</c> handle; instance methods do.
    /// </summary>
    public enum CtResourceMethodKind
    {
        Constructor,
        Static,
        Instance,
    }

    /// <summary>
    /// A method on a resource type. Matches the WIT form:
    /// <code>
    ///   constructor(params);
    ///   static name: func(params) -&gt; result;
    ///   name: func(params) -&gt; result;
    /// </code>
    /// </summary>
    public sealed class CtResourceMethod
    {
        public string? Name { get; }
        public CtResourceMethodKind Kind { get; }
        public CtFunctionType Function { get; }

        public CtResourceMethod(string? name, CtResourceMethodKind kind,
                                CtFunctionType function)
        {
            Name = name;
            Kind = kind;
            Function = function;
        }
    }

    /// <summary>
    /// <c>resource Name { constructor; method: func(...) -&gt; ...; … }</c>.
    /// Handles are opaque at the component boundary; only the host sees
    /// the underlying representation via resource.rep.
    /// </summary>
    public sealed class CtResourceType : CtValType
    {
        public string Name { get; }
        public IReadOnlyList<CtResourceMethod> Methods { get; }
        public CtResourceType(string name, IReadOnlyList<CtResourceMethod> methods)
        {
            Name = name;
            Methods = methods;
        }
    }

    /// <summary>
    /// <c>own&lt;R&gt;</c> — owning handle; drops the resource when dropped.
    /// The target resource is held as a <see cref="CtValType"/> to
    /// accommodate forward references during single-pass conversion.
    /// After resolution, expect a <see cref="CtResourceType"/> directly
    /// or a <see cref="CtTypeRef"/> whose target wraps one.
    /// </summary>
    public sealed class CtOwnType : CtValType
    {
        public CtValType Resource { get; }
        public CtOwnType(CtValType resource) { Resource = resource; }
    }

    /// <summary>
    /// <c>borrow&lt;R&gt;</c> — non-owning handle, valid only for the
    /// duration of the call it was passed to. The binding surface
    /// enforces call-scoped lifetime structurally (handle invalidated on
    /// return). Like <see cref="CtOwnType"/>, <see cref="Resource"/> may
    /// be a <see cref="CtTypeRef"/> before resolution.
    /// </summary>
    public sealed class CtBorrowType : CtValType
    {
        public CtValType Resource { get; }
        public CtBorrowType(CtValType resource) { Resource = resource; }
    }

    // ---- Named references ------------------------------------------------

    /// <summary>
    /// A reference to a named type defined elsewhere (in the same
    /// interface, or imported via <c>use</c>). Single-file conversion
    /// produces unresolved refs keyed by name; later phases resolve them
    /// across interfaces and packages.
    /// </summary>
    public sealed class CtTypeRef : CtValType
    {
        /// <summary>
        /// The qualified name of the target. For a type defined in the
        /// same interface this is just the type's local name; for a
        /// <c>use pkg:ns/iface@ver.{X}</c> import it's the imported name
        /// (with optional <c>as</c> alias substituted).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The bound target, set once resolution runs. <c>null</c> on
        /// freshly-converted trees — the binding comes later.
        /// </summary>
        public CtNamedType? Target { get; set; }

        public CtTypeRef(string name) { Name = name; }
    }

    /// <summary>
    /// A named type definition — pairs a declared name with its
    /// underlying value type. Every <c>record</c>, <c>variant</c>,
    /// <c>enum</c>, <c>flags</c>, <c>resource</c>, or <c>type</c>
    /// alias in a WIT file becomes one of these.
    ///
    /// <para><see cref="Type"/> is settable only from within
    /// <c>Wacs.ComponentModel</c> so the converter can fill in the
    /// body during its second pass (resolving forward references
    /// inside an interface). Once the converter returns, the body is
    /// fixed.</para>
    /// </summary>
    public sealed class CtNamedType
    {
        public string Name { get; }
        public CtValType Type { get; internal set; }

        /// <summary>
        /// The interface that declared this type, if any. World-level
        /// types leave this null.
        /// </summary>
        public CtInterfaceType? Owner { get; set; }

        public CtNamedType(string name, CtValType type) { Name = name; Type = type; }
    }
}
