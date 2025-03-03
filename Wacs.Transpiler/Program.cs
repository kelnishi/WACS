using System;
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
            string fileName = args.Length > 0 ? args[0] : throw new ArgumentException("No file provided");
            using var fileStream = new FileStream(fileName, FileMode.Open);
        
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var runtime = new WasmRuntime();
            var moduleInst = runtime.InstantiateModule(module);
            var transpiler = new Transpiler();
            foreach (var funcaddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcaddr);
                if (func is FunctionInstance funcInst)
                {
                    transpiler.TranspileFunction(module, funcInst);
                }
            }
        
        }
    }
}