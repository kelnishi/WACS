using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;

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
                .WithNotParsed(HandleParseError);
        }

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            // Handle errors (e.g., log them or display a message)
            System.Console.WriteLine("Error occurred while parsing arguments.");
        }

        private void Run(string wasmFilePath)
        {
            var runtime = new WasmRuntime();
            
            //Parse the module
            using var fileStream = new FileStream(wasmFilePath, FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            
            //If you just want to do validation without a runtime, you could do it like this
            var validationResult = module.Validate();
            foreach (var error in validationResult.Errors)
            {
                switch (error.Severity)
                {
                    case Severity.Warning:
                    case Severity.Error:
                        
                        if (error.ErrorMessage.StartsWith("Function["))
                        {
                            var parts = error.ErrorMessage.Split(":");
                            var path = parts[0];
                            var msg = string.Join("",parts[1..]);

                            int line = module.CalculateLine(path, false, out var code);
                            if (!string.IsNullOrWhiteSpace(code))
                                code = $" ({code})";
                            System.Console.Error.WriteLine($"Validation {error.Severity}.{msg}");
                            System.Console.Error.WriteLine($"    at{code} in {wasmFilePath}:line {line}\n");
                        }
                        else
                        {
                            System.Console.Error.WriteLine($"Validation {error.Severity}: {error.ErrorMessage}");
                        }
                        break;
                }
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
            
            
            var wasi = new WASIp1.Wasi(Wasi.GetDefaultWasiConfiguration());
            wasi.BindToRuntime(runtime);
            
            

            //Validation normally happens after instantiation, but you can skip it if you did it after parsing
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true });
            runtime.RegisterModule("hello", modInst);

            var mainAddr = runtime.GetExportedFunction(("hello", "main"));
            
            var caller = runtime.CreateInvoker<Delegates.WasmFunc<int>>(mainAddr);
            int result = caller();
            
            System.Console.WriteLine($"Result was: {result}");
        }
    }
}