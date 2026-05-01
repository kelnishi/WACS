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

## Capability flags

`WasiConfiguration` exposes a few opt-in capability gates beyond the
filesystem preopens:

| Flag | Default | What it enables |
|---|---|---|
| `AllowFileCreation` | `false` | `path_open(O_CREAT)` and `path_create_directory` |
| `AllowFileDeletion` | `false` | `path_unlink_file` and `path_remove_directory` |
| `AllowSymbolicLinks` | `false` | `path_symlink` (creates real host symlinks via `File.CreateSymbolicLink`) |
| `AllowHardLinks` | `false` | `path_link` (P/Invoke `link(2)` on Unix, `CreateHardLinkW` on Windows) |
| `AllowTimeAccess` | `true` | `clock_time_get` and `clock_res_get` |
| `AllowNetworkSockets` | `false` | Preopened sockets (see below) |
| `PreopenHostRootDirectory` | `true` | Auto-binds `HostRootDirectory` as a preopen at fd 3 (legacy `Wacs.Console` behavior; flip false to follow the wasmtime convention where fd 3 is the first explicit preopen) |

## Network sockets

WASI Preview 1's socket surface is intentionally narrow: there is no
`sock_open` / `sock_bind` / `sock_listen`. Listening sockets are
handed in by the embedder as preopens (the same model `wasmtime serve`
uses for HTTP), and `sock_accept` mints connection fds from them.

```csharp
using System.Net;
using System.Net.Sockets;
using Wacs.WASIp1.Types;

var listener = new Socket(AddressFamily.InterNetwork,
    SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen(8);

var wasiConfig = new WasiConfiguration {
    HostRootDirectory = Directory.GetCurrentDirectory(),
    AllowNetworkSockets = true,
    PreopenedSockets = { (listener, FdFlags.NonBlock) },
};
```

The guest's `accept(3)` on fd 3 (after stdio at 0/1/2) will produce a
working TCP connection. Two layers of explicit consent before any
network egress: the flag must be on AND the embedder must have
already constructed and bound the `Socket`.

## Conformance

Tested continuously against the official
[WebAssembly/wasi-testsuite](https://github.com/WebAssembly/wasi-testsuite)
fixtures via `Wacs.WASIp1.Test`. The harness reads each test's
`*.json` manifest, runs the wasm under WACS with the prescribed
preopens / args / env, and asserts on exit code + stdout + stderr. The
test project's `skip.json` documents which conformance fixtures are
deliberately not yet asserting (each entry carries a reason).

## License

WACS is distributed under the [Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE), allowing usage in both open-source and commercial projects.
