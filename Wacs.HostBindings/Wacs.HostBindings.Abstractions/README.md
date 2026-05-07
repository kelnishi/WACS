# WACS.HostBindings.Abstractions

The shared vocabulary for WACS host bindings. Exposes:

- `[WacsImport(string module, string name)]` — annotate a static method as
  the implementation of a wasm import. Consumed by
  `WACS.HostBindings.SourceGen` at consumer build time.
- `WacsHostMemory` — a 16-byte readonly struct view over wasm linear memory,
  passed as the first parameter to every binding method. Bounds-checked
  accessors plus an `AsSpan` escape hatch for bulk I/O.
- `WacsHostFault` — exception type for binding-side traps. Surfaced as a
  wasm runtime fault by the caller.

Tiny by design. No runtime allocations, AOT-clean, netstandard2.0 so the
matching Roslyn source generator can reference it directly.

## Authoring a host binding

```csharp
public static class MyEngineBindings
{
    [WacsImport("game_engine", "log")]
    public static void Log(WacsHostMemory mem, int strPtr, int strLen)
    {
        var span = mem.AsSpan(strPtr, strLen);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(span));
    }

    [WacsImport("game_engine", "rand")]
    public static int Rand(WacsHostMemory mem, int max)
        => Random.Shared.Next(max);
}
```

The first parameter is always `WacsHostMemory` (host code may need it).
Subsequent parameters match the wasm import's typed signature.

## Per-call shared state

Bindings that need long-lived state (file descriptors, configuration, etc.)
take a state parameter as the second argument:

```csharp
public static class WasiPreview1Bindings
{
    [WacsImport("wasi_snapshot_preview1", "fd_write")]
    public static int FdWrite(WacsHostMemory mem, State state,
                              int fd, int iovs, int iovs_len, int nwritten)
    {
        // … implementation backed by `state.FileDescriptors[fd]`
    }
}
```

The source generator detects state-typed parameters via type identity and
threads them through from a constructor on `GeneratedHostImports`.
Multiple bindings sharing the same state type share the same constructor
parameter.
