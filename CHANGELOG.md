# Changelog

## WACS.Transpiler [0.1.0-preview.1] First preview

- New NuGet package: `WACS.Transpiler`. Installs as a dotnet global tool
  (command: `wasm-transpile`). Ahead-of-time transpiles a `.wasm` module
  into a .NET assembly.
- CLI surface mirrors `TranspilerOptions`: `--simd`, `--no-tail-calls`,
  `--max-fn-size`, `--data-storage`, `--gc-checking`.
- `--emit-main` / `--entry-point` / `--main-class` bundle a host
  `Program.Main` into the output assembly for modules with no imports
  and scalar exports.
- Library surface: `Wacs.Transpiler.AOT.ModuleTranspiler.Transpile(...)`
  and `TranspilationResult.SaveAssembly(path)` for programmatic use.
- Spec-equivalent to the WACS interpreter: 473/473 passing on the
  WebAssembly 3.0 spec test suite.
- Known limitations: saved `.dll` is intended for in-process use in
  this preview — cross-process standalone execution (init-data embedded
  into the assembly) is a v0.2 milestone. See
  `Wacs.Transpiler/README.md` for details.

## [0.8.0] Public transpiler surface

- Public getters on ~20 instruction classes, `IFunctionInstance.Invoke`
  on the interface, `Store.ReplaceFunction`, and runtime accessors so
  `WACS.Transpiler` can drive transpilation from outside the assembly.
- No behavior change for existing consumers — additive only.

## [0.7.4] Performance
### Link-time optimization
- Instantiated functions are now flattened into a tape at link time
- Labels, branches, and function call targets are now computed during link
- Addressable store elements can now be precomputed and cached during link
- block, loop, trytable, and end instructions are now flagged as nops and will not incur a dispatch function call
### OpStack resident locals
- Local variables are now allocated on the stack
- Local variable operations now have improved cache locality 
- This refactor is prep for link-time register computation

## [0.7.3]
- Reimplemented AOT compatible invoker bindings

## [0.7.2]
- removing Linq.Expression for AOT compatibility

## [0.7.1]
- fixes to CreateInvoker binding

## [0.7.0]
- wasm-3.0 spec support
- exnref/tag support
- memory64 support
- multi-memory support (enabled)

## [0.6.0]
- wasm-gc extension
- function-references extension

## [0.3.0]
- Implemented JSPI-like async binding and execution
- Hooked up more super-instruction threading

## [0.2.0]
- Implemented super-instruction threading
- Precomputed (non-allocating) block labels

## [0.1.6]
- Updating to latest dll
- Fixing package layout
- Fixing Sample importer

## [0.1.4]
- Initial project setup for Unity.
