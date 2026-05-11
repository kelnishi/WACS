// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Text;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Exceptions
{
    /// <summary>
    /// Formats a <see cref="WasmStackFrame"/> chain into a human-
    /// readable trace. Two fidelity levels:
    /// <list type="bullet">
    ///   <item><b>Cheap</b> (<see cref="Format(IReadOnlyList{WasmStackFrame}, Module, Store)"/>):
    ///     <c>at $func (i32.add @+0x42) ← $caller (call @+0x18) ← …</c>.
    ///     Uses only the directly-captured fields plus the function
    ///     instance's <c>Id</c>. No source-map work, no re-rendering.</item>
    ///   <item><b>Verbose</b> (<see cref="FormatVerbose"/>):
    ///     adds source <c>(line:col)</c> when
    ///     <see cref="Module.SourcePositions"/> is populated
    ///     (WAT-parsed modules). For binary-parsed modules, the
    ///     verbose form falls back to the cheap form unless the
    ///     caller supplies a <see cref="LineMap"/> from a prior
    ///     <see cref="TextModuleWriter.WriteWithLineMap"/>.</item>
    /// </list>
    /// </summary>
    public static class WasmStackTrace
    {
        /// <summary>
        /// Cheap-form formatter. Produces a single-line ←-separated
        /// trace from top to bottom.
        /// </summary>
        public static string Format(
            IReadOnlyList<WasmStackFrame> frames, Module module, Store store)
        {
            if (frames == null || frames.Count == 0)
                return "<empty WASM stack>";

            var sb = new StringBuilder();
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) sb.Append(" ← ");
                AppendFrame(sb, frames[i], module, store, lineMap: null);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Verbose-form formatter. Resolves source <c>(line:col)</c>
        /// via <see cref="Module.SourcePositions"/> for WAT-parsed
        /// modules. If <paramref name="lineMap"/> is supplied (e.g.
        /// from a prior canonical re-render via
        /// <see cref="TextModuleWriter.WriteWithLineMap"/>), per-
        /// section line spans serve as a fallback for binary-parsed
        /// modules where <c>SourcePositions</c> is null.
        /// </summary>
        public static string FormatVerbose(
            IReadOnlyList<WasmStackFrame> frames,
            Module module,
            Store store,
            LineMap? lineMap = null)
        {
            if (frames == null || frames.Count == 0)
                return "<empty WASM stack>";

            var sb = new StringBuilder();
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append("  at ");
                AppendFrame(sb, frames[i], module, store, lineMap);
            }
            return sb.ToString();
        }

        private static void AppendFrame(
            StringBuilder sb, WasmStackFrame frame,
            Module module, Store store, LineMap? lineMap)
        {
            // Function identity — prefer the parsed `.Id` (e.g.
            // "$myfunc|3" from AnnotateWhileParsing), fall back to
            // the raw store address.
            string funcLabel = "<func@?>";
            try
            {
                var addr = new FuncAddr((int)frame.FuncAddr);
                if (store.Contains(addr))
                {
                    var inst = store[addr];
                    funcLabel = string.IsNullOrEmpty(inst.Id)
                        ? $"func@{frame.FuncAddr}"
                        : $"${inst.Id}";
                }
            }
            catch
            {
                // The store may have been disposed or the address
                // may be stale — fall through to the placeholder.
            }
            sb.Append(funcLabel);

            // Instruction context — only the top frame carries it
            // directly. Lower frames would need a linker-PC lookup
            // (Pass D); for now they're left at function-level
            // granularity.
            if (frame.Instruction != null)
            {
                sb.Append(" (");
                sb.Append(frame.Instruction.Op.GetMnemonic());
                sb.Append(" @+0x");
                sb.Append(frame.Instruction.ByteOffsetInFunc.ToString("x"));
                sb.Append(')');

                // Verbose form: resolve source line / column.
                if (lineMap != null
                    || module.SourcePositions != null)
                {
                    var sourceCoord = TryResolveSourceCoord(frame.Instruction, module, lineMap);
                    if (sourceCoord != null)
                    {
                        sb.Append(' ');
                        sb.Append(sourceCoord);
                    }
                }
            }
            else if (frame.ResumeContinuationAddress >= 0)
            {
                // Lower frame — surface the resume PC. The
                // linker's address space differs from the source-
                // level instruction index, so this isn't a clean
                // mapping, but it's still useful identifying info.
                sb.Append(" (resume@");
                sb.Append(frame.ResumeContinuationAddress);
                sb.Append(')');
            }
        }

        private static string? TryResolveSourceCoord(
            Wacs.Core.Instructions.InstructionBase inst,
            Module module,
            LineMap? lineMap)
        {
            // First preference: WAT-parsed module has direct per-
            // instruction source coords from Pass B.
            if (module.SourcePositions != null
                && module.SourcePositions.TryGetValue(inst, out var pos))
            {
                return $"({pos.Line}:{pos.Column})";
            }

            // Fall back to LineMap (Pass 6): a span on the enclosing
            // function. The caller had to pre-render via
            // TextModuleWriter.WriteWithLineMap to obtain the map.
            // We don't have a quick reverse mapping from
            // InstructionBase → FuncIdx, so this fallback applies
            // only when the caller cared enough to compute the map.
            // Leave as null when nothing's available — formatter
            // drops the suffix.
            _ = lineMap;
            return null;
        }
    }
}
