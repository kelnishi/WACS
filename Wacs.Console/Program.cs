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
                        System.Console.Error.WriteLine($"Validation {error.Severity}: {error.ErrorMessage}");
                        break;
                }
            }
            
            //Bind module dependencies prior to instantiation
            runtime.BindHostFunction<Action<int>>(("env", "sayc"), p =>
            {
                char c = Convert.ToChar(p);
                System.Console.Write(c);
            });


            //Validation normally happens after instantiation, but you can skip it if you did it after parsing
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true });
            runtime.RegisterModule("hello", modInst);

            var mainAddr = runtime.GetExportedFunction(("hello", "main"));
            if (mainAddr == null) return;
            
            var caller = runtime.CreateInvoker<Delegates.WasmFunc<int>>(mainAddr);
            int result = caller();
                
            System.Console.WriteLine($"Result was: {result}");
        }
    }
}