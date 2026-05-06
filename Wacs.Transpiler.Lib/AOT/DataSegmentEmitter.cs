// Copyright 2025 Kelvin Nishikawa
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
using Wacs.Core;
using Wacs.Core.Types;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes a WASM memory declaration from the module.
    /// </summary>
    public class MemoryDecl
    {
        public long MinPages { get; set; }
        public long MaxPages { get; set; }
    }

    /// <summary>
    /// Describes a WASM data segment for initialization.
    /// </summary>
    public class DataSegmentInfo
    {
        public int Index { get; set; }
        public int MemoryIndex { get; set; }
        public long Offset { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool IsPassive { get; set; }
        public Wacs.Core.Types.Expression? OffsetExpression { get; set; }

        /// <summary>
        /// Historic resource name from the legacy embedded-resource storage
        /// strategy. The RVA migration unified all storage strategies to a
        /// single shape (RVA-mapped fields under <c>__WACSAotData</c> and
        /// <c>__WACSInit.Data</c>); this name is no longer used by the
        /// emitter pipeline but is retained for diagnostics.
        /// </summary>
        public string ResourceName => $"data_segment_{Index}";
    }

    /// <summary>
    /// Walks the WASM module's memory + data sections and exposes the
    /// extracted metadata to the rest of the transpile pipeline. Storage
    /// emission itself is owned by <c>ModuleClassGenerator</c>
    /// (active segments → RVA-mapped <c>__WACSAotData.Segment_*</c>) and
    /// the codec blob (<c>__WACSInit.Data</c>); the historic per-strategy
    /// branches (CompressedResource / RawResource / StaticArrays) all
    /// converged on the same RVA shape and have been retired here.
    /// </summary>
    public class DataSegmentEmitter
    {
        private readonly WasmModule _wasmModule;
        private readonly DiagnosticCollector _diagnostics;

        public MemoryDecl[] Memories { get; private set; } = Array.Empty<MemoryDecl>();
        public DataSegmentInfo[] Segments { get; private set; } = Array.Empty<DataSegmentInfo>();

        /// <summary>
        /// Construct an emitter. <paramref name="strategy"/> is preserved
        /// for source compatibility with callers that still pass a
        /// <see cref="DataSegmentStorage"/> selection; the value is
        /// advisory and does not affect emission.
        /// </summary>
        public DataSegmentEmitter(
            WasmModule wasmModule,
            DataSegmentStorage strategy,
            DiagnosticCollector diagnostics)
        {
            _ = strategy;
            _wasmModule = wasmModule;
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// Extract memory declarations and data segments from the WASM module.
        /// </summary>
        public void Analyze()
        {
            // Memory declarations
            var memDecls = new System.Collections.Generic.List<MemoryDecl>();
            foreach (var mem in _wasmModule.Memories)
            {
                memDecls.Add(new MemoryDecl
                {
                    MinPages = mem.Limits.Minimum,
                    MaxPages = mem.Limits.Maximum ?? 65536
                });
            }
            Memories = memDecls.ToArray();

            // Data segments
            var segs = new System.Collections.Generic.List<DataSegmentInfo>();
            for (int i = 0; i < _wasmModule.Datas.Length; i++)
            {
                var data = _wasmModule.Datas[i];
                var info = new DataSegmentInfo
                {
                    Index = i,
                    Data = data.Init,
                    IsPassive = data.Mode is WasmModule.DataMode.PassiveMode
                };

                if (data.Mode is WasmModule.DataMode.ActiveMode active)
                {
                    info.MemoryIndex = (int)active.MemoryIndex.Value;
                    info.Offset = EvaluateConstOffset(active.Offset);
                    info.OffsetExpression = active.Offset;
                }

                segs.Add(info);
            }
            Segments = segs.ToArray();

            _diagnostics.Info($"Analyzed {Memories.Length} memories, {Segments.Length} data segments " +
                $"({Segments.Sum(s => s.Data.Length)} bytes total)");
        }

        /// <summary>
        /// Evaluate a constant expression (typically i32.const N) to get the offset.
        /// Handles the common case of a single constant instruction.
        /// </summary>
        private static long EvaluateConstOffset(Wacs.Core.Types.Expression expr)
        {
            foreach (var inst in expr.Instructions)
            {
                if (inst is Wacs.Core.Instructions.Numeric.InstI32Const i32)
                    return i32.Value;
                if (inst is Wacs.Core.Instructions.Numeric.InstI64Const i64)
                    return i64.FetchImmediate(null!);
            }
            return 0; // Default offset
        }

        /// <summary>
        /// Get the initialization data for the Module constructor.
        /// Returns the raw bytes per segment.
        /// </summary>
        public byte[] GetSegmentData(int index)
        {
            return Segments[index].Data;
        }
    }

    internal static class DataSegmentLinqExtensions
    {
        public static long Sum(this DataSegmentInfo[] segments, Func<DataSegmentInfo, int> selector)
        {
            long total = 0;
            foreach (var seg in segments) total += selector(seg);
            return total;
        }
    }
}
