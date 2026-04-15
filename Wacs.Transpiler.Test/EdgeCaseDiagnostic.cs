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
                        _output.WriteLine($"NON-SIMD BLOCKED: {dir}/{file} func={entry.MethodName} " +
                            $"export={entry.ExportName ?? "(none)"} " +
                            $"params={fi.Type.ParameterTypes.Arity} results={fi.Type.ResultType.Arity} " +
                            $"hasImportCalls={HasImportCalls(fi.Body.Instructions, importCount)}");
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
