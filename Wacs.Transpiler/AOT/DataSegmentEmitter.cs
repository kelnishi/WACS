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
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
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
        /// Resource name when stored as embedded resource.
        /// </summary>
        public string ResourceName => $"data_segment_{Index}";
    }

    /// <summary>
    /// Emits data segment storage and memory initialization logic for the transpiled assembly.
    ///
    /// Strategies:
    /// - CompressedResource: Brotli-compressed embedded resources. Smallest assembly.
    /// - RawResource: uncompressed embedded resources. Fastest instantiation.
    /// - StaticArrays: static readonly byte[] fields. Simplest, largest.
    ///
    /// Also emits the memory allocation logic (byte[] per memory, sized from memory section).
    /// </summary>
    public class DataSegmentEmitter
    {
        private readonly DataSegmentStorage _strategy;
        private readonly WasmModule _wasmModule;
        private readonly DiagnosticCollector _diagnostics;

        public MemoryDecl[] Memories { get; private set; } = Array.Empty<MemoryDecl>();
        public DataSegmentInfo[] Segments { get; private set; } = Array.Empty<DataSegmentInfo>();
        public FieldBuilder[] StaticFields { get; private set; } = Array.Empty<FieldBuilder>();

        public DataSegmentEmitter(
            WasmModule wasmModule,
            DataSegmentStorage strategy,
            DiagnosticCollector diagnostics)
        {
            _wasmModule = wasmModule;
            _strategy = strategy;
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
        /// Emit the data segment storage into the assembly.
        /// Call this before the Module class constructor is emitted.
        /// </summary>
        public void Emit(TypeBuilder functionsType, ModuleBuilder moduleBuilder)
        {
            switch (_strategy)
            {
                case DataSegmentStorage.StaticArrays:
                    EmitStaticArrays(functionsType);
                    break;
                case DataSegmentStorage.RawResource:
                    EmitResources(moduleBuilder, compress: false);
                    break;
                case DataSegmentStorage.CompressedResource:
                    EmitResources(moduleBuilder, compress: true);
                    break;
            }
        }

        private void EmitStaticArrays(TypeBuilder functionsType)
        {
            StaticFields = new FieldBuilder[Segments.Length];
            for (int i = 0; i < Segments.Length; i++)
            {
                var seg = Segments[i];
                if (seg.Data.Length == 0) continue;

                var field = functionsType.DefineField(
                    $"_dataSegment{i}",
                    typeof(byte[]),
                    FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly);
                StaticFields[i] = field;

                _diagnostics.Info($"Data segment {i}: {seg.Data.Length} bytes as static array",
                    opcode: "data.static");
            }
        }

        private void EmitResources(ModuleBuilder moduleBuilder, bool compress)
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                var seg = Segments[i];
                if (seg.Data.Length == 0) continue;

                string suffix = compress ? ".br" : ".raw";
                string name = $"{seg.ResourceName}{suffix}";

                // Note: dynamic assemblies (AssemblyBuilderAccess.Run) don't support
                // DefineManifestResource. Resources require PersistedAssemblyBuilder (.NET 9+)
                // or saving to disk. For now, store the data in the DataSegmentInfo
                // and the Module constructor will access it from there.
                // When persisting assemblies, resources can be properly embedded.

                if (compress)
                {
                    using var ms = new MemoryStream();
                    using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal))
                    {
                        brotli.Write(seg.Data, 0, seg.Data.Length);
                    }
                    var compressed = ms.ToArray();
                    double ratio = seg.Data.Length > 0
                        ? (double)compressed.Length / seg.Data.Length * 100 : 0;
                    _diagnostics.Info(
                        $"Data segment {i}: {seg.Data.Length} bytes ��� {compressed.Length} bytes " +
                        $"({ratio:F1}% compressed)", opcode: "data.brotli");
                }
                else
                {
                    _diagnostics.Info($"Data segment {i}: {seg.Data.Length} bytes as raw resource",
                        opcode: "data.raw");
                }
            }
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
        /// Returns the raw bytes per segment (decompressed if needed).
        /// For dynamic assemblies, the data is kept in-memory in DataSegmentInfo.
        /// For persisted assemblies, this would read from resources.
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
