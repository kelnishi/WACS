# WACS (C# WebAssembly Interpreter)

## Overview

**WACS** is a pure C# WebAssembly Interpreter designed for .NET environments, including Unity's IL2CPP. It allows seamless execution of WASM modules with minimal setup, offering compatibility and advanced interop features.

## Features

- **Pure C# Implementation**: Built with C# 9.0/.NET Standard 2.1 (no unsafe code).
- **Unity Compatibility**: Supports **Unity 2021.3+** with IL2CPP/AOT compatibility.
- **Full WebAssembly MVP Compliance**: Passes the WebAssembly spec test suite.
- **Interop Bindings**: Host bindings created through reflection, requiring no boilerplate.
- **WASI Support**: WACS.WASIp1 provides a wasi_snapshot_preview1 implementation.

## Installation

Install WACS from NuGet:
```bash
dotnet add package WACS
```

## Usage Example

Here's a basic example demonstrating how to load and run a WebAssembly module:

```csharp
using System;
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;

var runtime = new WasmRuntime();
runtime.BindHostFunction<Action<char>>(("env", "sayc"), c => Console.Write(c));

using var fileStream = new FileStream("HelloWorld.wasm", FileMode.Open);
var module = BinaryModuleParser.ParseWasm(fileStream);

var modInst = runtime.InstantiateModule(module);
runtime.RegisterModule("hello", modInst);

if (runtime.TryGetExportedFunction(("hello", "main"), out var mainAddr))
{
    var mainInvoker = runtime.CreateInvoker<Func<Value>>(mainAddr);
    int result = mainInvoker();
    Console.Error.WriteLine($"hello.main() => {result}");
}
```

## License

WACS is distributed under the [Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE), allowing usage in both open-source and commercial projects.
