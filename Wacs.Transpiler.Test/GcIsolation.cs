using System;
using System.IO;
using System.Linq;
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.Test
{
    public class GcIsolation
    {
        private readonly ITestOutputHelper _output;
        private const string LogFile = "/tmp/gc_isolation.log";
        public GcIsolation(ITestOutputHelper output)
        {
            _output = output;
            File.WriteAllText(LogFile, "");
        }

        private void Log(string msg)
        {
            File.AppendAllText(LogFile, msg + "\n");
        }

        /// <summary>
        /// Transpile and invoke every function in every sub-module of gc/array.wast.
        /// Find which specific module + function produces crashing IL.
        /// </summary>
        [Fact]
        public void Array6GetAlone()
        {
            var runtime = new WasmRuntime();
            new SpecTestEnv().BindToRuntime(runtime);
            var path = "../../../../Spec.Test/generated-json/gc/array.wast/array.6.wasm";
            using var stream = new FileStream(path, FileMode.Open);
            var mod = BinaryModuleParser.ParseWasm(stream);
            var inst = runtime.InstantiateModule(mod);
            var result = new ModuleTranspiler().Transpile(inst, runtime);
            Log($"Transpiled: {result.TranspiledCount}, fallback: {result.FallbackCount}");

            if (result.FallbackCount > 0) { Log("Has fallbacks — skipping"); return; }

            var wrapper = new TranspiledModuleWrapper(result);
            wrapper.Instantiate();
            Log("Instantiated");

            // Call get(0, 0) with default args
            try
            {
                var r = wrapper.InvokeExport("get", new[] { new Value(0), new Value(0) });
                Log($"get(0,0) = {r[0].Data.Int32}");
            }
            catch (Exception ex)
            {
                Log($"get failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [Fact]
        public void FindCrashingModule()
        {
            var dir = "../../../../Spec.Test/generated-json/gc/array.wast";
            var wasmFiles = Directory.GetFiles(dir, "*.wasm").OrderBy(f => f).ToArray();
            Log($"Found {wasmFiles.Length} modules");

            foreach (var wasmPath in wasmFiles)
            {
                var filename = Path.GetFileName(wasmPath);
                Log($"\n=== {filename} ===");

                WasmModule module;
                try
                {
                    using var stream = new FileStream(wasmPath, FileMode.Open);
                    module = BinaryModuleParser.ParseWasm(stream);
                }
                catch (Exception ex)
                {
                    Log($"  Parse failed: {ex.GetType().Name}");
                    continue;
                }

                var runtime = new WasmRuntime();
                new SpecTestEnv().BindToRuntime(runtime);

                ModuleInstance inst;
                try
                {
                    inst = runtime.InstantiateModule(module);
                }
                catch (Exception ex)
                {
                    Log($"  Instantiate failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                TranspilationResult result;
                try
                {
                    result = new ModuleTranspiler().Transpile(inst, runtime);
                }
                catch (Exception ex)
                {
                    Log($"  Transpile failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                Log($"  {result.TranspiledCount} transpiled, {result.FallbackCount} fallback");

                if (result.FallbackCount > 0 || result.ModuleClass == null)
                {
                    Log($"  Skipping (fallback or no module class)");
                    continue;
                }

                TranspiledModuleWrapper wrapper;
                try
                {
                    wrapper = new TranspiledModuleWrapper(result);
                    wrapper.Instantiate();
                    Log($"  Instantiated");
                }
                catch (Exception ex)
                {
                    Log($"  Module instantiation failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // Try invoking each export
                foreach (var export in result.ExportMethods)
                {
                    var sanitized = InterfaceGenerator.SanitizeName(export.Name);
                    Log($"  Invoking '{export.Name}' (sanitized: '{sanitized}')...");
                    try
                    {
                        // Build default args for the function type
                        var paramTypes = export.WasmType.ParameterTypes.Types;
                        var args = new Value[paramTypes.Length];
                        for (int i = 0; i < args.Length; i++)
                        {
                            args[i] = paramTypes[i] switch
                            {
                                Wacs.Core.Types.Defs.ValType.I32 => new Value(0),
                                Wacs.Core.Types.Defs.ValType.I64 => new Value(0L),
                                Wacs.Core.Types.Defs.ValType.F32 => new Value(0f),
                                Wacs.Core.Types.Defs.ValType.F64 => new Value(0.0),
                                _ => new Value(paramTypes[i]) // ref types get null
                            };
                        }

                        var r = wrapper.InvokeExport(export.Name, args);
                        Log($"    OK: {r.Length} results");
                    }
                    catch (Exception ex)
                    {
                        Log($"    FAIL: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            Log("\nAll modules processed");
        }
    }
}
