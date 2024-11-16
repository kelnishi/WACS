# WACS - WebAssembly Interpreter

**WACS** is a pure C# WebAssembly Interpreter designed for .NET environments, including Unity's IL2CPP.

## Features

- **Execute WebAssembly**: Load and run .wasm files or data streams.
- **Interpreter-Only**: No JIT compilation, works in AOT modes like IL2CPP on iOS.
- **Magic Interop**: Reflection based interop allows for easy function binding with little boilerplate code.
- **Open Source**: Library is fully open source, free to inspect, change, and improve.

## Unity Installation

   - Window>Package Manager
   - Click +  Add package from git URL...
   - Enter the repo URL: ```https://github.com/kelnishi/WACS```
   - Click Add

## Usage

To use Wacs, you'll need to instantiate a runtime and a module.

```csharp
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

public class ExampleClass : MonoBehaviour
{
    [SerializeField] private WasmAsset wasmAsset;
    public string moduleName = "_";
    
    void Start()
    {
        var stream = new MemoryStream(wasmAsset.data);
        var module = BinaryModuleParser.ParseWasm(stream);
        
        _runtime = new WasmRuntime();
        _output = new();
        _runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
        {
            _output.Append(c);
        });
        
        _moduleInstance = _runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true});
        _runtime.RegisterModule(moduleName, _moduleInstance);
        
        var fa = runtime.GetExportedFunction((moduleName, "main"));
        var main = runtime.CreateInvoker<Func<int>>(fa);
        
        //Execute the module
        int result = main();
    }
}
```
