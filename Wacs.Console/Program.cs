using System;
using System.Collections.Generic;
using CommandLine;
using FluentValidation;
using Wacs.Core;


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
            }
        }
    }
}
