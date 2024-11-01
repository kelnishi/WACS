using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Console
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Options
    {
        [Value(0, Required = true, HelpText = "Path to the WebAssembly (.wasm) file.")]
        public string WasmFilePath { get; init; } = "";
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => new Program().Run(options.WasmFilePath))
                .WithNotParsed(errors =>
                {
                    var messages = string.Join("|",errors.Select(e=> e.ToString()));
                    System.Console.WriteLine($"Error occurred while parsing arguments. {messages}");
                });
        }

        private void Run(string wasmFilePath)
        {
            var runtime = new WasmRuntime();
            
            //Parse the module
            using var fileStream = new FileStream(wasmFilePath, FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            
            string outputFilePath = Path.ChangeExtension(wasmFilePath, ".wat");
            using var outputStream = new FileStream(outputFilePath, FileMode.Create);
            ModuleRenderer.RenderWatToStream(outputStream, module);
            
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
                            var (fline, fcode) = module.CalculateLine(path, functionRelative: true);
                            
                            System.Console.Error.WriteLine($"Validation {error.Severity}.{msg}");
                            System.Console.Error.WriteLine($"    {path}");
                            System.Console.Error.WriteLine($"    at{code} in {wasmFilePath}:line {line} ({fline})");
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

            runtime.BindHostFunction<Action<int>>(("env", "emscripten_notify_memory_growth"), p =>
            {
                System.Console.WriteLine($"Emscripten notify memory growth {p}");
            });
            
            
            var wasi = new WASIp1.Wasi(Wasi.DefaultConfiguration());
            wasi.BindToRuntime(runtime);
            
            System.Console.WriteLine("Instantiating Module");

            string moduleName = "hello";
            //Validation normally happens after instantiation, but you can skip it if you did it after parsing or you're like super confident.
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true});
            runtime.RegisterModule(moduleName, modInst);


            var callOptions = new InvokerOptions {
                LogGas = true,
                LogProgressEvery = 0x40_0000, 
                LogInstructionExecution = false,
                CalculateLineNumbers = false,
                CollectStats = true,
            };
            
            //Wasm/WASI entry points
            if (modInst.StartFunc != null)
            {
                var caller = runtime.CreateInvoker<Delegates.WasmAction>(modInst.StartFunc, callOptions);    
                
                System.Console.WriteLine("Calling start");
                caller();
            }
            else if (runtime.TryGetExportedFunction((moduleName, "main"), out var mainAddr)) 
            {
                var caller = runtime.CreateInvoker<Func<Value>>(mainAddr, callOptions);
                
                System.Console.WriteLine("Calling main");
                int result = caller();
                
                System.Console.WriteLine($"Result was: {result}");
            }
            
        }
    }
}