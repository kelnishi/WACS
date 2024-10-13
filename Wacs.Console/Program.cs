using System;
using System.Collections.Generic;
using Wacs.Core;
using Wacs.Core.Validation;

using CommandLine;

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
                var module = ModuleParser.Parse(fileStream);
                
                var result = ValidationUtility.ValidateModule(module);
                
                System.Console.WriteLine(result);
            }
        }
    }
}
