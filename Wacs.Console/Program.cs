// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.WASIp1;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.Cli;
using Wacs.WASIp1.Types;

namespace Wacs.Console
{
    public class Program
    {
        static int Main(string[] args)
        {
            List<string> subargs = new List<string>();
            var moduleArg = args.FirstOrDefault(arg => File.Exists(arg));
            if (moduleArg != null)
            {
                int moduleIndex = Array.IndexOf(args, moduleArg) + 1;
                subargs.AddRange(args[moduleIndex..]);
                args = args[0..moduleIndex];
            }

            var parser = new Parser(with =>
            {
                with.EnableDashDash = true;
                with.AutoHelp = true;        // Suppress automatic help
                with.AutoVersion = true;      // Suppress automatic version
                with.HelpWriter = System.Console.Out;
            });

            // Map subargs to ExecutableArgs in the options
            var parsedResult = parser.ParseArguments<CommandLineOptions>(args);

            return parsedResult.MapResult(
                opts =>
                {
                    opts.ExecutableArgs = subargs; // Assign subargs to ExecutableArgs
                    return RunWithOptions(opts);
                },
                _ => 1
            );
        }

        static int RunWithOptions(CommandLineOptions opts)
        {
            // Validate executable path
            if (!File.Exists(opts.WasmModule))
            {
                System.Console.Error.WriteLine($"Error: Wasm file not found: {opts.WasmModule}");
                return 1;
            }

            // Check the file extension
            string fileExtension = Path.GetExtension(opts.WasmModule).ToLowerInvariant();
            if (fileExtension != ".wasm" && fileExtension != ".wat")
            {
                System.Console.Error.WriteLine(
                    $"Error: Invalid file extension: {fileExtension}. Expected .wasm or .wat (for .wast scripts use a spec runner).");
                return 1;
            }

            var args = new List<string> { opts.WasmModule };
            var moreArgs = opts.ExecutableArgs.ToList();
            for (int a = 0; a < moreArgs.Count; ++a)
            {
                if (moreArgs[a].StartsWith("--invoke"))
                {
                    if (a + 1 < moreArgs.Count)
                    {
                        a += 1;
                        opts.InvokeFunction = moreArgs[a];
                    }
                    continue;
                }
                args.Add(moreArgs[a]);
            }

            // Validate directories
            foreach (var dir in opts.Directories)
            {
                if (!Directory.Exists(dir))
                {
                    System.Console.Error.WriteLine($"Error: Directory not found: {dir}");
                    return 1;
                }
            }

            // Process environment variables
            var envVars = new Dictionary<string, string>();
            foreach (var env in opts.EnvironmentVars)
            {
                var parts = env.Split('=', 2);
                if (parts.Length != 2)
                {
                    System.Console.Error.WriteLine($"Error: Invalid environment variable format: {env}");
                    return 1;
                }
                envVars[parts[0]] = parts[1];
            }

            var runtime = new WasmRuntime();

            var parseTimer = new Stopwatch();
            if (opts.LogProg)
            {
                parseTimer.Start();
            }
            
            //Parse the module — dispatch on extension.
            Wacs.Core.Module module;
            using (var fileStream = new FileStream(opts.WasmModule, FileMode.Open))
            {
                module = fileExtension == ".wat"
                    ? Wacs.Core.Text.TextModuleParser.ParseWat(fileStream)
                    : BinaryModuleParser.ParseWasm(fileStream);
            }

            if (opts.LogProg)
            {
                parseTimer.Stop();
                System.Console.Error.WriteLine($"Parsing module took {parseTimer.ElapsedMilliseconds:#0.###}ms");
            }
            
            if (opts.Render)
            {
                string outputFilePath = Path.ChangeExtension(opts.WasmModule, ".wat");
                // Parser-friendly WAT output via TextModuleWriter — round-
                // trips cleanly through TextModuleParser. Use
                // ModuleRenderer.RenderWatToStream if you want the
                // debug/display variant (stack annotations, (;id;) comments).
                var wat = Wacs.Core.Text.TextModuleWriter.Write(module);
                File.WriteAllText(outputFilePath, wat);
                if (opts.LogProg)
                    System.Console.Error.WriteLine($"Rendered {outputFilePath} ({wat.Length} chars)");
            }

            if (!opts.SkipValidation)
            {
                //If you just want to do validation without a runtime, you could do it like this
                var validationResult = module.Validate();
                var funcsToRender = new HashSet<(FuncIdx, string)>();
                foreach (var error in validationResult.Errors)
                {
                    if (funcsToRender.Count > 100) break;
                    switch (error.Severity)
                    {
                        case Severity.Warning:
                        case Severity.Error:

                            if (error.ErrorMessage.StartsWith("Function["))
                            {
                                var parts = error.ErrorMessage.Split(":");
                                var path = parts[0];
                                var msg = string.Join(":", parts[1..]);

                                var (line, code) = module.CalculateLine(path);
                                if (!string.IsNullOrWhiteSpace(code)) code = $" ({code})";
                                var (fline, _) = module.CalculateLine(path, functionRelative: true);

                                System.Console.Error.WriteLine($"Validation {error.Severity}.{msg}");
                                System.Console.Error.WriteLine($"    {path}");
                                System.Console.Error.WriteLine(
                                    $"    at{code} in {opts.WasmModule}:line {line} ({fline})");
                                System.Console.Error.WriteLine();

                                FuncIdx fIdx = ModuleRenderer.GetFuncIdx(path);
                                string funcId = ModuleRenderer.ChopFunctionId(path);
                                funcsToRender.Add((fIdx, funcId));
                            }
                            else
                            {
                                System.Console.Error.WriteLine($"Validation {error.Severity}: {error.ErrorMessage}");
                            }

                            break;
                    }
                }

                foreach (var (fIdx, funcId) in funcsToRender)
                {
                    string funcString = ModuleRenderer.RenderFunctionWat(module, fIdx, "", true);
                    using var outputFileStream = new FileStream($"{funcId}.part.wat", FileMode.Create);
                    using var outputStreamWriter = new StreamWriter(outputFileStream);
                    outputStreamWriter.Write(funcString);
                    outputStreamWriter.Close();
                }
            }


            //Bind module dependencies prior to instantiation
            runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
            {
                System.Console.Write(c);
            });

            // runtime.BindHostFunction<Action<int>>(("env", "emscripten_notify_memory_growth"), p =>
            // {
            //     System.Console.WriteLine($"Emscripten notify memory growth {p}");
            // });

            var wasiConfig = Wasi.DefaultConfiguration();
            wasiConfig.Arguments = args;
            wasiConfig.EnvironmentVariables = envVars;
            wasiConfig.PreopenedDirectories = opts.Directories
                    .Select(path => new PreopenedDirectory(wasiConfig, path))
                    .ToList();
            using var wasi = new WASIp1.Wasi(wasiConfig);
            wasi.BindToRuntime(runtime);

            string moduleName = opts.ModuleName;

            if (opts.LogProg)
                System.Console.Error.WriteLine($"Instantiating Module {moduleName}");

            // --super routes to whichever fuser belongs to the active
            // runtime: the polymorphic block-level rewriter rewrites the
            // instruction tree into Wacs.Core.Instructions.SuperInstruction
            // types that the switch-runtime's BytecodeCompiler can't
            // consume, so the two are mutually exclusive. --switch takes
            // precedence: when present, --super enables the switch
            // runtime's bytecode-stream fuser; otherwise it enables the
            // polymorphic tree rewriter.
            if (opts.UseSwitch)
            {
                runtime.UseSwitchRuntime = true;
                runtime.ExecContext.Attributes.UseSwitchSuperInstructions = opts.SuperInstructions;
            }
            else if (opts.SuperInstructions)
            {
                runtime.SuperInstruction = true;
            }

            //Validation normally happens after instantiation, but you can skip it if you did it after parsing, or you're like super confident.
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true, TimeInstantiation = opts.LogProg});
            runtime.RegisterModule(moduleName, modInst);

            // -t / --transpiler and --aot both route through the AOT path.
            // Kept as aliases so --transpiler does what its name implies
            // (actually invoke the transpiler); the interpreter
            // super-instruction toggle moved to its own --super flag.
            if (opts.Transpile || opts.Aot)
            {
                return RunViaTranspiler(opts, runtime, modInst);
            }

            var callOptions = new InvokerOptions
            {
                LogGas = opts.LogGas,
                GasLimit = opts.LimitGas,
                LogProgressEvery = opts.LogProgressEvery,
                LogInstructionExecution = opts.LogInstructionExecution,
                CalculateLineNumbers = opts.CalculateLineNumbers,
                CollectStats = opts.CollectStats,
                SynchronousExecution = true,
            };

            //Wasm/WASI entry points
            if (modInst.StartFunc != null)
            {
                var caller = runtime.CreateInvokerAction(modInst.StartFunc, callOptions);

                var name = runtime.GetFunctionName(modInst.StartFunc);
                if (opts.LogProg)
                    System.Console.Error.WriteLine($"Executing wasm function {name}");

                using (IDisposable _ = opts.Profile ? new ProfilingSession() : new NoOpProfilingSession())
                {
                    try
                    {
                        caller();
                    }
                    catch (TrapException exc)
                    {
                        System.Console.Error.WriteLine(exc);
                        return 1;
                    }
                    catch (SignalException exc)
                    {
                        if (opts.LogProg)
                            System.Console.Error.WriteLine($"{exc.HumanReadable}");
                        return exc.Signal;
                    }
                }
            }
            else if (runtime.TryGetExportedFunction((moduleName, "_start"), out var startAddr))
            {
                if (opts.LogProg)
                    System.Console.Error.WriteLine("Calling start");

                var caller = runtime.CreateInvokerAction(startAddr, callOptions);

                using (IDisposable _ = opts.Profile ? new ProfilingSession() : new NoOpProfilingSession())
                {
                    try
                    {
                        caller();
                    }
                    catch (TrapException exc)
                    {
                        System.Console.Error.WriteLine(exc);
                        return 1;
                    }
                    catch (SignalException exc)
                    {
                        ErrNo sig = (ErrNo)exc.Signal;
                        if (opts.LogProg)
                            System.Console.Error.WriteLine($"{sig.HumanReadable()}");
                        return exc.Signal;
                    }
                }
            }
            else if (runtime.TryGetExportedFunction((moduleName, opts.InvokeFunction), out var invokeAddr))
            {
                if (opts.LogProg)
                    System.Console.Error.WriteLine($"Calling {opts.InvokeFunction}");

                var caller = runtime.CreateStackInvoker(invokeAddr, callOptions);

                using (IDisposable _ = opts.Profile ? new ProfilingSession() : new NoOpProfilingSession())
                {
                    try
                    {
                        var type = runtime.GetFunctionType(invokeAddr);
                        var provided = args.Skip(1).ToList();
                        if (type.ParameterTypes.Arity != provided.Count)
                        {
                            var pStrs = string.Join(" ", provided);
                            System.Console.Error.WriteLine($"Number of parameters [{pStrs}] != function[{opts.InvokeFunction}] {type.ParameterTypes.Arity}");
                            return 1;
                        }

                        var pVals = new Value[provided.Count];
                        for (int i = 0; i < provided.Count; i++)
                        {
                            pVals[i] = new Value(type.ParameterTypes.Types[i], provided[i]);
                        }

                        Value[] result = caller(pVals);

                        System.Console.WriteLine($"Result:[{string.Join(" ", result)}]");
                    }
                    catch (TrapException exc)
                    {
                        System.Console.Error.WriteLine(exc);
                        return 1;
                    }
                    catch (SignalException exc)
                    {
                        ErrNo sig = (ErrNo)exc.Signal;
                        if (opts.LogProg)
                            System.Console.Error.WriteLine($"{sig.HumanReadable()}");
                        return exc.Signal;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// AOT path for <c>--aot</c>: transpile the already-instantiated module
        /// into a .NET assembly and execute through the transpiled code.
        /// Imports wired via <see cref="HostedRunner"/> — the runtime keeps
        /// its interpreter-bound host functions (WASI, user hosts) live;
        /// the transpiled module calls into them through a DispatchProxy.
        /// </summary>
        private static int RunViaTranspiler(CommandLineOptions opts, WasmRuntime runtime, ModuleInstance modInst)
        {
            // Note: --aot needs Reflection.Emit at runtime. If you're running
            // a PublishAot-compiled binary of Wacs.Console, the transpiler
            // will throw deep in Reflection.Emit. We surface that failure
            // below via the catch-all; the RuntimeFeature.IsDynamicCodeSupported
            // check is unreliable here because PublishAot=true in the csproj
            // sets it false at compile-time even for `dotnet run` builds.

            var tOpts = new TranspilerOptions
            {
                Simd = ParseSimdStrategy(opts.AotSimd),
                EmitTailCallPrefix = !opts.AotNoTailCalls,
                MaxFunctionSize = opts.AotMaxFnSize,
                DataStorage = ParseDataStorage(opts.AotDataStorage),
            };

            if (opts.LogProg)
                System.Console.Error.WriteLine(
                    $"AOT: transpiling (simd={tOpts.Simd}, tail_calls={tOpts.EmitTailCallPrefix}, "
                    + $"max_fn_size={tOpts.MaxFunctionSize}, data_storage={tOpts.DataStorage})");

            var transpiler = new ModuleTranspiler("WacsConsoleAot", tOpts);
            TranspilationResult result;
            try
            {
                result = transpiler.Transpile(modInst, runtime, "WasmModule");
            }
            catch (Exception exc)
            {
                System.Console.Error.WriteLine($"error: --aot: transpile failed: {exc.Message}");
                if (opts.LogProg) System.Console.Error.WriteLine(exc);
                return 1;
            }

            if (!string.IsNullOrEmpty(opts.AotSave))
            {
                try
                {
                    result.SaveAssembly(opts.AotSave);
                    if (opts.LogProg)
                        System.Console.Error.WriteLine($"AOT: saved to {opts.AotSave}");
                }
                catch (Exception exc)
                {
                    System.Console.Error.WriteLine($"error: --aot: SaveAssembly failed: {exc.Message}");
                    return 1;
                }
            }

            string entryPoint = !string.IsNullOrEmpty(opts.InvokeFunction)
                ? opts.InvokeFunction
                : (modInst.StartFunc != null
                    ? runtime.GetFunctionName(modInst.StartFunc)
                    : "_start");

            return HostedRunner.Run(result, runtime, modInst, entryPoint, opts.LogProg);
        }

        private static SimdStrategy ParseSimdStrategy(string value) => value switch
        {
            "interpreter" => SimdStrategy.InterpreterDispatch,
            "intrinsics" => SimdStrategy.HardwareIntrinsics,
            _ => SimdStrategy.ScalarReference,
        };

        private static DataSegmentStorage ParseDataStorage(string value) => value switch
        {
            "raw" => DataSegmentStorage.RawResource,
            "static" => DataSegmentStorage.StaticArrays,
            _ => DataSegmentStorage.CompressedResource,
        };
    }
}