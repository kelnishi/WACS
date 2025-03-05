using System;
using System.Diagnostics;
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Transpiler
{
    class Program
    {
        static void Main(string[] args)
        {
            Stopwatch timer = new();
            timer.Start();
            
            string fileName = args.Length > 0 ? args[0] : throw new ArgumentException("No file provided");
            using var fileStream = new FileStream(fileName, FileMode.Open);
        
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var runtime = new WasmRuntime();
            var moduleInst = runtime.InstantiateModule(module);
            
            timer.Stop();
            Console.WriteLine($"Parsing and Instantiation took {timer.ElapsedMilliseconds}ms");
            var transpiler = new Transpiler();
            transpiler.TranspileModule(runtime, moduleInst);
        }
    }
}