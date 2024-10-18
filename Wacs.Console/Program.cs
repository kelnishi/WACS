using System;
using System.Collections.Generic;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;


namespace Wacs.Console
{
    public class Options
    {
        [Value(0, Required = true, HelpText = "Path to the WebAssembly (.wasm) file.")]
        public string WasmFilePath { get; set; }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(options => new Program().Run(options.WasmFilePath))
                .WithNotParsed(errors => HandleParseError(errors));
        }

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            // Handle errors (e.g., log them or display a message)
            System.Console.WriteLine("Error occurred while parsing arguments.");
        }

        private void Run(string wasmFilePath)
        {
            using (var fileStream = new System.IO.FileStream(wasmFilePath, System.IO.FileMode.Open))
            {
                var module = BinaryModuleParser.ParseWasm(fileStream);
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

                var runtime = new WasmRuntime();
                runtime.BindHostFunction("env","sayc", (scalars) =>
                {
                    char c = Convert.ToChar(scalars[0]);
                    System.Console.Write(c);
                    return Array.Empty<object>();
                });
                
                var modInst = runtime.InstantiateModule(module, new(){SkipModuleValidation = true});
                runtime.RegisterModule("hello", modInst);

                var mainAddr = runtime.GetExportedFunction("hello", "main");
                if (mainAddr != null)
                {
                    runtime.Invoke(mainAddr);
                    int steps = 0;
                    while (runtime.ProcessThread())
                    {
                        steps += 1;
                    }
                    System.Console.WriteLine($"Process used {steps} gas.");
                }
                
                
                System.Console.WriteLine(modInst);
            }
        }
    }
}
