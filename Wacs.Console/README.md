# Wacs.Console

The `wacs` CLI tool (NuGet `WACS.Cli`) — supersedes the original
`wasm-transpile`. One verb each for run / build / aot / inspect /
bindgen, with `--wasi` / `--wasip2` flags that bake in the Preview 1 / 2
host packages on demand.

## Contents

- **[Wacs.Console/](Wacs.Console/)** — single-project family. `OutputType=Exe` distributed as `dotnet tool install -g WACS.Cli`. Reads command verbs from `Verbs/`; routes `wacs aot` through `Wacs.Transpiler.Lib` for transpile + `dotnet publish /p:PublishAot` in one shot.

Bundled wasm fixtures under `Wacs.Console/Data/` (coremark, wasm2wat,
wast2json, perl) ride along for offline `wacs run` smoke tests and as
the canonical inputs for `Wacs.Bench` / `Wacs.OpProfile`.
