# WACS.WASIp1 (deprecated)

**This package has been renamed to `WACS.WASI.Preview1`.** It is now a
metapackage that pulls in the new package transitively, so a stale
`<PackageReference Include="WACS.WASIp1" />` continues to restore. Consumer
code must update two lines: the package reference and the using statement.

## Migration

```diff
- <PackageReference Include="WACS.WASIp1" Version="0.11.0" />
+ <PackageReference Include="WACS.WASI.Preview1" Version="0.11.0" />
```

```diff
- using Wacs.WASIp1;
- using Wacs.WASIp1.Types;
+ using Wacs.WASI.Preview1;
+ using Wacs.WASI.Preview1.Types;
```

If your codebase has more than a handful of files, a one-shot sed:

```bash
git ls-files '*.cs' '*.csproj' \
  | xargs sed -i '' \
      -e 's/Wacs\.WASIp1/Wacs.WASI.Preview1/g' \
      -e 's/WACS\.WASIp1/WACS.WASI.Preview1/g'
```

(GNU sed: drop the empty `''` after `-i`.)

## Why the rename

WASI's package family is growing — `WACS.WASI.Preview1`,
`WACS.WASI.Preview2`, and eventually `WACS.WASI.Preview3` need a consistent
naming root. The `WACS.WASIp1` name doesn't scale to additional WASI revisions
without ad-hoc abbreviations. Renaming once now is cheaper than living with
inconsistent prefixes forever.

## What's in this package

Just NuGet metadata and a transitive dependency on `WACS.WASI.Preview1`. The
shipping `Wacs.WASIp1.dll` is an empty assembly. C#'s `TypeForwardedTo`
attribute can move types between assemblies but cannot bridge a namespace
rename, so the migration path is source-level (the sed above).
