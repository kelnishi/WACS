# Browser-Idiom Features (✳️)

Three WebAssembly proposals are *defined against a JavaScript host* — their
normative text is written in terms of JS primitives (`BigInt`, JS strings,
`Promise`) because that's the embedding the Web group specifies. WACS is
not a JS runtime, but each proposal's *observable semantics* translate
cleanly to a native .NET primitive, so modules that depend on these
features run on WACS without modification.

This file documents the three ✳️ rows in the feature table: what the
proposal does in a browser host, what WACS binds it to instead, and the
host-side API surface for each.

---

## JavaScript BigInt ↔ WebAssembly i64 Integration

[Proposal](https://github.com/WebAssembly/JS-BigInt-integration) · [Finished
2020-06-09, WebAssembly 2.0](https://github.com/WebAssembly/proposals/blob/main/finished-proposals.md)

**In a JS host.** JavaScript numbers are IEEE-754 doubles, which cannot
represent the full 64-bit integer range. Before this proposal, `i64`
values could not cross the JS↔WASM boundary at all — calling a wasm
function that took or returned `i64` threw `TypeError`. The proposal
routes i64 through the `BigInt` primitive: wasm exports returning `i64`
yield a `BigInt` in JS, and JS passes a `BigInt` in for `i64` parameters.

**In WACS.** C# has a first-class 64-bit integer type: `long`. WACS's
delegate marshaling maps wasm `i64 ↔ typeof(long)` directly
(`Wacs.Core/Runtime/Delegates.cs`). No wrapping type, no range check, no
throw-on-i64. A host binding just uses the primitive:

```csharp
runtime.BindHostFunction<Func<long, long>>(("env", "double_i64"),
    x => x * 2);

var invoker = runtime.CreateInvokerFunc<long, long>(funcAddr);
long result = invoker(0x7FFF_FFFF_FFFF_FFFFL);
```

**Why ✳️.** The *proposal* is a JS-API concern — it exists to fix a JS
embedding gap. In .NET, `long` is native; there's nothing to "enable."
Modules built expecting BigInt-i64 integration Just Work because the
boundary is already 64-bit clean.

---

## JS String Builtins

[Proposal](https://github.com/WebAssembly/js-string-builtins) · [Phase 5 /
merged into WebAssembly 3.0](https://github.com/WebAssembly/proposals/blob/main/finished-proposals.md)

**In a JS host.** Wasm modules commonly need to read, compare, and build
UTF-16 strings owned by the JS host. Without this proposal, every
interaction requires copying bytes through wasm linear memory and
re-encoding on each boundary crossing. JS String Builtins defines a
recognized import namespace (`wasm:js-string`) of 13 functions — `length`,
`charCodeAt`, `substring`, `concat`, `equals`, `compare`, `fromCharCode`,
`fromCodePoint`, etc. — that the engine specializes into direct JS-string
operations, skipping the copy.

The entire proposal is defined observationally against *UTF-16 code units*:
length is the code-unit count, `charCodeAt` returns a code unit (not a
code point), `substring` is half-open on code-unit indices, and surrogate
pairs are preserved verbatim. Nothing in the spec constrains the
underlying representation — only the input/output behavior.

**In WACS.** `System.String` is also a UTF-16 code-unit sequence with
identical indexing and surrogate semantics, so the same 13 functions
backed by `System.String` are observationally indistinguishable from the
JS implementation. WACS wraps `System.String` in `JsStringRef` (an
`IGcRef`) and implements each builtin as an `IFunctionInstance` that
operates on the operand stack directly:

| # | Import | Signature | .NET backing |
|---|---|---|---|
| 1 | `test` | `(externref) → i32` | `obj is string` |
| 2 | `cast` | `(externref) → (ref extern)` | trap if not a string |
| 3 | `length` | `(externref) → i32` | `String.Length` |
| 4 | `concat` | `(externref, externref) → (ref extern)` | `String.Concat` |
| 5 | `substring` | `(externref, i32, i32) → (ref extern)` | clamped slice |
| 6 | `equals` | `(externref, externref) → i32` | `String.Equals(Ordinal)` |
| 7 | `compare` | `(externref, externref) → i32` | `String.CompareOrdinal`, normalized |
| 8 | `charCodeAt` | `(externref, i32) → i32` | `str[i]`, OOB → -1 |
| 9 | `codePointAt` | `(externref, i32) → i32` | `Char.ConvertToUtf32`, OOB → -1 |
| 10 | `fromCharCode` | `(i32) → (ref extern)` | `((char)cu).ToString()` |
| 11 | `fromCodePoint` | `(i32) → (ref extern)` | `Char.ConvertFromUtf32`, traps on > 0x10FFFF |
| 12 | `fromCharCodeArray` | `((ref null (array (mut i16))), i32, i32) → (ref extern)` | `StoreArray` → `string` |
| 13 | `intoCharCodeArray` | `(externref, (ref null (array (mut i16))), i32) → i32` | `string` → `StoreArray` |

**Host opt-in.** Register the namespace before instantiating modules that
import from it (same idiom as `Wasi.BindToRuntime`):

```csharp
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Builtins;

var runtime = new WasmRuntime();
JsStringBuiltins.BindTo(runtime);

// Now modules that (import "wasm:js-string" "length" (func …)) etc.
// instantiate and run unchanged.
var modInst = runtime.InstantiateModule(module);
```

Host code hands strings to wasm by wrapping them as an externref:

```csharp
var jsString = new Value(ValType.Extern, 0L, new JsStringRef("hello"));
```

Returned externrefs carry `JsStringRef` back out, which unwraps to the
underlying `System.String`.

**Why ✳️.** The *import contract* is JS-flavored — the namespace literally
reads `wasm:js-string` — but the observable behavior is UTF-16 all the
way down, which is C#'s native string representation too. Modules
compiled with `--enable-js-string-builtins` (Binaryen) or equivalent run
on WACS with identical semantics and without copying through memory.

---

## JS Promise Integration (JSPI)

[Proposal](https://github.com/WebAssembly/js-promise-integration) · Phase 4

**In a JS host.** Wasm is synchronous by default — a wasm function that
needs an async result (e.g., `fetch`) can't express "suspend and resume
when the promise settles." JSPI adds two JS-side wrappers,
`WebAssembly.Suspending` (marks a JS import as awaitable) and
`WebAssembly.promising` (marks a wasm export as returning a promise).
At runtime, hitting a Suspending import unwinds the wasm stack onto a
stored continuation; when the underlying JS promise resolves, the
continuation resumes with the result.

**In WACS.** The suspend / resume discipline maps directly onto .NET's
`Task` / `async` machinery. A host function is declared async at bind
time; the interpreter's execution loop awaits it and resumes on
completion:

```csharp
runtime.BindHostFunction<Func<ExecContext, int, Task<int>>>(
    ("env", "fetch_byte"),
    async (ctx, offset) =>
    {
        var b = await httpClient.GetByteAsync(offset);
        return b;
    },
    isAsync: true);

// Invoking a module function that (possibly transitively) calls
// fetch_byte suspends the wasm frame, awaits the Task, and resumes:
var invoker = runtime.CreateInvokerFuncAsync<int>(entryAddr);
int result = await invoker();
```

The plumbing is carried by `HostFunction.InvokeAsync`,
`IFunctionInstance.IsAsync`, and the `await`-preserving paths in
`Wacs.Core/Runtime/ExecContext.cs` + `WasmRuntimeExecution.cs` — any
host function returning `Task`/`ValueTask` will suspend every wasm frame
above it until it completes.

**Why ✳️.** The *JS API* (`WebAssembly.Suspending` /
`WebAssembly.promising`) is not wired because there's no JS host. The
*runtime capability* — unwind a wasm stack onto an awaitable, resume on
completion — is first-class in WACS using the .NET Task model. Modules
that use JSPI to bridge to async host I/O work out of the box; the only
migration is that a host binds `Func<…, Task<T>>` instead of a
`Suspending`-wrapped JS function.

---

## Summary

| Proposal | Browser primitive | WACS binding |
|---|---|---|
| JS BigInt ↔ i64 | `BigInt` | C# `long` |
| JS String Builtins | JS strings + `wasm:js-string` imports | `System.String` via `JsStringBuiltins.BindTo` |
| JSPI | `WebAssembly.Suspending` / `.promising` | `Task` / `async` on host functions |

Each row is "conceptually supported" rather than "fully supported" because
the JS-API-level surface area doesn't exist in a .NET runtime — but for
any module that targets the proposal's *wasm-level* semantics, WACS
provides an observably equivalent execution environment.
