// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;

namespace Wacs.ComponentModel.WIT
{
    // AST for the WIT (WebAssembly Interface Types) IDL used by the component
    // model. Reference: https://github.com/WebAssembly/component-model/blob/
    // main/design/mvp/WIT.md
    //
    // A .wit source file is a single top-level document whose body is one or
    // more packages; a package owns a set of interfaces and worlds; interfaces
    // and worlds own types, functions, imports, exports, and `use` imports.
    // All these AST nodes carry a source span for diagnostics.

    /// <summary>
    /// Source span carried on every AST node. Line/column are 1-based.
    /// </summary>
    public readonly struct WitSpan
    {
        public readonly int Line;
        public readonly int Column;
        public WitSpan(int line, int column) { Line = line; Column = column; }
        public override string ToString() => $"{Line}:{Column}";
    }

    public abstract class WitNode
    {
        public WitSpan Span { get; internal set; }
    }

    // ---- Document root ---------------------------------------------------

    /// <summary>
    /// A parsed .wit document. Typically holds a single package (file-level
    /// form) but the grammar also allows multi-package documents with
    /// explicit <c>package … { … }</c> blocks.
    /// </summary>
    public sealed class WitDocument : WitNode
    {
        public List<WitPackage> Packages { get; } = new List<WitPackage>();

        /// <summary>
        /// Top-level <c>use</c> statements — shorthand for pulling names into
        /// all worlds in the document.
        /// </summary>
        public List<WitUse> TopLevelUses { get; } = new List<WitUse>();
    }

    // ---- Packages --------------------------------------------------------

    public sealed class WitPackage : WitNode
    {
        public WitPackageName Name { get; set; } = null!;
        public List<WitInterface> Interfaces { get; } = new List<WitInterface>();
        public List<WitWorld> Worlds { get; } = new List<WitWorld>();

        /// <summary>
        /// True if this package was declared with a `package foo:bar { … }`
        /// block syntax (explicit body); false if it's the implicit package
        /// declared by a bare `package foo:bar;` header at file top.
        /// </summary>
        public bool HasExplicitBody { get; set; }
    }

    public sealed class WitPackageName : WitNode
    {
        /// <summary>
        /// Namespace prefix — the segment before the <c>:</c>. Empty when
        /// the name lacks a namespace.
        /// </summary>
        public string Namespace { get; set; } = "";

        /// <summary>
        /// Package name — segment after the <c>:</c> and any additional
        /// <c>:</c>-separated path segments. Flattened to dotted form.
        /// </summary>
        public List<string> Path { get; } = new List<string>();

        /// <summary>
        /// Optional semver suffix (after <c>@</c>). Null if absent.
        /// </summary>
        public WitVersion? Version { get; set; }
    }

    public sealed class WitVersion : WitNode
    {
        public int Major, Minor, Patch;
        public string? Prerelease;   // "-alpha.1" without leading dash
        public string? Build;        // "+sha.abc" without leading plus

        public override string ToString()
        {
            var s = $"{Major}.{Minor}.{Patch}";
            if (!string.IsNullOrEmpty(Prerelease)) s += "-" + Prerelease;
            if (!string.IsNullOrEmpty(Build)) s += "+" + Build;
            return s;
        }
    }

    // ---- Interfaces ------------------------------------------------------

    public sealed class WitInterface : WitNode
    {
        public string Name { get; set; } = "";
        public List<WitInterfaceItem> Items { get; } = new List<WitInterfaceItem>();
    }

    public abstract class WitInterfaceItem : WitNode { }

    /// <summary>
    /// A named type definition inside an interface. Types are nominal —
    /// references to this name from outside resolve through the package /
    /// use graph.
    /// </summary>
    public sealed class WitTypeDef : WitInterfaceItem
    {
        public string Name { get; set; } = "";
        public WitType Type { get; set; } = null!;

        /// <summary>
        /// Block of <c>///</c> doc-comment lines immediately
        /// preceding the declaration. Preserved verbatim (one
        /// entry per source line, leading <c>///</c> + optional
        /// one space stripped). Null when no doc comment is
        /// present.
        /// </summary>
        public List<string>? DocLines { get; set; }
    }

    /// <summary>
    /// A function signature inside an interface.
    /// </summary>
    public sealed class WitFunction : WitInterfaceItem
    {
        public string Name { get; set; } = "";
        public List<WitParam> Params { get; } = new List<WitParam>();

        /// <summary>
        /// Return type. A function may have no result (void-return), a single
        /// anonymous result (set <see cref="Result"/>), or a named tuple of
        /// results (populate <see cref="NamedResults"/>). Only one of the two
        /// fields is non-null for a given function.
        /// </summary>
        public WitType? Result { get; set; }

        public List<WitParam>? NamedResults { get; set; }
    }

    /// <summary>
    /// <c>use pkg:iface.{name as alias, other}</c> form. May appear
    /// top-level, inside an interface, or inside a world.
    /// </summary>
    public sealed class WitUse : WitInterfaceItem
    {
        public WitUsePath Path { get; set; } = null!;
        public List<WitUsedName> Names { get; } = new List<WitUsedName>();
    }

    public sealed class WitUsePath : WitNode
    {
        public WitPackageName? Package { get; set; }   // null when path is a bare interface name in same package
        public string InterfaceName { get; set; } = "";
    }

    public sealed class WitUsedName : WitNode
    {
        public string Name { get; set; } = "";
        public string? Alias { get; set; }
    }

    // ---- Worlds ----------------------------------------------------------

    public sealed class WitWorld : WitNode
    {
        public string Name { get; set; } = "";
        public List<WitWorldItem> Items { get; } = new List<WitWorldItem>();
    }

    public abstract class WitWorldItem : WitNode { }

    public sealed class WitWorldImport : WitWorldItem
    {
        public WitExternSpec Spec { get; set; } = null!;
    }

    public sealed class WitWorldExport : WitWorldItem
    {
        public WitExternSpec Spec { get; set; } = null!;
    }

    public sealed class WitWorldUse : WitWorldItem
    {
        public WitUse Use { get; set; } = null!;
    }

    public sealed class WitWorldTypeDef : WitWorldItem
    {
        public WitTypeDef TypeDef { get; set; } = null!;
    }

    /// <summary>
    /// <c>include pkg:world with { oldname as newname, … }</c>. Merges the
    /// named world's items into this world, with optional renaming.
    /// </summary>
    public sealed class WitWorldInclude : WitWorldItem
    {
        public WitUsePath Path { get; set; } = null!;
        public List<WitRename> With { get; } = new List<WitRename>();
    }

    public sealed class WitRename : WitNode
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    /// <summary>
    /// An import/export descriptor's body. Four syntactic variants:
    /// <c>name: func(...)</c>, <c>name: interface { … }</c>,
    /// <c>name: pkg:iface</c>, or just a bare interface reference
    /// <c>pkg:iface</c> (no colon-prefixed name).
    /// </summary>
    public abstract class WitExternSpec : WitNode
    {
        /// <summary>
        /// The binding name as it appears in the world. For bare interface
        /// references this is derived from the interface's local name.
        /// </summary>
        public string Name { get; set; } = "";
    }

    public sealed class WitExternFunc : WitExternSpec
    {
        public WitFunction Function { get; set; } = null!;
    }

    public sealed class WitExternInterfaceRef : WitExternSpec
    {
        public WitUsePath Path { get; set; } = null!;
    }

    public sealed class WitExternInlineInterface : WitExternSpec
    {
        public WitInterface Interface { get; set; } = null!;
    }

    // ---- Types -----------------------------------------------------------

    public abstract class WitType : WitNode { }

    public enum WitPrim
    {
        Bool,
        S8,  U8,
        S16, U16,
        S32, U32,
        S64, U64,
        F32, F64,
        Char,
        String,
    }

    public sealed class WitPrimType : WitType
    {
        public WitPrim Kind { get; set; }
    }

    public sealed class WitListType : WitType
    {
        public WitType Element { get; set; } = null!;
    }

    public sealed class WitOptionType : WitType
    {
        public WitType Inner { get; set; } = null!;
    }

    public sealed class WitResultType : WitType
    {
        public WitType? Ok  { get; set; }   // null → no payload
        public WitType? Err { get; set; }
    }

    public sealed class WitTupleType : WitType
    {
        public List<WitType> Elements { get; } = new List<WitType>();
    }

    public sealed class WitRecordType : WitType
    {
        public List<WitField> Fields { get; } = new List<WitField>();
    }

    public sealed class WitField : WitNode
    {
        public string Name { get; set; } = "";
        public WitType Type { get; set; } = null!;
    }

    public sealed class WitVariantType : WitType
    {
        public List<WitVariantCase> Cases { get; } = new List<WitVariantCase>();
    }

    public sealed class WitVariantCase : WitNode
    {
        public string Name { get; set; } = "";
        public WitType? Payload { get; set; }
    }

    public sealed class WitEnumType : WitType
    {
        public List<string> Cases { get; } = new List<string>();
    }

    public sealed class WitFlagsType : WitType
    {
        public List<string> Flags { get; } = new List<string>();
    }

    public sealed class WitResourceType : WitType
    {
        public List<WitResourceMethod> Methods { get; } = new List<WitResourceMethod>();
    }

    public enum WitResourceMethodKind
    {
        Instance,
        Static,
        Constructor,
    }

    public sealed class WitResourceMethod : WitNode
    {
        public string Name { get; set; } = "";   // empty for constructor
        public WitResourceMethodKind Kind { get; set; }
        public List<WitParam> Params { get; } = new List<WitParam>();
        public WitType? Result { get; set; }
        public List<WitParam>? NamedResults { get; set; }
    }

    public sealed class WitOwnType : WitType
    {
        /// <summary>Name of the resource type this handle refers to.</summary>
        public string ResourceName { get; set; } = "";
    }

    public sealed class WitBorrowType : WitType
    {
        public string ResourceName { get; set; } = "";
    }

    /// <summary>
    /// A bare identifier used where a type is expected — refers to some type
    /// declared earlier in the same interface / world, or imported via
    /// <c>use</c>. Resolution is deferred to a later pass (out of scope for
    /// phase 1.7 parsing).
    /// </summary>
    public sealed class WitTypeRef : WitType
    {
        public string Name { get; set; } = "";
    }

    public sealed class WitParam : WitNode
    {
        public string Name { get; set; } = "";
        public WitType Type { get; set; } = null!;
    }
}
