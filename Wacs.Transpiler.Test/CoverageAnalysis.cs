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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    public class CoverageAnalysis
    {
        private readonly ITestOutputHelper _output;

        public CoverageAnalysis(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Report which unsupported opcodes block the most functions from being transpiled.
        /// </summary>
        [Fact]
        public void ReportBlockingOpcodes()
        {
            var specDir = FindSpecTestDir();
            if (string.IsNullOrEmpty(specDir) || !Directory.Exists(specDir))
            {
                _output.WriteLine("Skipped: no spec test files found");
                return;
            }

            // Track: for each opcode, how many functions does it block?
            var blockers = new Dictionary<string, int>();

            var wasmFiles = Directory.GetFiles(specDir, "*.wasm", SearchOption.AllDirectories)
                .OrderBy(f => f);

            foreach (var wasmPath in wasmFiles)
            {
                Module module;
                using (var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read))
                {
                    try { module = BinaryModuleParser.ParseWasm(stream); }
                    catch { continue; }
                }

                var runtime = new WasmRuntime();
                ModuleInstance moduleInst;
                try { moduleInst = runtime.InstantiateModule(module); }
                catch { continue; }

                foreach (var funcAddr in moduleInst.FuncAddrs)
                {
                    var func = runtime.GetFunction(funcAddr);
                    if (func is not FunctionInstance fi || fi.Module != moduleInst)
                        continue;

                    var blocker = FindFirstUnsupportedOpcode(fi.Body.Instructions);
                    if (blocker != null)
                    {
                        blockers.TryGetValue(blocker, out int count);
                        blockers[blocker] = count + 1;
                    }
                }
            }

            _output.WriteLine("Opcodes blocking transpilation (sorted by impact):");
            foreach (var kv in blockers.OrderByDescending(kv => kv.Value))
            {
                _output.WriteLine($"  {kv.Key}: {kv.Value} functions");
            }
        }

        private static string? FindFirstUnsupportedOpcode(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                var op = inst.Op.x00;

                // Multi-byte prefix
                if (op == OpCode.FB || op == OpCode.FC ||
                    op == OpCode.FD || op == OpCode.FE)
                {
                    return inst.Op.GetMnemonic();
                }

                // Numeric (0x41-0xC4) — supported
                byte b = (byte)op;
                if (b >= 0x41 && b <= 0xC4)
                    continue;

                // Supported control/variable/parametric
                if (op == OpCode.Unreachable || op == OpCode.Nop ||
                    op == OpCode.Block || op == OpCode.Loop ||
                    op == OpCode.If || op == OpCode.Else ||
                    op == OpCode.End || op == OpCode.Br ||
                    op == OpCode.BrIf || op == OpCode.BrTable ||
                    op == OpCode.Return ||
                    op == OpCode.LocalGet || op == OpCode.LocalSet ||
                    op == OpCode.LocalTee || op == OpCode.Drop ||
                    op == OpCode.Select || op == OpCode.SelectT)
                    continue;

                // Anything else is unsupported
                return inst.Op.GetMnemonic();
            }

            // Check nested blocks
            foreach (var inst in instructions)
            {
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var result = FindFirstUnsupportedOpcode(blockInst.GetBlock(i).Instructions);
                        if (result != null)
                            return result;
                    }
                }
            }

            return null;
        }

        private static string FindSpecTestDir()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "Spec.Test", "generated-json");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }
            return "";
        }
    }
}
