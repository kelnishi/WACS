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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
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
        /// Report which unsupported opcodes block the most functions.
        /// Uses the actual transpiler to determine what's unsupported.
        /// </summary>
        [Fact]
        public void ReportBlockingOpcodes()
        {
            var provider = new WastTestDataProvider();
            var blockers = new Dictionary<string, int>();

            foreach (var wasmPath in provider.GetWasmFiles())
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

                var transpiler = new ModuleTranspiler();
                TranspilationResult result;
                try { result = transpiler.Transpile(moduleInst, runtime); }
                catch { continue; }

                // For each non-transpiled function, find the first unsupported opcode
                int importCount = 0;
                bool foundLocal = false;
                foreach (var funcAddr in moduleInst.FuncAddrs)
                {
                    var func = runtime.GetFunction(funcAddr);
                    if (func is FunctionInstance fi && fi.Module == moduleInst)
                    {
                        foundLocal = true;
                        int localIdx = (int)fi.Index.Value - importCount;
                        if (localIdx >= 0 && localIdx < result.Manifest.Functions.Count)
                        {
                            var entry = result.Manifest.Functions[localIdx];
                            if (!entry.IsTranspiled)
                            {
                                var blocker = FindFirstUnsupportedInBody(fi);
                                if (blocker != null)
                                {
                                    blockers.TryGetValue(blocker, out int count);
                                    blockers[blocker] = count + 1;
                                }
                            }
                        }
                    }
                    else if (!foundLocal)
                    {
                        importCount++;
                    }
                }
            }

            _output.WriteLine("Opcodes blocking transpilation (sorted by impact):");
            foreach (var kv in blockers.OrderByDescending(kv => kv.Value))
            {
                _output.WriteLine($"  {kv.Key}: {kv.Value} functions");
            }
        }

        /// <summary>
        /// Walk the instruction tree and return the mnemonic of the first
        /// opcode that causes the transpiler to reject the function.
        /// </summary>
        private static string? FindFirstUnsupportedInBody(FunctionInstance fi)
        {
            return FindFirst(fi.Body.Instructions);
        }

        private static string? FindFirst(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                // Use the mnemonic for any unsupported opcode.
                // The transpiler's HasEmitter is the source of truth,
                // but we approximate here since it's internal.
                var op = inst.Op.x00;

                // Prefix opcodes: report the full prefixed mnemonic
                if (op == Wacs.Core.OpCodes.OpCode.FB ||
                    op == Wacs.Core.OpCodes.OpCode.FD ||
                    op == Wacs.Core.OpCodes.OpCode.FE)
                    return inst.Op.GetMnemonic();

                // 0xFC: sat trunc (0x00-0x07) and bulk ops (0x08-0x11) are supported
                if (op == Wacs.Core.OpCodes.OpCode.FC)
                {
                    byte ext = (byte)inst.Op.xFC;
                    if (ext <= 0x11) continue;
                    return inst.Op.GetMnemonic();
                }

                // Single-byte: anything not in the handled ranges
                byte b = (byte)op;
                if (b >= 0x00 && b <= 0x1F) continue; // control + exception + try_table
                if (b >= 0x1A && b <= 0x1C) continue; // drop, select
                if (b >= 0x20 && b <= 0x26) continue; // locals, globals, table.get/set
                if (b >= 0x28 && b <= 0xC4) continue; // memory + numeric + sign ext
                if (b >= 0xD0 && b <= 0xD6) continue; // ref ops + br_on_null/non_null

                return inst.Op.GetMnemonic();
            }

            foreach (var inst in instructions)
            {
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var result = FindFirst(blockInst.GetBlock(i).Instructions);
                        if (result != null)
                            return result;
                    }
                }
            }

            return null;
        }
    }
}
