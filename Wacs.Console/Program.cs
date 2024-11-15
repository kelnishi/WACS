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
using System.IO;
using System.Linq;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.WASIp1;
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
            
            var parser = new Parser(with => {
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
            string fileExtension = Path.GetExtension(opts.WasmModule);
            if (fileExtension != ".wasm")
            {
                System.Console.Error.WriteLine($"Error: Invalid file extension: {fileExtension}. Expected .wasm");
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
            
            //Parse the module
            using var fileStream = new FileStream(opts.WasmModule, FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);

            if (opts.Render)
            {
                string outputFilePath = Path.ChangeExtension(opts.WasmModule, ".wat");
                using var outputStream = new FileStream(outputFilePath, FileMode.Create);
                ModuleRenderer.RenderWatToStream(outputStream, module);
            }
            
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
                            var msg = string.Join(":",parts[1..]);

                            var (line, code) = module.CalculateLine(path);
                            if (!string.IsNullOrWhiteSpace(code)) code = $" ({code})";
                            var (fline, _) = module.CalculateLine(path, functionRelative: true);
                            
                            System.Console.Error.WriteLine($"Validation {error.Severity}.{msg}");
                            System.Console.Error.WriteLine($"    {path}");
                            System.Console.Error.WriteLine($"    at{code} in {opts.WasmModule}:line {line} ({fline})");
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
            var wasi = new WASIp1.Wasi(wasiConfig);
            wasi.BindToRuntime(runtime);
            
            string moduleName = opts.ModuleName;
            
            if (opts.LogProg)
                System.Console.Error.WriteLine($"Instantiating Module {moduleName}");

            //Validation normally happens after instantiation, but you can skip it if you did it after parsing, or you're like super confident.
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true});
            runtime.RegisterModule(moduleName, modInst);


            var callOptions = new InvokerOptions {
                LogGas = opts.LogGas,
                GasLimit = opts.LimitGas,
                LogProgressEvery = opts.LogProgressEvery, 
                LogInstructionExecution = opts.LogInstructionExecution,
                CalculateLineNumbers = opts.CalculateLineNumbers,
                CollectStats = opts.CollectStats,
            };

            //Wasm/WASI entry points
            if (modInst.StartFunc != null)
            {
                var caller = runtime.CreateInvoker<Action>(modInst.StartFunc, callOptions);

                var name = runtime.GetFunctionName(modInst.StartFunc);
                if (opts.LogProg)
                    System.Console.Error.WriteLine($"Executing wasm function {name}");
                
                using (IDisposable _ = opts.Profile? new ProfilingSession(): new NoOpProfilingSession())
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
                
                var caller = runtime.CreateInvoker<Action>(startAddr, callOptions);
                
                using (IDisposable _ = opts.Profile? new ProfilingSession(): new NoOpProfilingSession())
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
                
                using (IDisposable _ = opts.Profile? new ProfilingSession(): new NoOpProfilingSession())
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

                        Value [] result = caller(pVals);
                        
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
    }
}