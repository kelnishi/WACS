# WACS.WASIp1 for WACS

A C# implementation of WASI preview 1 for the WACS WebAssembly Interpreter.

## Installation

Add the assembly from NuGet:
```bash
dotnet add package WACS.WASIp1
```

## Usage Example

Here's a basic example demonstrating how to bind WASIp1 to the WACS WebAssembly runtime:

```csharp
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;

var runtime = new WasmRuntime();
var wasiConfig = new WasiConfiguration() {
    StandardInput = System.Console.OpenStandardInput(),
    StandardOutput = System.Console.OpenStandardOutput(),
    StandardError = System.Console.OpenStandardError(),
    
    Arguments = Environment.GetCommandLineArgs()
        .Skip(1)
        .ToList(),
    
    EnvironmentVariables = Environment.GetEnvironmentVariables()
        .Cast<DictionaryEntry>()
        .ToDictionary(de => de.Key.ToString()!, de => de.Value?.ToString()??""),
    
    HostRootDirectory = Directory.GetCurrentDirectory(),
};
var wasi = new WASIp1.Wasi(wasiConfig);
wasi.BindToRuntime(runtime);

using var fileStream = new FileStream("module.wasm", FileMode.Open);
var module = BinaryModuleParser.ParseWasm(fileStream);

var modInst = runtime.InstantiateModule(module);
runtime.RegisterModule("mymodule", modInst);

if (runtime.TryGetExportedFunction(("mymodule", "main"), out var mainAddr))
{
    try
    {
        var mainInvoker = runtime.CreateInvoker<Func<Value>>(mainAddr);
        int result = mainInvoker();
        Console.Error.WriteLine($"mymodule.main() => {result}");
    }
    catch (TrapException exc)
    {
        System.Console.Error.WriteLine(exc);
        return 1;
    }
    catch (SignalException exc)
    {
        System.Console.Error.WriteLine($"{exc.HumanReadable}");
        return exc.Signal;
    }
}
```

## License

WACS is distributed under the [Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE), allowing usage in both open-source and commercial projects.
