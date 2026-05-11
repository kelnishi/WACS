// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Text;

namespace Wacs.Core
{
    /// <summary>
    /// Identifies which module element a comment or annotation
    /// attaches to. <see cref="Kind"/> tells you the strata
    /// (module-level, a type, a function, etc.); <see cref="Index"/>
    /// is the 0-based index within that strata. For instruction-
    /// scoped attachments <see cref="InstructionIndex"/> is the
    /// 0-based offset within the owning function's flat body
    /// instruction stream.
    /// </summary>
    public enum ModuleElementKind : byte
    {
        Module = 0,
        Type = 1,
        Import = 2,
        Function = 3,
        Table = 4,
        Memory = 5,
        Tag = 6,
        Global = 7,
        Export = 8,
        Start = 9,
        Element = 10,
        Data = 11,
        /// <summary>An instruction within a function body. <c>Index</c>
        /// is the function index; <c>InstructionIndex</c> is the
        /// instruction offset within that body.</summary>
        Instruction = 12,
    }

    /// <summary>
    /// Stable handle for a module element used to key
    /// <see cref="Module.Annotations"/> / <see cref="Module.Comments"/>.
    /// </summary>
    public readonly struct ModuleElementRef
    {
        public readonly ModuleElementKind Kind;
        public readonly int Index;
        public readonly int InstructionIndex;

        public ModuleElementRef(ModuleElementKind kind, int index = -1, int instructionIndex = -1)
        {
            Kind = kind;
            Index = index;
            InstructionIndex = instructionIndex;
        }

        public static readonly ModuleElementRef ModuleLevel =
            new(ModuleElementKind.Module);

        public override int GetHashCode() =>
            ((int)Kind * 397) ^ Index ^ (InstructionIndex << 16);

        public override bool Equals(object? obj) =>
            obj is ModuleElementRef o
            && o.Kind == Kind && o.Index == Index
            && o.InstructionIndex == InstructionIndex;

        public override string ToString() => InstructionIndex >= 0
            ? $"{Kind}[{Index}]@{InstructionIndex}"
            : Index >= 0 ? $"{Kind}[{Index}]" : Kind.ToString();
    }

    /// <summary>
    /// A captured <c>(@name …)</c> annotation from a WAT source.
    /// Stored as opaque text (the raw payload between
    /// <c>(@name</c> and the matching <c>)</c>); structural
    /// validation is the consumer's job. The text-parser captures
    /// annotations alongside their owner so the writer can re-emit
    /// them at the right position.
    /// </summary>
    public sealed class WatAnnotation
    {
        /// <summary>The annotation name. For
        /// <c>(@metadata.code.branch_hint "\01")</c> this is
        /// <c>"metadata.code.branch_hint"</c>.</summary>
        public string Name { get; set; } = "";

        /// <summary>The raw payload text between the name and the
        /// closing paren of the annotation form. May be empty for
        /// bare <c>(@name)</c> annotations.</summary>
        public string Payload { get; set; } = "";

        /// <summary>Original source position — useful for error
        /// rendering and ordering on re-emit.</summary>
        public int Line { get; set; }
        public int Column { get; set; }
    }

    /// <summary>
    /// A comment captured from a WAT source, attached to a specific
    /// module element by <c>TextModuleParser</c> (in Pass 3). The
    /// <see cref="TriviaToken"/> carries the original delimiters and
    /// position; <see cref="Text"/> is the materialized lexeme.
    /// </summary>
    public sealed class WatComment
    {
        public TriviaKind Kind { get; set; }
        public string Text { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }

        /// <summary>
        /// Whether the comment was on the same line as its anchor
        /// element (a trailing comment) or on a preceding line (a
        /// leading comment). The writer uses this to choose between
        /// before-element and after-element placement.
        /// </summary>
        public bool IsTrailing { get; set; }
    }

    /// <summary>
    /// Round-trip side-tables for WAT comments and annotations. Both
    /// dictionaries are lazy — they stay null on a Module that was
    /// loaded from a comment-free source, so the bulk of WACS's
    /// allocation profile is unaffected.
    /// </summary>
    public partial class Module
    {
        /// <summary>
        /// <c>(@name …)</c> annotations parsed from the source.
        /// Each owner key may carry multiple annotations.
        /// </summary>
        public Dictionary<ModuleElementRef, List<WatAnnotation>>? Annotations { get; internal set; }

        /// <summary>
        /// <c>;;</c> line comments and <c>(;…;)</c> block comments
        /// parsed from the source, attached to the module element
        /// they preceded (or trailed on the same line).
        /// </summary>
        public Dictionary<ModuleElementRef, List<WatComment>>? Comments { get; internal set; }

        /// <summary>
        /// Append an annotation to <see cref="Annotations"/>, lazily
        /// allocating the table and the per-owner list.
        /// </summary>
        public void AddAnnotation(ModuleElementRef owner, WatAnnotation annot)
        {
            Annotations ??= new Dictionary<ModuleElementRef, List<WatAnnotation>>();
            if (!Annotations.TryGetValue(owner, out var list))
            {
                list = new List<WatAnnotation>(1);
                Annotations[owner] = list;
            }
            list.Add(annot);
        }

        /// <summary>
        /// Append a comment to <see cref="Comments"/>, lazily
        /// allocating the table and the per-owner list.
        /// </summary>
        public void AddComment(ModuleElementRef owner, WatComment comment)
        {
            Comments ??= new Dictionary<ModuleElementRef, List<WatComment>>();
            if (!Comments.TryGetValue(owner, out var list))
            {
                list = new List<WatComment>(1);
                Comments[owner] = list;
            }
            list.Add(comment);
        }
    }
}
