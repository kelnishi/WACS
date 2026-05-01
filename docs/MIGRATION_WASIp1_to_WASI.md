# Migration: `WACS.WASIp1` → `WACS.WASI.Preview1`

The `WACS.WASIp1` package has been renamed to `WACS.WASI.Preview1` to
make room for `WACS.WASI.Preview2` (and eventually `WACS.WASI.Preview3`)
under a single, consistent prefix. This page is the one-stop migration
guide.

The shipped behavior is identical — same types, same methods, same
semantics, same conformance posture against `wasi-testsuite`. The
work is purely a rename.

## TL;DR

```diff
- <PackageReference Include="WACS.WASIp1" Version="0.10.0" />
+ <PackageReference Include="WACS.WASI.Preview1" Version="0.11.0" />
```

```diff
- using Wacs.WASIp1;
- using Wacs.WASIp1.Types;
+ using Wacs.WASI.Preview1;
+ using Wacs.WASI.Preview1.Types;
```

The `Wacs.Core.WASIp1` namespace inside the runtime library
(`IBindable`, `ErrNo`, `SystemExitException`, etc.) is **not**
renamed. It stays where it is — those types are deeply tied to
interpreter wiring conventions and live in `WACS` (the runtime
package), not in the WASI host implementation.

## What you must change

### 1. The package reference

```diff
- <PackageReference Include="WACS.WASIp1" Version="0.10.0" />
+ <PackageReference Include="WACS.WASI.Preview1" Version="0.11.0" />
```

### 2. Every `using Wacs.WASIp1` line

```diff
- using Wacs.WASIp1;
- using Wacs.WASIp1.Types;
+ using Wacs.WASI.Preview1;
+ using Wacs.WASI.Preview1.Types;
```

For codebases with more than a handful of files, a one-shot sed:

```bash
# macOS / BSD sed
git ls-files '*.cs' '*.csproj' \
  | xargs sed -i '' \
      -e 's/Wacs\.WASIp1/Wacs.WASI.Preview1/g' \
      -e 's/WACS\.WASIp1/WACS.WASI.Preview1/g'

# GNU sed (Linux): drop the empty '' after -i
```

The two patterns cover:

- `using Wacs.WASIp1;` → `using Wacs.WASI.Preview1;`
- `Wacs.WASIp1.Wasi` → `Wacs.WASI.Preview1.Wasi` (fully-qualified refs)
- `<PackageReference Include="WACS.WASIp1" />` → new id
- Doc comments and string literals naming the old package

The `Wacs.Core.WASIp1` namespace is unaffected by the patterns above
because the substring is `Wacs.WASIp1` (no `.Core.` segment), so the
sed leaves it alone.

## What you don't have to change

- **`using Wacs.Core.WASIp1;`** stays — `IBindable`, `ErrNo`,
  `SystemExitException`, `SignalException`, and `[Signal]` live in
  `Wacs.Core` and didn't move.
- **The `Wasi` class API** — `new Wasi(WasiConfiguration)`,
  `wasi.BindToRuntime(runtime)`, every `WasiConfiguration` flag, every
  field — all identical.
- **Runtime behavior** — the `wasi_snapshot_preview1` host functions
  produce the same outputs for the same inputs.
- **`WasiConfiguration` flags** added in 0.11.0 (e.g.
  `AllowNetworkSockets`, `PreopenedSockets`,
  `PreopenHostRootDirectory`) are additive; they default to safe
  values that match the old behavior.

## Back-compat: the deprecated metapackage

If you can't update right away, the old `WACS.WASIp1` package id still
restores. As of 0.11.0 it is a **metapackage** with no code of its
own — it transitively pulls in `WACS.WASI.Preview1`, so the binary on
disk is the new assembly.

That gets you a working build, but **C# `using Wacs.WASIp1;` lines
will still fail to compile** against the new assembly. C#'s
`TypeForwardedTo` attribute can move types between assemblies but
cannot bridge a namespace rename. The one-shot sed above is the
required next step.

The metapackage will be marked deprecated on NuGet.org one release
after `WACS.WASI.Preview1` ships and will be removed entirely after
two further minor versions. Update your `using` lines and your
package reference together.

## Why the rename

WASI's package family is growing:

| Package | Purpose | Status |
|---|---|---|
| `WACS.WASI.Preview1` | `wasi_snapshot_preview1` core-module host | shipping |
| `WACS.WASI.Preview2` | Component-Model `wasi:*` 0.2.x hosts | shipping |
| `WACS.WASI.Preview3` | Async `wasi:*` 0.3.x hosts | tracked |
| `WACS.WASI.Threads` | `wasi-threads` proposal | shipping |

`WACS.WASIp1` doesn't scale to additional WASI revisions without
ad-hoc abbreviations. Renaming once now is cheaper than living with
inconsistent prefixes forever.

## See also

- [Package reference](https://www.nuget.org/packages/WACS.WASI.Preview1)
- [`Wacs.WASI.Preview1/README.md`](../Wacs.WASI.Preview1/README.md) — usage, capability flags, sockets
- [`CHANGELOG.md`](../CHANGELOG.md) — full release notes for 0.11.0
