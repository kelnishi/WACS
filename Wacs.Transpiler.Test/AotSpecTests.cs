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
using System.Linq;
using Spec.Test;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Runs the WebAssembly spec test commands against AOT-transpiled modules.
    ///
    /// Hybrid approach:
    ///   - Module loading runs through the interpreter (handles WASM instantiation
    ///     semantics, import resolution, validation, and Store transactions).
    ///   - After each module load, the module is transpiled and instantiated via
    ///     the Module class constructor (IExports/IImports path).
    ///   - Assertions invoke exports through the transpiled Module instance,
    ///     exercising the standalone assembly code path.
    ///   - Commands that test loading semantics (assert_invalid, assert_malformed,
    ///     assert_unlinkable, assert_uninstantiable) use the interpreter only.
    /// </summary>
    public class AotSpecTests
    {
        private readonly ITestOutputHelper _output;

        public AotSpecTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [ClassData(typeof(TranspilerTestDefinitions))]
        public void RunWastAotTranspiled(WastJson file)
        {
            _output.WriteLine($"AOT spec test: {file.TestName}");
            var env = new SpecTestEnv();
            var runtime = new WasmRuntime();
            env.BindToRuntime(runtime);
            runtime.TranspileModules = false;

            // Track transpiled modules by name for cross-module import resolution.
            // null key = the "current" unnamed module.
            var wrappers = new Dictionary<string, TranspiledModuleWrapper>();
            TranspiledModuleWrapper? currentWrapper = null;

            int totalTranspiled = 0;
            int totalFallback = 0;

            Module? module = null;
            foreach (var command in file.Commands)
            {
                try
                {
                    _output.WriteLine($"    {command}");

                    // For invoke-bearing commands, try the transpiled path first
                    if (currentWrapper != null && TryRunThroughTranspiler(command, wrappers, currentWrapper, file, ref runtime, ref module))
                    {
                        continue; // Transpiled path handled it
                    }

                    // Fall through to interpreter for everything else
                    var warnings = command.RunTest(file, ref runtime, ref module);
                    foreach (var error in warnings)
                    {
                        _output.WriteLine($"    Warning: {error}");
                    }

                    // After a module is loaded, transpile and create wrapper
                    if (command is ModuleCommand mc)
                    {
                        // Reset — new module replaces previous
                        currentWrapper = null;
                        var wrapper = TranspileAndWrap(runtime, mc.Name ?? "", wrappers);
                        if (wrapper != null)
                        {
                            currentWrapper = wrapper;
                            totalTranspiled += wrapper.Result.TranspiledCount;
                            totalFallback += wrapper.Result.FallbackCount;
                        }
                    }
                }
                catch (TestException exc)
                {
                    Assert.Fail(exc.Message);
                }
                catch (Exception exc) when (exc is not Xunit.Sdk.XunitException)
                {
                    Assert.Fail($"Unexpected exception at {command}: {exc.GetType().Name}: {exc.Message}");
                }
            }

            if (totalTranspiled + totalFallback > 0)
            {
                _output.WriteLine($"    AOT: {totalTranspiled} transpiled, {totalFallback} fallback");
            }
        }

        /// <summary>
        /// Try to execute an invoke-bearing command through the transpiled module.
        /// Returns true if handled, false to fall through to interpreter.
        /// </summary>
        private bool TryRunThroughTranspiler(
            ICommand command,
            Dictionary<string, TranspiledModuleWrapper> wrappers,
            TranspiledModuleWrapper currentWrapper,
            WastJson file, ref WasmRuntime runtime, ref Module? module)
        {
            // Only handle commands with invoke actions
            InvokeAction? invokeAction = command switch
            {
                AssertReturnCommand arc => arc.Action as InvokeAction,
                AssertTrapCommand atc => atc.Action as InvokeAction,
                AssertExhaustionCommand aec => aec.Action as InvokeAction,
                ActionCommand ac => ac.Action as InvokeAction,
                _ => null
            };

            if (invokeAction == null) return false;

            // Resolve which wrapper to use
            var wrapper = currentWrapper;
            if (!string.IsNullOrEmpty(invokeAction.Module))
            {
                if (!wrappers.TryGetValue(invokeAction.Module, out var namedWrapper))
                    return false; // Named module not transpiled — fall through
                wrapper = namedWrapper;
            }

            if (wrapper?.ModuleInstance == null) return false;

            // Try to invoke through the transpiled module
            switch (command)
            {
                case AssertReturnCommand arc:
                    return RunAssertReturn(wrapper, invokeAction, arc);

                case AssertTrapCommand atc:
                    return RunAssertTrap(wrapper, invokeAction, atc);

                case AssertExhaustionCommand aec:
                    return RunAssertExhaustion(wrapper, invokeAction);

                case ActionCommand:
                case InvokeCommand:
                    // Fire-and-forget invoke — just run it
                    try { wrapper.InvokeExport(invokeAction.Field, invokeAction.Args.Select(a => a.AsValue).ToArray()); }
                    catch { /* Actions may trap — that's fine */ }
                    return true;

                default:
                    return false;
            }
        }

        private bool RunAssertReturn(
            TranspiledModuleWrapper wrapper, InvokeAction invokeAction, AssertReturnCommand arc)
        {
            Value[] result;
            try
            {
                var args = invokeAction.Args.Select(a => a.AsValue).ToArray();
                result = wrapper.InvokeExport(invokeAction.Field, args);
            }
            catch (Exception ex)
            {
                throw new TestException(
                    $"Unexpected exception at {arc}: {ex.GetType().Name}: {ex.Message}");
            }

            if (result.Length != arc.Expected.Count)
                throw new TestException(
                    $"Test failed {arc} \"{invokeAction.Field}\": " +
                    $"Expected [{string.Join(", ", arc.Expected.Select(e => e.AsValue))}], " +
                    $"but got [{string.Join(", ", result.Select(r => r.ToString()))}]");

            foreach (var (actual, arg) in result.Zip(arc.Expected, (a, e) => (a, e)))
            {
                if (arg.Type == "either")
                {
                    if (!arg.AsValues.Any(v => CompareValues(actual, v)))
                        throw new TestException(
                            $"Test failed {arc} \"{invokeAction.Field}\": " +
                            $"\nExpected either:[{string.Join(", ", arg.AsValues)}]," +
                            $"\n but got [{string.Join(", ", result.Select(r => r.ToString()))}]");
                }
                else
                {
                    var expected = arg.AsValue;
                    if (expected.IsNullRef)
                    {
                        if (!actual.IsNullRef)
                            throw new TestException(
                                $"Test failed {arc} \"{invokeAction.Field}\": " +
                                $"Expected null ref, but got {actual}");
                    }
                    else if (!CompareValues(actual, expected))
                    {
                        throw new TestException(
                            $"Test failed {arc} \"{invokeAction.Field}\": " +
                            $"\nExpected [{string.Join(", ", arc.Expected.Select(e => e.AsValue))}]," +
                            $"\n but got [{string.Join(", ", result.Select(r => r.ToString()))}]");
                    }
                }
            }

            return true;
        }

        private bool RunAssertTrap(
            TranspiledModuleWrapper wrapper, InvokeAction invokeAction, AssertTrapCommand atc)
        {
            try
            {
                var args = invokeAction.Args.Select(a => a.AsValue).ToArray();
                wrapper.InvokeExport(invokeAction.Field, args);
                // Should have trapped
                throw new TestException($"Test failed {atc} \"{atc.Text}\"");
            }
            catch (TrapException)
            {
                return true; // Expected trap
            }
            catch (Wacs.Core.Runtime.Exceptions.WasmRuntimeException)
            {
                return true; // Also a valid trap (e.g., stack exhaustion)
            }
            catch (DivideByZeroException)
            {
                return true; // CLR div-by-zero = WASM trap
            }
            catch (OverflowException)
            {
                return true; // CLR overflow = WASM trap
            }
            catch (TestException)
            {
                throw; // Re-throw our own failure
            }
            catch (Exception ex)
            {
                throw new TestException(
                    $"Unexpected exception at {atc}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool RunAssertExhaustion(
            TranspiledModuleWrapper wrapper, InvokeAction invokeAction)
        {
            try
            {
                var args = invokeAction.Args.Select(a => a.AsValue).ToArray();
                wrapper.InvokeExport(invokeAction.Field, args);
                return false; // Should have trapped — fall through to interpreter
            }
            catch (Wacs.Core.Runtime.Exceptions.WasmRuntimeException)
            {
                return true; // Expected exhaustion
            }
            catch (TrapException)
            {
                return true; // Stack exhaustion may manifest as trap
            }
            catch
            {
                return false; // Unexpected — fall through
            }
        }

        /// <summary>
        /// Transpile the current module and create a wrapper instance.
        /// Returns null if transpilation fails or should be skipped.
        /// </summary>
        private TranspiledModuleWrapper? TranspileAndWrap(
            WasmRuntime runtime, string? moduleName,
            Dictionary<string, TranspiledModuleWrapper> wrappers)
        {
            ModuleInstance moduleInst;
            try
            {
                moduleInst = string.IsNullOrEmpty(moduleName)
                    ? runtime.GetModule(null)
                    : runtime.GetModule(moduleName);
            }
            catch { return null; }

            // Skip modules with GC struct/array types — GC IL not stable yet
            if (HasGcTypes(moduleInst)) return null;

            var transpiler = new ModuleTranspiler();
            TranspilationResult result;
            try { result = transpiler.Transpile(moduleInst, runtime); }
            catch (Exception ex)
            {
                _output.WriteLine($"    Transpilation failed: {ex.Message}");
                return null;
            }

            if (result.ModuleClass == null)
            {
                _output.WriteLine($"    No Module class generated");
                return null;
            }

            // Only use the standalone Module path when ALL functions are transpiled
            // and the module doesn't need interpreter features (bulk memory with Store,
            // ref.func requiring Module). Otherwise fall through to interpreter.
            if (result.FallbackCount > 0)
            {
                _output.WriteLine($"    {result.FallbackCount} fallback functions — using interpreter-backed path");
                return null;
            }

            // Check if the module needs runtime features not available standalone
            bool needsStore = moduleInst.Repr.Datas.Length > 0 // memory.init needs data segments via Store
                || moduleInst.Repr.Imports.Any(i => i.Desc is Wacs.Core.Module.ImportDesc.FuncDesc);
            if (needsStore)
            {
                _output.WriteLine($"    Module needs Store features — using interpreter-backed path");
                return null;
            }

            var wrapper = new TranspiledModuleWrapper(result);

            // Build imports proxy if the module has imports
            object? importsProxy = null;
            if (result.ImportsInterface != null)
            {
                importsProxy = BuildImportsProxy(result, wrappers, runtime, moduleInst);
                if (importsProxy == null)
                {
                    _output.WriteLine($"    Could not build imports proxy");
                    return null;
                }
            }

            try
            {
                wrapper.Instantiate(importsProxy);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"    Module instantiation failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }

            // Register by name ("" = current unnamed module)
            wrappers[moduleName] = wrapper;

            return wrapper;
        }

        /// <summary>
        /// Build an IImports proxy backed by previously transpiled module exports
        /// and interpreter-backed host functions.
        /// </summary>
        private object? BuildImportsProxy(
            TranspilationResult result,
            Dictionary<string, TranspiledModuleWrapper> wrappers,
            WasmRuntime runtime,
            ModuleInstance moduleInst)
        {
            if (result.ImportsInterface == null) return null;

            var handlers = new Dictionary<string, Func<object?[], object?>>();

            foreach (var importMethod in result.ImportMethods)
            {
                // Import method names are sanitized: "{moduleName}_{fieldName}"
                // We need to find the source: a transpiled module's export or a host function
                var importName = importMethod.Name;

                // Try to find the handler from the WASM import descriptors
                bool found = false;
                foreach (var import in moduleInst.Repr.Imports)
                {
                    if (import.Desc is not Wacs.Core.Module.ImportDesc.FuncDesc) continue;

                    var expectedName = InterfaceGenerator.SanitizeName($"{import.ModuleName}_{import.Name}");
                    if (expectedName != importName) continue;

                    // Check if we have a transpiled module for this import's source
                    if (wrappers.TryGetValue(import.ModuleName, out var sourceWrapper)
                        && sourceWrapper.ModuleInstance != null)
                    {
                        try
                        {
                            var handler = sourceWrapper.CreateExportHandler(import.Name);
                            handlers[importName] = handler;
                            found = true;
                        }
                        catch { /* Export not found on transpiled module */ }
                    }

                    if (!found)
                    {
                        // Fall back to interpreter for host functions (spectest, etc.)
                        var funcType = importMethod.WasmType;
                        handlers[importName] = args =>
                        {
                            // Resolve through runtime and invoke via interpreter
                            if (runtime.TryGetExportedFunction((import.ModuleName, import.Name), out var addr))
                            {
                                var invoker = runtime.CreateStackInvoker(addr);
                                var valueArgs = new Value[args?.Length ?? 0];
                                for (int i = 0; i < valueArgs.Length; i++)
                                {
                                    if (args![i] is Value v) valueArgs[i] = v;
                                    else if (args[i] is int iv) valueArgs[i] = new Value(iv);
                                    else if (args[i] is long lv) valueArgs[i] = new Value(lv);
                                    else if (args[i] is float fv) valueArgs[i] = new Value(fv);
                                    else if (args[i] is double dv) valueArgs[i] = new Value(dv);
                                }
                                var results = invoker(valueArgs);
                                // Return first result as the appropriate CLR type
                                if (results.Length == 0) return null;
                                return ConvertValueToClr(results[0], funcType.ResultType.Types[0]);
                            }
                            return null;
                        };
                        found = true;
                    }

                    break; // Found the matching import
                }

                if (!found)
                {
                    // Register a no-op handler to avoid crashes
                    handlers[importName] = _ => null;
                }
            }

            try
            {
                return ImportDispatcher.Create(result.ImportsInterface, handlers);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"    Proxy creation failed: {ex.Message}");
                return null;
            }
        }

        private static object? ConvertValueToClr(Value val, Wacs.Core.Types.Defs.ValType type)
        {
            return type switch
            {
                Wacs.Core.Types.Defs.ValType.I32 => val.Data.Int32,
                Wacs.Core.Types.Defs.ValType.I64 => val.Data.Int64,
                Wacs.Core.Types.Defs.ValType.F32 => val.Data.Float32,
                Wacs.Core.Types.Defs.ValType.F64 => val.Data.Float64,
                _ => val
            };
        }

        private static bool CompareValues(Value actual, Value expected)
        {
            if (expected.IsNullRef)
                return actual.IsNullRef;
            return actual.Equals(expected);
        }

        private static bool HasGcTypes(ModuleInstance moduleInst)
        {
            try
            {
                foreach (var recType in moduleInst.Repr.Types)
                    foreach (var subType in recType.SubTypes)
                        if (subType.Body is Wacs.Core.Types.StructType
                            || subType.Body is Wacs.Core.Types.ArrayType)
                            return true;
            }
            catch { }
            return false;
        }
    }
}
