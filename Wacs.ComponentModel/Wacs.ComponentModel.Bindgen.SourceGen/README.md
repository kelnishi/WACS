# WACS.ComponentModel.Bindgen.SourceGen

Roslyn incremental source generator that emits **host-side** C#
interfaces from WIT files at compile time. Drives the
`Wacs.WASI.Preview2` package's host-interface surface.

## What it produces

For each WIT interface in your `<AdditionalFiles>`, the generator
emits one `*.g.cs` file containing:

- `public interface IXxx` for each WIT resource (one per resource;
  method signatures only, no `Handle` field, no `Dispose` plumbing
  — that's the host's job)
- `public interface I{InterfaceName}` for the free functions (when
  the WIT interface has any)
- Plain DTO classes for `record` types, abstract base + sealed
  subclasses for `variant` types, C# `enum` for WIT enums,
  `[Flags]` C# enum for WIT flags
- A `[WitSource(@"...")]` attribute on every generated symbol
  carrying the verbatim WIT-text fragment that produced it (used
  by the validation layer to extract a contract from a binding
  type at runtime)

### Faithful type mapping

The generator maps WIT types to C# without ergonomic shortcuts —
the wire shape stays visible:

| WIT | C# |
|---|---|
| `bool`, `s8`..`s64`, `u8`..`u64`, `f32`, `f64`, `char` | `bool`, `sbyte`..`long`, `byte`..`ulong`, `float`, `double`, `uint` |
| `string` | `string` |
| `list<u8>` | `byte[]` |
| `list<T>` | `T[]` |
| `tuple<a, b, ...>` | `(A, B, ...)` |
| `option<T>` | `Option<T>` (NOT `T?`) |
| `result<T, E>` | `Result<T, E>` (in both param and return) |
| `result<_, E>` | `Result<Unit, E>` |
| `result<T, _>` | `Result<T, Unit>` |
| `result` (unit-result) | `Result<Unit, Unit>` |
| `own<R>` / `borrow<R>` | `IR` (the resource interface) |
| `record { ... }` | `public sealed class { public T Field { get; set; } }` |
| `variant { case-a, case-b(T) }` | abstract base + nested sealed subclasses |
| `enum { a, b, c }` | C# `enum` |
| `flags { a, b, c }` | `[Flags]` C# enum, `uint`-backed |

`Result<TOk, TErr>`, `Option<T>`, and `Unit` live in
`Wacs.ComponentModel.Runtime`. Construct via `Result.FromOk(...)` /
`Result.FromErr(...)` / `Option.Some(...)` / `Option.None`.

### Namespace mapping

By default, generated files land under the WIT package's namespace
prefix:

| WIT package | C# namespace (default) |
|---|---|
| `wasi:cli@0.2.3` | `Wasi.Cli.V0_2_3` |
| `wasi:io@0.2.3` | `Wasi.Io.V0_2_3` |

Set `<WitHostNamespaceOverride>` in your csproj to relocate
generated code under your project's tree (and drop the version
suffix):

```xml
<PropertyGroup>
  <WitHostNamespaceOverride>MyCompany.Wasi</WitHostNamespaceOverride>
</PropertyGroup>
```

`Wacs.WASI.Preview2` uses `Wacs.WASI.Preview2` as the override, so
e.g. `wasi:cli` → `Wacs.WASI.Preview2.Cli` (matches the hand-written
binding tree).

## Wiring it into a project

1. Reference the source-gen project as an Analyzer:

```xml
<ItemGroup>
  <ProjectReference
      Include="..\Wacs.ComponentModel.Bindgen.SourceGen\Wacs.ComponentModel.Bindgen.SourceGen.csproj"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false" />
</ItemGroup>
```

2. Reference `Wacs.ComponentModel` (provides `Result<,>`,
   `Option<>`, `Unit`, `WitSourceAttribute`):

```xml
<ItemGroup>
  <ProjectReference Include="..\Wacs.ComponentModel\Wacs.ComponentModel.csproj" />
</ItemGroup>
```

3. List your WIT files as `<AdditionalFiles>` with
   `WitForHost="true"`:

```xml
<ItemGroup>
  <AdditionalFiles Include="wit/**/*.wit" WitForHost="true" />
  <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="WitForHost" />
  <CompilerVisibleProperty Include="WitHostNamespaceOverride" />
</ItemGroup>
```

4. (Optional) Enable persistence under `obj/Generated` so generated
   code is navigable in IDEs:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>obj/Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## Selective emission (per-package gating)

When migrating gradually or targeting a subset, flag only specific
WIT files for emission. Files **not** flagged still get parsed (so
cross-package `use` chains resolve properly), they just don't
contribute output:

```xml
<ItemGroup>
  <!-- Migrate random + clocks first -->
  <AdditionalFiles Include="wit/deps/random/**/*.wit" WitForHost="true" />
  <AdditionalFiles Include="wit/deps/clocks/**/*.wit" WitForHost="true" />

  <!-- Other packages visible but not emitted -->
  <AdditionalFiles Include="wit/**/*.wit"
                   Exclude="wit/deps/random/**/*.wit;wit/deps/clocks/**/*.wit"
                   WitForHost="false" />
</ItemGroup>
```

## What the generator does NOT do

- **No client bindings** — the consumer-side (DllImport stubs +
  handle wrappers for calling INTO a guest component) lives in
  `Wacs.ComponentModel.Bindgen.Lib`'s default emission mode. This
  source generator runs that emitter in `HostInterfaceMode = true`,
  which produces the implement-this-interface shape instead.
- **No binding wire-up** — the generator emits interfaces; you
  write `XxxBindings : IBindable` classes that implement them and
  call `runtime.BindHostFunction<TDelegate>(...)`. See
  `Wacs.WASI.Preview2/{Cli,Http,Filesystem,...}/*Bindings.cs` for
  examples.
- **No DLL load at compile time** — the source-gen embeds its
  emitter logic via `<Compile Include>` from
  `Wacs.ComponentModel/{Types,WIT,CSharpEmit}/`, satisfying
  Roslyn's `EnforceExtendedAnalyzerRules` (no file IO, no
  reflection on user code).

## Cross-package references

WIT supports `use foo:bar.{type-x}` across packages. The generator
resolves the chain via `WitResolver.Resolve(packages)` and emits
fully-qualified `global::Wacs.WASI.Preview2.Io.IPollable`-style
references at use sites. Cross-package types at the C# level just
work — the generator follows alias chains across all loaded
packages, including multi-hop (`filesystem.types` reaching
`io/error.error` through `io/streams.error`).

## Embedded `[WitSource]` metadata

Every generated symbol carries the WIT text it was emitted from:

```csharp
[WitSource(@"interface random",
    Package = "wasi:random@0.2.3", Interface = "random")]
public interface IRandom
{
    [WitSource(@"get-random-bytes: func(len: u64) -> list<u8>;",
        Package = "wasi:random@0.2.3", Interface = "random",
        Item = "get-random-bytes")]
    byte[] GetRandomBytes(ulong len);
    // ...
}
```

This metadata is the bridge to runtime validation — see
[`Wacs.ComponentModel/Validation/README.md`](../Wacs.ComponentModel/Validation/README.md)
for how `WitContract.FromBindingTypes(typeof(IRandom), ...)`
reconstructs the spec from these attributes.

## Implementing a generated interface

The host writes a class that implements the generated interface
and is wired through a `XxxBindings` orchestrator. Faithful types
mean returns are explicit `Result.FromOk(...)` /
`Result.FromErr(...)` rather than throwing:

```csharp
public sealed class MyRandom : IRandom
{
    public byte[] GetRandomBytes(ulong len)
    {
        // Bare-list return — no Result wrapper since the WIT is
        // `func(len: u64) -> list<u8>` (not result-wrapped).
        var buf = new byte[(int)len];
        RandomNumberGenerator.Fill(buf);
        return buf;
    }

    public ulong GetRandomU64()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return BitConverter.ToUInt64(b);
    }
}
```

For `result<...>`-returning methods:

```csharp
public override Result<byte[], StreamError> Read(ulong len)
{
    try
    {
        var buf = ReadFromUnderlyingStream(len);
        return buf.Length == 0
            ? Result<byte[], StreamError>.FromErr(
                new StreamError.StreamErrorClosed())
            : Result<byte[], StreamError>.FromOk(buf);
    }
    catch (Exception e)
    {
        return Result<byte[], StreamError>.FromErr(
            new StreamError.StreamErrorLastOperationFailed(
                new Error(e.Message)));
    }
}
```

## Adding a new WIT package

1. Drop `.wit` files under `wit/deps/<your-package>/` (mirror
   upstream WIT-tree conventions; the file with `package foo:bar;`
   declaration anchors the package).
2. The generator picks them up automatically on next build.
3. Inspect `obj/Generated/.../Wacs_..._YourInterface.g.cs` to see
   what got emitted.
4. Write a `YourBindings : IBindable` orchestrator that wires each
   generated interface method to a `runtime.BindHostFunction<>`
   call. See `Wacs.WASI.Preview2/Random/RandomBindings.cs` for the
   simplest example (~140 lines covers all 3 random interfaces).

## License

Apache-2.0
