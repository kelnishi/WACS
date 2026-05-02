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
            /// matching the spec's "code body" coordinate).
            /// </summary>
            public Dictionary<uint, Dictionary<uint, BranchHint>> ByFuncIndex { get; }
                = new Dictionary<uint, Dictionary<uint, BranchHint>>();

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
