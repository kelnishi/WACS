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
            // the raw store address. Also capture the absolute
            // FuncIdx + defined-function body so we can walk to find
            // the trap instruction's byte offset on demand.
            string funcLabel = "<func@?>";
            int funcIdx = -1;
            Module.Function? function = null;
            try
            {
                var addr = new FuncAddr((int)frame.FuncAddr);
                if (store.Contains(addr))
                {
                    var inst = store[addr];
                    funcLabel = string.IsNullOrEmpty(inst.Id)
                        ? $"func@{frame.FuncAddr}"
                        : $"${inst.Id}";
                    // Only concrete WASM functions carry a module-level
                    // FuncIdx; host shims implement IFunctionInstance
                    // without one. Pattern-match to keep the interface
                    // narrow.
                    if (inst is FunctionInstance fi)
                    {
                        funcIdx = (int)fi.Index.Value;
                        int defIdx = funcIdx - module.ImportedFunctions.Count;
                        if (defIdx >= 0 && defIdx < module.Funcs.Count)
                            function = module.Funcs[defIdx];
                    }
                }
            }
            catch
            {
                // The store may have been disposed or the address
                // may be stale — fall through to the placeholder.
            }
            sb.Append(funcLabel);

            // Instruction context — only the top frame carries it
            // directly. Lower frames would need a linker-PC-to-
            // call-site-instruction lookup; for now they're left at
            // function-level granularity.
            if (frame.Instruction != null)
            {
                // Walk the function body to find the trap instruction's
                // byte offset on demand. Replaces the prior approach of
                // storing offsets on InstructionBase, which raced when
                // instances are interned process-wide. For shared
                // singletons (i32.const, drop, …) the walk picks the
                // first reference match — same ambiguity as before, but
                // computed cleanly without parse-time stamping.
                uint? offset = function != null
                    ? Wacs.Core.Bin.ByteOffsetWalker.Find(
                        function.Body.Instructions, frame.Instruction)
                    : null;

                sb.Append(" (");
                sb.Append(frame.Instruction.Op.GetMnemonic());
                if (offset.HasValue)
                {
                    sb.Append(" @+0x");
                    sb.Append(offset.Value.ToString("x"));
                }
                sb.Append(')');

                // Verbose form: resolve source line / column.
                if (lineMap != null
                    || module.SourcePositions != null)
                {
                    var sourceCoord = TryResolveSourceCoord(
                        frame.Instruction, module, lineMap, funcIdx, offset);
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
            LineMap? lineMap,
            int funcIdx,
            uint? byteOffset)
        {
            // First preference: WAT-parsed module has direct per-
            // instruction source coords captured at parse.
            if (module.SourcePositions != null
                && module.SourcePositions.TryGetValue(inst, out var pos))
            {
                return $"({pos.Line}:{pos.Column})";
            }

            // Fallback for binary-parsed modules: per-instruction
            // spans recorded by `TextModuleWriter.WriteWithLineMap`
            // during a canonical re-render. Key is (funcIdx,
            // byteOffset) — funcIdx
            // identifies which function body, byteOffset pinpoints
            // the instruction within that body (computed via the
            // walker, same as the @+0x display above).
            if (lineMap != null && funcIdx >= 0 && byteOffset.HasValue)
            {
                var key = new ModuleElementRef(
                    ModuleElementKind.Instruction,
                    funcIdx,
                    (int)byteOffset.Value);
                var span = lineMap.ByElement(key);
                if (span.HasValue)
                    return $"({span.Value.StartLine}:{span.Value.StartCol})";
            }
            return null;
        }
    }
}
