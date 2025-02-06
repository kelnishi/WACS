using Godot;
using System;
using System.IO;
using System.Text;
using Wacs.Core;
using Wacs.Core.Runtime;
using FileAccess = Godot.FileAccess;

public partial class Node2d : Node2D
{
    public override void _Ready()
    {
        var runtime = new WasmRuntime();

        //Bind a host function (for demonstration purposes)
        var sb = new StringBuilder();
        runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
        {
            sb.Append(c);
        });

        //Open a wasm file from resources
        string filePath = "res://Data/HelloWorld.wasm";
        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PrintErr($"Could not open file {filePath}");
            return;
        }

        //Wrap the data in a MemoryStream for parsing
        using var memStream = new MemoryStream(file.GetBuffer((long)file.GetLength()));
        
        //Parse the wasm data into a module
        var module = BinaryModuleParser.ParseWasm(memStream);

        //Instantiate the module
        var modInst = runtime.InstantiateModule(module);

        //Register the module to add its exported functions to the export table
        runtime.RegisterModule("hello", modInst);

        //Get the module's exported function
        if (runtime.TryGetExportedFunction(("hello", "main"), out var mainAddr))
        {
            //For wasm functions you can expect return types as Wacs.Core.Runtime.Value
            //  Value has implicit conversion to many useful primitive types
            var mainInvoker = runtime.CreateInvokerFunc<Value>(mainAddr);
    
            //Call the wasm function and get the result
            //  Implicit conversion from Value to int
            int result = mainInvoker();
            
            //The bound host function will have filled the string builder
            GD.Print(sb.ToString());
        }
    }
}