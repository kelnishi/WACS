// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Instructions;

namespace Wacs.Core
{
    /// <summary>
    /// A position in a parsed WAT source: 1-based line / column and
    /// the byte offset into the original source string. Populated by
    /// <see cref="Wacs.Core.Text.TextModuleParser"/> for every
    /// instruction it constructs, so trap / exception formatting can
    /// resolve a runtime frame to a source location without re-
    /// rendering the module.
    /// </summary>
    public readonly struct SourcePos
    {
        public readonly int Line;
        public readonly int Column;

        /// <summary>
        /// Byte offset into the original WAT source string. Useful
        /// for IDE / debugger tooling that needs an unambiguous
        /// position handle (line / column can repeat across files
        /// if re-rendered; the offset doesn't).
        /// </summary>
        public readonly int SourceOffset;

        public SourcePos(int line, int column, int sourceOffset)
        {
            Line = line;
            Column = column;
            SourceOffset = sourceOffset;
        }

        public override string ToString() => $"{Line}:{Column}";
    }

    public partial class Module
    {
        /// <summary>
        /// Per-instruction source positions captured by the WAT
        /// parser. Keyed by <see cref="InstructionBase"/> reference,
        /// so lookups are O(1) and the table only carries entries
        /// for the WAT-parsed function bodies (binary-parsed modules
        /// leave this null).
        ///
        /// <para>Memory cost is opt-in: the dictionary stays null on
        /// modules loaded from binary, and is lazy-allocated by the
        /// WAT parser only when it first emits an instruction. The
        /// hot interpreter loop never touches it.</para>
        ///
        /// <para>Consumed by
        /// <see cref="Wacs.Core.Runtime.Exceptions.WasmStackTrace"/>
        /// to resolve a frame's instruction to a source line / column.
        /// When this is null, the formatter re-renders the module via
        /// <see cref="Wacs.Core.Text.TextModuleWriter.WriteWithLineMap"/>
        /// (lazy, on demand) and resolves the instruction's body-
        /// relative byte offset through
        /// <see cref="Wacs.Core.Bin.ByteOffsetWalker"/>.</para>
        /// </summary>
        public Dictionary<InstructionBase, SourcePos>? SourcePositions { get; internal set; }

        /// <summary>
        /// Append a source position for <paramref name="inst"/>,
        /// lazy-allocating the table on first call. Used by the WAT
        /// parser; safe to call from outside the assembly for callers
        /// that build a Module by hand.
        /// </summary>
        public void RecordSourcePosition(InstructionBase inst, SourcePos pos)
        {
            SourcePositions ??= new Dictionary<InstructionBase, SourcePos>();
            SourcePositions[inst] = pos;
        }
    }
}
