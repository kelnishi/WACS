# WIT-harness AOT spike — findings

Package 3 of [wit-harness-plan.md](wit-harness-plan.md). Spike
goal: prove the typed-harness pattern is viable under NativeAOT
(the IL2CPP proxy) before building `WACS.ComponentModel.Harness.SourceGen`
and `WACS.ComponentModel.Harness.Runtime`. NativeAOT was chosen as
the iteration platform — the constraints overlap with IL2CPP
(no `MakeGenericType`, no `MakeGenericMethod`, generic
specializations must be statically rooted) and the toolchain is
faster to drive than Unity.

## Result

**Pattern verified end-to-end.** A hand-written typed harness for
a single-export WIT world (`greet: func(name: string) -> string`)
compiles, NativeAOT-publishes, and runs as a native binary that
returns `"Hello, World!"`.

## Spike layout

- `Spec.Test/components/fixtures/wit-harness-spike-hello/`
  - `wit/world.wit` — the WIT contract (single export `greet`).
  - `src/lib.rs` + `Cargo.toml` — Rust source compiled with
    `wit-bindgen` 0.41 to `wasm32-wasip2`, then bundled via
    `wasm-tools component new` into `.component.wasm`.
  - `wasm/hello.component.wasm` — 41 730-byte built artifact.
    Main core module exports: `memory`, `cabi_post_greet`,
    `greet`, `cabi_realloc`. No start function.
  - `Aot.Spike/` — the spike's C# consumer.
    - `WitHarnessSpike.Aot.csproj` — `net8.0`, `PublishAot=true`,
      `IsAotCompatible=true`, `TrimMode=full`,
      `InvariantGlobalization=true`. References only `Wacs.Core`
      and `Wacs.ComponentModel` (for the AOT-safe parser).
    - `HelloHarness.cs` — the hand-written typed harness.
    - `Program.cs` — entry point: load bytes, call
      `harness.Greet("World")`, exit 0 / 1 / 2.

## Harness shape (what the SourceGen must emit)

```csharp
public sealed class HelloHarness {
    public static HelloHarness LoadFrom(byte[] componentBytes) {
        // 1. Parse the .component.wasm wrapper.
        //    Wacs.ComponentModel.Runtime.Parser is pure byte walk,
        //    no reflection — AOT-clean today.
        var component = ComponentBinaryParser.Parse(new MemoryStream(componentBytes));
        var coreModuleBytes = component.CoreModuleBinaries.First();

        // 2. Parse + instantiate that core module via Wacs.Core
        //    (AOT-clean).
        var runtime = new WasmRuntime();
        BindWasiStubs(runtime);
        var coreModule = BinaryModuleParser.ParseWasm(new MemoryStream(coreModuleBytes));
        var moduleInst = runtime.InstantiateModule(coreModule);
        runtime.RegisterModule("hello", moduleInst);

        // 3. Walk exports → typed delegates. The generic
        //    instantiations (Func<int,int,int,int,int>,
        //    Func<int,int,int>, Action<int>) are statically rooted
        //    at this call site — IL2CPP sees them all when
        //    transpiling the user's assembly.
        var realloc   = runtime.CreateInvokerFunc<int,int,int,int,int>(reallocAddr);
        var greet     = runtime.CreateInvokerFunc<int,int,int>(greetAddr);
        var postGreet = runtime.CreateInvokerAction<int>(postGreetAddr);
        return new HelloHarness(runtime, memory, realloc, greet, postGreet);
    }

    public string Greet(string name) {
        // Canonical-ABI lower: realloc a UTF-8 buffer, copy in.
        var utf8 = Encoding.UTF8.GetBytes(name);
        int inPtr = _reallocInvoke(0, 0, 1, utf8.Length);
        utf8.CopyTo(_memory.Data, inPtr);

        // Invoke. Returns a pointer to an 8-byte (ptr, len) tuple.
        int retArea = _greetInvoke(inPtr, utf8.Length);

        // Lift: read (ptr, len), copy out as UTF-8 string.
        int outPtr = ReadI32LE(_memory, retArea);
        int outLen = ReadI32LE(_memory, retArea + 4);
        var result = Encoding.UTF8.GetString(_memory.Data, outPtr, outLen);

        // Free the return area.
        _postGreetInvoke(retArea);
        return result;
    }
}
```

The generator must emit *exactly* this shape for the runtime to
be AOT-clean — no Activator, no MakeGenericType, no GetMethod, no
DynamicInvoke in user code.

## Verification

```bash
# In-process (dev mode):
$ dotnet run -c Release \
    --project Spec.Test/components/fixtures/wit-harness-spike-hello/Aot.Spike
Hello, World!

# NativeAOT publish:
$ dotnet publish -c Release \
    Spec.Test/components/fixtures/wit-harness-spike-hello/Aot.Spike/WitHarnessSpike.Aot.csproj
…
WitHarnessSpike.Aot ->
  …/bin/Release/net8.0/osx-arm64/publish/

$ ./Spec.Test/components/fixtures/wit-harness-spike-hello/Aot.Spike/bin/Release/net8.0/osx-arm64/publish/WitHarnessSpike.Aot
Hello, World!
```

## Findings — bugs surfaced + fixed by the spike

### 1. `CreateInvokerFunc<…,TResult>` couldn't unbox primitives

`Delegates.AnonymousFunctionFromType` wraps the wasm return in
`new Value(...)`. `DynamicInvoke` boxes that `Value` struct, then
the lambda body did `(TResult)boxed`. For `TResult == int`, that's
unbox-to-int against a boxed `Value` — `InvalidCastException`
every time.

Fix shipped in WACS 0.15.22: `UnboxReturn<TResult>` first unboxes
to `Value`, then dispatches through `Value`'s implicit operators
for `int` / `uint` / `long` / `ulong` / `float` / `double`. Every
`CreateInvokerFunc` arity now routes through it. The void path
(`CreateInvokerAction`) was unaffected.

## Findings — items the productionization work needs to address

### A. AOT trim warnings in `Wacs.Core`'s host-binding path

NativeAOT publish surfaced (under `TrimmerSingleWarn=false`):

- `WasmRuntimeBinding.cs:515,529` — `BindHostFunction<TDelegate>`
  uses `del.GetType().GetMethod("Invoke")` for reflection-based
  delegate introspection. Trim flagger doesn't know `TDelegate`
  preserves `PublicMethods`.
- `WasmRuntimeExecution.cs:459` — `CreateInvoker`'s
  `GenericDelegate` constructs exceptions via reflection on the
  caught exception's type.
- `Types/HostFunction.cs:327` — `HostFunction.InvokeAsync` uses
  `GetType().GetProperty(…)`.

The spike's harness consumed these paths and still ran (NativeAOT
preserved enough metadata via the application manifest, and the
warnings collapsed to a single `IL2104` for the assembly). For
IL2CPP, the safer move is to either:

- Add `[DynamicallyAccessedMembers]` on `TDelegate` and the
  callers, plumbing the annotation back to the user-facing
  `BindHostFunction`, or
- Replace the reflection-based binding API with a typed
  per-arity binding API (mirrors what the SourceGen would
  generate anyway), and treat the reflective path as the
  JIT-only convenience.

The SourceGen needs to emit harness code that doesn't traverse
those paths regardless — but the binding side (WASI stubs in the
spike, real WASI implementations in production) does, so cleaning
this up is on the critical path for IL2CPP.

### B. WASI imports auto-bundled by `wasm32-wasip2`

Even with `generate_all` left off, the `wasm32-wasip2` target
pulls in ~20 `wasi:io` / `wasi:cli` imports. The spike sidesteps
by binding stubs that throw — `greet` is pure string formatting
so the stubs never fire — but a production harness will need a
real WASI implementation wired through `BindHostFunction`. The
existing `Wacs.WASI.Preview2` bundle is the obvious target,
which means item A is a hard dependency for the production
harness story.

### C. `Wacs.ComponentModel` triggers `NETSDK1210` during AOT publish

`Wacs.ComponentModel` multi-targets net8.0 + netstandard2.1.
`IsAotCompatible=true` is unconditional, but
`IsAotCompatible / EnableAotAnalyzer` aren't supported for
netstandard2.1 — `NETSDK1210` warning fires on every consumer
publish. Cosmetic; the same condition fix that `Wacs.Core.csproj`
uses
(`<IsAotCompatible Condition="'$(TargetFramework)' == 'net8.0'">true</IsAotCompatible>`)
applies. Roll into the harness PR.

### D. Spike consumes `Wacs.ComponentModel` *parser only* — **RESOLVED**

The harness only pulls `ComponentBinaryParser` from the parser
namespace; the reflective surfaces (`ComponentInstance`,
`ComponentBridge`, `HostInterfaceRuntime.InvokeStaticFactoryReflective`,
etc.) are annotated `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`
from the warning-hygiene pass, so they're correctly excluded
from the AOT call graph. Validates the architectural split — the
parser belongs in a runtime-callable surface, the reflective
bridge belongs behind the AOT-gated annotations.

**Resolved** by the `WACS.ComponentModel.Parser` package split
(CHANGELOG entry "WACS.ComponentModel.Parser 0.1.0 /
WACS.ComponentModel 0.5.1 — AOT-safe parser split"). The spike's
`Aot.Spike/WitHarnessSpike.Aot.csproj` now references
`Wacs.ComponentModel.Parser` directly instead of the umbrella
`Wacs.ComponentModel`. Side benefit: the previously-tracked
`NETSDK1210` warning (finding C) also resolves for harness
consumers because the new package gates `IsAotCompatible` to
net8.0 only — finding C remains open against the existing
`Wacs.ComponentModel` csproj.

## Productionization status

**Shipped in the `wit-harness-spike` arc (9 checkpoint commits):**

- [x] Hand-written harness compiles + NativeAOT-publishes + runs.
- [x] `WACS.ComponentModel.Parser 0.1.0` — AOT-safe parser split
  out of `Wacs.ComponentModel` (closes finding D above; harness
  consumers no longer pull the reflective surface).
- [x] `WACS.ComponentModel.Harness.Runtime 0.3.0` — canonical-ABI
  primitives (`MemoryHelpers`, `StringCoding`), `HarnessLoader`
  (parse + instantiate + export resolution helpers the emitted
  `LoadFrom` calls into).
- [x] `WACS.ComponentModel.Harness.Lib 0.3.0` — IL emitter
  (PersistedAssemblyBuilder, net9.0). Handles primitives,
  records of primitives, variants of {unit, primitive, record}
  cases, string-in/string-out exports. Emits a `I{World}`
  symmetric interface alongside `{World}Harness`, plus a
  `_WitContract` static field carrying the raw WIT source.
- [x] `WACS.Cli 1.10.0` — `wacs harness <wit-dir> -o <out.dll>`
  verb; `--harness` / `--wit-dir` flags on `wacs aot` + `wacs build`
  that thread the contract through to the transpiler.
- [x] `WACS.Transpiler.Lib 0.10.2` — compile-time WIT contract
  validation via `TranspilerOptions.HarnessContractText` +
  `WitContractCompare.Diff`. Exports + imports diffed
  structurally; typed mismatch report; throws before any IL is
  emitted on contract drift.
- [x] Richer fixture
  (`Spec.Test/components/fixtures/wit-harness-spike-richer/`):
  multi-export world with record + variant, generated-validate
  console asserts behavioral equivalence to a hand-shaped
  reference.

**Deferred (next-mile v1/v2 work; tracked here so the gaps
stay explicit):**

- [ ] Unity IL2CPP verification on-device. Skipped per user
  direction once the NativeAOT spike passed — IL2CPP and
  NativeAOT share the no-`MakeGenericType` / no-runtime-emit
  constraints the spike exercised.
- [ ] **`BinaryWitDecoder` primary-section decode**: existing
  `BuildPackage` is hardcoded for `wit-component`'s nested-
  wrapper encoding. Cargo-built `wasm32-wasip2` components don't
  ship that wrapper; their WIT lives in the primary
  type/export sections. Today's workaround: run
  `wasm-tools component embed` to add the section. Lifting this
  needs ~200-400 LOC of new decoder logic.
- [ ] **Transpiler emits `implements I{World}`**: the
  CLR-interface-level engine-symmetry payoff. Requires either
  cross-assembly type sharing (transpiler references the
  harness's `Vec2` / `Outcome` types) or a translation layer
  at the interface boundary. ~500-1000 LOC coupled to
  `ComponentExportsEmit`'s 3281-LOC class-emission pipeline.
  Embedders today get typed call sites via the interpreter
  harness + compile-time validation via `--harness` on
  transpile; this closes the cross-engine identity gap.
- [ ] **More WIT shapes in records / variants** — strings, lists,
  options, results, nested records inside records. Each is an
  incremental emitter addition (~200 LOC per shape).
- [ ] **Interface-reference imports** in the validator: the
  v0 diff surfaces these as "not validated" rather than
  matching them. Needs WIT-side interface resolution.
- [ ] **Runtime-side validation in `HarnessLoader.Load`**:
  embedded `_WitContract` + `BinaryWitDecoder` + the same
  `WitContractCompare` machinery. Has a dependency cost
  (`Harness.Runtime` would need to pull `Wacs.ComponentModel`'s
  WIT-parser surface).
- [ ] **`wacs run --harness` / `--wit-dir`** — only meaningful
  once primary-section decode or runtime-validation lands;
  without those it's a no-op on cargo-built fixtures.
- [ ] **`WACS.ComponentModel.Harness.SourceGen`** — Roslyn
  source-gen variant of the same emit core, for embedders who
  prefer the build-time-codegen workflow over a generated
  `.dll`. The CLI verb is the high-value distribution path; the
  source-gen is additive.
- [ ] **`docs/WIT_HARNESS_USAGE.md`** — embedder guide.
- [ ] **`Spec.Test/components/fixtures/unity-harness-demo/`** —
  full IL2CPP demo project.
