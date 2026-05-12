// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Instructions;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    /// <summary>
    /// A single hint entry from the <c>metadata.code.branch_hint</c>
    /// custom section. The proposal pins the hint payload at one byte
    /// today (0x00 = unlikely-taken, 0x01 = likely-taken), but encodes
    /// it as a length-prefixed byte vector so future revisions can
    /// extend it. We keep the raw bytes alongside the decoded
    /// <see cref="HintByte"/> so consumers always have the full data
    /// even if a future hint shape isn't yet recognized.
    /// </summary>
    public readonly struct BranchHint
    {
        /// <summary>Function-body-relative byte offset of the hinted instruction.</summary>
        public readonly uint ByteOffset;

        /// <summary>Raw bytes from the hint payload — always at least one byte today.</summary>
        public readonly byte[] RawData;

        /// <summary>Convenience accessor for the first byte of the hint payload.</summary>
        public byte HintByte => RawData[0];

        /// <summary>
        /// True when the hinted branch is predicted to be taken
        /// (<c>0x01</c>); false for any other value, including the
        /// explicit unlikely (<c>0x00</c>) and any unrecognized payload.
        /// Callers that need to distinguish "hint absent" from
        /// "hint present and unlikely" should check for the hint via
        /// <see cref="Module.BranchHintSection"/> directly.
        /// </summary>
        public bool IsLikely => RawData.Length >= 1 && RawData[0] == 0x01;

        public BranchHint(uint byteOffset, byte[] rawData)
        {
            ByteOffset = byteOffset;
            RawData = rawData;
        }
    }

    public partial class Module
    {
        /// <summary>
        /// All hints captured from a <c>metadata.code.branch_hint</c>
        /// custom section, keyed by function index, then by the
        /// instruction's byte offset within its function body. Null
        /// when no such custom section was present in the module.
        /// </summary>
        public BranchHintMap? BranchHints { get; internal set; }

        /// <summary>
        /// Per-module storage for parsed branch hints. Wraps the
        /// keyed-by-funcidx dictionary so consumers can ask
        /// "is there any hint table at all" without two-level
        /// null-checks.
        /// </summary>
        public sealed class BranchHintMap
        {
            /// <summary>
            /// Per-function map from instruction byte-offset to its
            /// hint. The outer key is funcidx; the inner key is the
            /// instruction's offset within its function body
            /// (i.e. relative to the byte after the locals decl,
            /// matching the spec's "code body" coordinate). Populated
            /// by the binary parser. The WAT parser populates
            /// <see cref="ByInstruction"/> directly since WAT
            /// instructions don't have meaningful byte offsets.
            /// </summary>
            public Dictionary<uint, Dictionary<uint, BranchHint>> ByFuncIndex { get; }
                = new Dictionary<uint, Dictionary<uint, BranchHint>>();

            /// <summary>
            /// Direct hint-by-instruction-reference map. The
            /// authoritative lookup at use site (transpiler /
            /// interpreter), since it's populated for both binary
            /// (post-parse join via <see cref="JoinByInstruction"/>)
            /// and WAT (during parse) inputs. Reference equality on
            /// <see cref="InstructionBase"/> — safe because
            /// instructions are never duplicated within a module.
            /// </summary>
            public Dictionary<InstructionBase, BranchHint> ByInstruction { get; }
                = new Dictionary<InstructionBase, BranchHint>(InstRefEquality.Instance);

            // .NET 5+ ships System.Collections.Generic.ReferenceEqualityComparer,
            // but netstandard2.1 (one of our build targets) doesn't.
            // Hand-roll the trivial comparer; safe to share across maps.
            private sealed class InstRefEquality : IEqualityComparer<InstructionBase>
            {
                public static readonly InstRefEquality Instance = new InstRefEquality();
                public bool Equals(InstructionBase? x, InstructionBase? y) =>
                    ReferenceEquals(x, y);
                public int GetHashCode(InstructionBase obj) =>
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }

            /// <summary>Returns the hint for an instruction, or null.</summary>
            public BranchHint? TryGet(uint funcIdx, uint instrByteOffset)
            {
                if (ByFuncIndex.TryGetValue(funcIdx, out var fnMap)
                    && fnMap.TryGetValue(instrByteOffset, out var hint))
                {
                    return hint;
                }
                return null;
            }

            /// <summary>
            /// Returns the hint attached to a given instruction, or
            /// null. The transpiler's preferred lookup — uniform for
            /// binary and WAT inputs.
            /// </summary>
            public BranchHint? TryGet(InstructionBase inst) =>
                ByInstruction.TryGetValue(inst, out var h) ? h : (BranchHint?)null;

            /// <summary>
            /// Walk every instruction body in <paramref name="funcs"/>
            /// and, for each instruction whose body-relative byte
            /// offset matches a hint in <see cref="ByFuncIndex"/>,
            /// populate the instruction-keyed lookup. Idempotent — safe
            /// to call twice. Used by the binary parser's finalize
            /// step to bridge offset-keyed parse data to ref-keyed
            /// transpile data. Offsets are computed on the fly through
            /// <see cref="Wacs.Core.Bin.ByteOffsetWalker"/> rather than
            /// stored on the instruction instance, so the same
            /// singleton (<c>InstI32Const</c>, <c>InstLocalGet</c>, …)
            /// reused across functions doesn't race.
            /// </summary>
            public void JoinByInstruction(
                IEnumerable<(uint funcIdx, InstructionSequence body)> funcs)
            {
                foreach (var (funcIdx, body) in funcs)
                {
                    if (!ByFuncIndex.TryGetValue(funcIdx, out var fnMap))
                        continue;
                    Wacs.Core.Bin.ByteOffsetWalker.Walk(body, (inst, off) =>
                    {
                        if (fnMap.TryGetValue(off, out var hint))
                            ByInstruction[inst] = hint;
                        return false;
                    });
                }
            }
        }
    }

    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec WebAssembly Branch Hinting proposal v1
        /// https://github.com/WebAssembly/branch-hinting
        ///
        /// Custom section <c>"metadata.code.branch_hint"</c> payload:
        /// <code>
        ///   funcs : vec(func_hint)
        ///   func_hint    ::= func_idx:u32  hints:vec(hint)
        ///   hint         ::= byte_offset:u32  data:vec(byte)
        /// </code>
        ///
        /// We accept any <c>data</c> length (the proposal pins it at 1
        /// today but is structured for forward-compat) and capture
        /// every byte. Validation against the actual instruction stream
        /// (target must be <c>if</c>/<c>br_if</c>; no duplicate offsets
        /// per func) happens after both sections are parsed; this
        /// parser is permissive and round-trip-friendly.
        /// </summary>
        internal static Module.BranchHintMap ParseBranchHintSection(BinaryReader reader)
        {
            var map = new Module.BranchHintMap();
            uint funcCount = reader.ReadLeb128_u32();
            for (uint f = 0; f < funcCount; f++)
            {
                uint funcIdx = reader.ReadLeb128_u32();
                uint hintCount = reader.ReadLeb128_u32();
                var fnMap = new Dictionary<uint, BranchHint>((int)hintCount);
                for (uint h = 0; h < hintCount; h++)
                {
                    uint byteOffset = reader.ReadLeb128_u32();
                    uint dataLen = reader.ReadLeb128_u32();
                    var data = reader.ReadBytes((int)dataLen);
                    if (data.Length != (int)dataLen)
                        throw new FormatException(
                            $"branch_hint: short read on hint payload (expected {dataLen} bytes)");
                    if (fnMap.ContainsKey(byteOffset))
                        throw new FormatException(
                            $"branch_hint: duplicate hint at funcidx={funcIdx} offset={byteOffset}");
                    fnMap.Add(byteOffset, new BranchHint(byteOffset, data));
                }
                if (map.ByFuncIndex.ContainsKey(funcIdx))
                    throw new FormatException(
                        $"branch_hint: duplicate function entry funcidx={funcIdx}");
                map.ByFuncIndex.Add(funcIdx, fnMap);
            }
            return map;
        }
    }
}
