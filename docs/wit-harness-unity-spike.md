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

## Next steps (per the original plan)

- [x] Hand-written harness compiles + NativeAOT-publishes + runs.
- [ ] Verify on Unity IL2CPP (drop `Wacs.Core` + the harness into
  a minimal Unity project, build for an IL2CPP-target platform,
  run on device).
- [ ] Address findings A–D, particularly A (the IL2CPP blocker
  for any binding side that touches WASI).
- [ ] `WACS.ComponentModel.Harness.Runtime` — extract the
  canonical-ABI memory readers/writers + the
  `ComponentLoader.Load` / `WitContractCompare.Match` shape
  from this spike.
- [ ] `WACS.ComponentModel.Harness.SourceGen` — emit the shape
  above from a WIT contract.
- [ ] `docs/WIT_HARNESS_USAGE.md` — embedder guide.
- [ ] `Spec.Test/components/fixtures/unity-harness-demo/` — full
  IL2CPP demo project.
