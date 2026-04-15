using System.IO;
using System.Linq;
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    public class EdgeCaseDiagnostic
    {
        private readonly ITestOutputHelper _output;

        public EdgeCaseDiagnostic(ITestOutputHelper output) => _output = output;

        [Fact]
        public void FindNonSimdNonTranspiledFunctions()
        {
            var provider = new WastTestDataProvider();
            int found = 0;

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

                int importCount = 0;
                bool foundLocal = false;
                foreach (var funcAddr in moduleInst.FuncAddrs)
                {
                    var func = runtime.GetFunction(funcAddr);
                    if (func is FunctionInstance fi && fi.Module == moduleInst)
                        foundLocal = true;
                    else if (!foundLocal)
                        importCount++;
                }

                foreach (var entry in result.Manifest.Functions)
                {
                    if (entry.IsTranspiled) continue;

                    // Check if this function contains SIMD opcodes
                    int funcAddrIdx = importCount + entry.Index;
                    var funcAddr = moduleInst.FuncAddrs.ElementAt(funcAddrIdx);
                    var func = runtime.GetFunction(funcAddr);
                    if (func is not FunctionInstance fi) continue;

                    bool hasSimd = ContainsSimd(fi.Body.Instructions);
                    if (!hasSimd)
                    {
                        found++;
                        string dir = Path.GetFileName(Path.GetDirectoryName(wasmPath)) ?? "";
                        string file = Path.GetFileName(wasmPath);
                        string reason = entry.RejectionReason ??
                            (fi.Type.ResultType.Arity > 1 ? "multi-value" : FindBlockingReason(fi));
                        _output.WriteLine($"NON-SIMD BLOCKED: {dir}/{file} func={entry.MethodName} " +
                            $"results={fi.Type.ResultType.Arity} reason={reason}");
                    }
                }
            }

            _output.WriteLine($"\nTotal non-SIMD non-transpiled: {found}");
        }

        private static bool ContainsSimd(System.Collections.Generic.IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                if (inst.Op.x00 == Wacs.Core.OpCodes.OpCode.FD) return true;
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                        if (ContainsSimd(blockInst.GetBlock(i).Instructions)) return true;
                }
            }
            return false;
        }

        private static string FindBlockingReason(FunctionInstance fi)
        {
            // First check simple known blockers
            var known = ScanForBlocker(fi.Body.Instructions);
            if (known != null) return known;

            // Deeper scan: check every opcode byte value
            var deepScan = ScanAllOpcodes(fi.Body.Instructions);
            if (deepScan != null) return $"opcode:0x{deepScan:X2}";

            return "unknown-structural";
        }

        private static byte? ScanAllOpcodes(System.Collections.Generic.IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                byte b = (byte)inst.Op.x00;
                // Check ranges NOT covered by any emitter
                // 0x00-0x15: control (most handled)
                // 0x08: throw, 0x0A: throw_ref, 0x1F: try_table
                // 0xD3: ref.eq, 0xD4: ref.as_non_null, 0xD5: br_on_null, 0xD6: br_on_non_null
                if (b == 0x08 || b == 0x0A || b == 0x1F) continue; // already scanned
                if (b >= 0xD3 && b <= 0xD6) continue; // already scanned

                // Check if any unreachable opcode
                if (b == 0x27) return b; // reserved
                if (b >= 0xC5 && b <= 0xCF) return b; // gap between sign ext and ref ops

                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var nested = ScanAllOpcodes(blockInst.GetBlock(i).Instructions);
                        if (nested != null) return nested;
                    }
                }
            }
            return null;
        }

        private static string? ScanForBlocker(System.Collections.Generic.IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                var op = inst.Op.x00;

                if (op == Wacs.Core.OpCodes.OpCode.Throw) return "throw";
                if (op == Wacs.Core.OpCodes.OpCode.ThrowRef) return "throw_ref";
                if (op == Wacs.Core.OpCodes.OpCode.TryTable) return "try_table";
                if (op == Wacs.Core.OpCodes.OpCode.BrOnNull) return "br_on_null";
                if (op == Wacs.Core.OpCodes.OpCode.BrOnNonNull) return "br_on_non_null";
                if (op == Wacs.Core.OpCodes.OpCode.RefAsNonNull) return "ref.as_non_null";
                if (op == Wacs.Core.OpCodes.OpCode.RefEq) return "ref.eq";

                // Global type issues
                if (op == Wacs.Core.OpCodes.OpCode.GlobalGet || op == Wacs.Core.OpCodes.OpCode.GlobalSet)
                {
                    // Check if global type is v128 (unsupported)
                    // Can't easily check from here — flag as potential
                }

                // GC array packed type issues — check if 0xFB instruction
                // might fail due to packed array element types
                if (op == Wacs.Core.OpCodes.OpCode.FB)
                {
                    // These pass CanEmit but might fail at emit time
                    // due to TypeBuilder issues with packed types
                }

                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var nested = ScanForBlocker(blockInst.GetBlock(i).Instructions);
                        if (nested != null) return nested;
                    }
                }
            }
            return null;
        }

        private static bool HasImportCalls(System.Collections.Generic.IEnumerable<InstructionBase> instructions, int importCount)
        {
            foreach (var inst in instructions)
            {
                if (inst is InstCall call && (int)call.X.Value < importCount) return true;
                if (inst.Op.x00 == Wacs.Core.OpCodes.OpCode.CallRef) return true;
                if (inst.Op.x00 == Wacs.Core.OpCodes.OpCode.ReturnCallRef) return true;
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                        if (HasImportCalls(blockInst.GetBlock(i).Instructions, importCount)) return true;
                }
            }
            return false;
        }
    }
}
