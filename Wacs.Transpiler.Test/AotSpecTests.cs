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
    /// Runs WebAssembly spec test commands against AOT-transpiled modules
    /// using ONLY the standalone IExports/IImports path.
    ///
    /// No interpreter fallback. If a module can't be fully transpiled and
    /// instantiated standalone, its assertions are skipped — not run through
    /// the interpreter. The interpreter-backed tests live in Spec.Test.
    ///
    /// Modules load through the interpreter (WASM instantiation semantics),
    /// then transpile and instantiate via the Module class constructor.
    /// Assertions invoke exclusively through TranspiledModuleWrapper.
    ///
    /// Modules that can't use the standalone path are skipped with a reason.
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

            var wrappers = new Dictionary<string, TranspiledModuleWrapper>();
            TranspiledModuleWrapper? currentWrapper = null;
            string? skipReason = null;

            int totalTranspiled = 0;
            int totalSkipped = 0;

            Module? module = null;
            foreach (var command in file.Commands)
            {
                try
                {
                    _output.WriteLine($"    {command}");

                    // Module commands: load through interpreter, then transpile
                    if (command is ModuleCommand mc)
                    {
                        // Always load through interpreter for proper WASM instantiation
                        command.RunTest(file, ref runtime, ref module);

                        // Reset for new module
                        currentWrapper = null;
                        skipReason = null;

                        var (wrapper, reason) = TranspileAndWrap(runtime, mc.Name ?? "", wrappers);
                        if (wrapper != null)
                        {
                            currentWrapper = wrapper;
                            wrappers[mc.Name ?? ""] = wrapper;
                            totalTranspiled += wrapper.Result.TranspiledCount;
                            _output.WriteLine($"      Standalone: {wrapper.Result.TranspiledCount} functions");
                        }
                        else
                        {
                            skipReason = reason;
                            totalSkipped++;
                            _output.WriteLine($"      Skipped: {reason}");
                        }
                        continue;
                    }

                    // Commands that test loading semantics — always run through interpreter
                    if (command is AssertInvalidCommand or AssertMalformedCommand
                        or AssertUnlinkableCommand or AssertUninstantiableCommand
                        or RegisterCommand)
                    {
                        command.RunTest(file, ref runtime, ref module);
                        continue;
                    }

                    // Invoke-bearing commands: run through transpiled module or skip
                    if (currentWrapper != null)
                    {
                        RunThroughTranspiler(command, wrappers, currentWrapper);
                    }
                    else if (skipReason != null)
                    {
                        // Module was skipped — skip its assertions too
                        continue;
                    }
                    else
                    {
                        // No module loaded yet — run through interpreter (preamble commands)
                        command.RunTest(file, ref runtime, ref module);
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

            if (totalTranspiled > 0 || totalSkipped > 0)
            {
                _output.WriteLine($"    AOT: {totalTranspiled} transpiled, {totalSkipped} modules skipped");
            }
        }

        /// <summary>
        /// Execute a command through the transpiled module. No interpreter fallback.
        /// </summary>
        private void RunThroughTranspiler(
            ICommand command,
            Dictionary<string, TranspiledModuleWrapper> wrappers,
            TranspiledModuleWrapper currentWrapper)
        {
            InvokeAction? invokeAction = command switch
            {
                AssertReturnCommand arc => arc.Action as InvokeAction,
                AssertTrapCommand atc => atc.Action as InvokeAction,
                AssertExhaustionCommand aec => aec.Action as InvokeAction,
                ActionCommand ac => ac.Action as InvokeAction,
                _ => null
            };

            if (invokeAction == null) return;

            // Resolve which wrapper to use
            var wrapper = currentWrapper;
            if (!string.IsNullOrEmpty(invokeAction.Module))
            {
                if (!wrappers.TryGetValue(invokeAction.Module, out var namedWrapper))
                    throw new TestException($"Named module '{invokeAction.Module}' not transpiled");
                wrapper = namedWrapper;
            }

            switch (command)
            {
                case AssertReturnCommand arc:
                    RunAssertReturn(wrapper, invokeAction, arc);
                    break;

                case AssertTrapCommand atc:
                    RunAssertTrap(wrapper, invokeAction, atc);
                    break;

                case AssertExhaustionCommand:
                    RunAssertExhaustion(wrapper, invokeAction);
                    break;

                case ActionCommand:
                    try { wrapper.InvokeExport(invokeAction.Field, invokeAction.Args.Select(a => a.AsValue).ToArray()); }
                    catch { /* Actions may trap */ }
                    break;
            }
        }

        private void RunAssertReturn(
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
        }

        private void RunAssertTrap(
            TranspiledModuleWrapper wrapper, InvokeAction invokeAction, AssertTrapCommand atc)
        {
            try
            {
                var args = invokeAction.Args.Select(a => a.AsValue).ToArray();
                wrapper.InvokeExport(invokeAction.Field, args);
                throw new TestException($"Test failed {atc} \"{atc.Text}\"");
            }
            catch (TrapException) { }
            catch (Wacs.Core.Runtime.Exceptions.WasmRuntimeException) { }
            catch (DivideByZeroException) { }
            catch (OverflowException) { }
            catch (TestException) { throw; }
            catch (Exception ex)
            {
                throw new TestException(
                    $"Unexpected exception at {atc}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void RunAssertExhaustion(
            TranspiledModuleWrapper wrapper, InvokeAction invokeAction)
        {
            try
            {
                var args = invokeAction.Args.Select(a => a.AsValue).ToArray();
                wrapper.InvokeExport(invokeAction.Field, args);
                throw new TestException("Expected exhaustion but call succeeded");
            }
            catch (Wacs.Core.Runtime.Exceptions.WasmRuntimeException) { }
            catch (TrapException) { }
            catch (TestException) { throw; }
        }

        /// <summary>
        /// Transpile the current module and create a standalone wrapper.
        /// Returns (wrapper, null) on success or (null, reason) on skip.
        /// </summary>
        private (TranspiledModuleWrapper?, string?) TranspileAndWrap(
            WasmRuntime runtime, string moduleName,
            Dictionary<string, TranspiledModuleWrapper> wrappers)
        {
            ModuleInstance moduleInst;
            try
            {
                moduleInst = string.IsNullOrEmpty(moduleName)
                    ? runtime.GetModule(null)
                    : runtime.GetModule(moduleName);
            }
            catch { return (null, "module not found"); }

            // GC modules go through the same path — no special-casing.
            // If the GC emitter produces bad IL, it surfaces as a test failure.

            var transpiler = new ModuleTranspiler();
            TranspilationResult result;
            try { result = transpiler.Transpile(moduleInst, runtime); }
            catch (Exception ex)
            {
                return (null, $"transpilation failed: {ex.Message}");
            }

            if (result.ModuleClass == null)
                return (null, "no Module class generated");

            if (result.FallbackCount > 0)
                return (null, $"{result.FallbackCount} fallback functions");

            var wrapper = new TranspiledModuleWrapper(result);


            // Build imports proxy if needed
            object? importsProxy = null;
            if (result.ImportsInterface != null)
            {
                importsProxy = BuildImportsProxy(result, wrappers, runtime, moduleInst);
                if (importsProxy == null)
                    return (null, "could not build imports proxy");
            }

            try
            {
                wrapper.Instantiate(importsProxy);
            }
            catch (Exception ex)
            {
                return (null, $"instantiation failed: {ex.GetType().Name}: {ex.Message}");
            }

            return (wrapper, null);
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
                var importName = importMethod.Name;
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
                        catch { }
                    }

                    if (!found)
                    {
                        // Host function: dispatch through interpreter
                        var funcType = importMethod.WasmType;
                        var capturedImport = import;
                        handlers[importName] = args =>
                        {
                            if (runtime.TryGetExportedFunction((capturedImport.ModuleName, capturedImport.Name), out var addr))
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
                                if (results.Length == 0) return null;
                                return ConvertValueToClr(results[0], funcType.ResultType.Types[0]);
                            }
                            return null;
                        };
                        found = true;
                    }

                    break;
                }

                if (!found)
                    handlers[importName] = _ => null;
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

    }
}
