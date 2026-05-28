// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Harness;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace WitHarnessSpike.Aot
{
    /// <summary>
    /// Hand-written typed harness for the wacs:harness-spike/hello
    /// component. Models what the future WIT-harness SourceGen
    /// would emit for this single-export world. Pure C# — no
    /// reflection, no generic specialization at runtime, no
    /// reachable [RequiresDynamicCode] / [RequiresUnreferencedCode]
    /// surface, so NativeAOT + Unity IL2CPP can transpile it cleanly.
    /// </summary>
    public sealed class HelloHarness
    {
        private readonly WasmRuntime _runtime;
        private readonly MemoryInstance _memory;

        // Direct exports of the main core module — the
        // canonical-ABI lowering of the component-level `greet`.
        //   cabi_realloc(old, oldlen, align, newlen) -> newptr
        //   greet(ptr, len) -> retArea (caller-owned 8 bytes
        //     holding the lifted string's ptr+len)
        //   cabi_post_greet(retArea) — frees what greet returned
        private readonly Func<int, int, int, int, int> _reallocInvoke;
        private readonly Func<int, int, int> _greetInvoke;
        private readonly Action<int> _postGreetInvoke;

        private HelloHarness(WasmRuntime runtime, MemoryInstance memory,
            Func<int, int, int, int, int> realloc,
            Func<int, int, int> greet,
            Action<int> postGreet)
        {
            _runtime = runtime;
            _memory = memory;
            _reallocInvoke = realloc;
            _greetInvoke = greet;
            _postGreetInvoke = postGreet;
        }

        /// <summary>
        /// Parse a wacs:harness-spike/hello-shaped component binary
        /// and wire it onto a fresh runtime. Throws on shape
        /// mismatch (missing exports, wrong import set).
        /// </summary>
        public static HelloHarness LoadFrom(byte[] componentBytes)
        {
            var loaded = HarnessLoader.Load(componentBytes, BindWasiStubs);
            var memory = HarnessLoader.RequireMemoryExport(loaded.Runtime, loaded.Module, "memory");
            var reallocAddr   = HarnessLoader.RequireFunctionExport(loaded.Module, "cabi_realloc");
            var greetAddr     = HarnessLoader.RequireFunctionExport(loaded.Module, "greet");
            var postGreetAddr = HarnessLoader.RequireFunctionExport(loaded.Module, "cabi_post_greet");

            // CreateInvokerFunc is generic over the wasm function's
            // param / return arity; all the generic instantiations
            // used here are statically rooted at this call site for
            // AOT.
            var realloc = loaded.Runtime.CreateInvokerFunc<int, int, int, int, int>(reallocAddr);
            var greet = loaded.Runtime.CreateInvokerFunc<int, int, int>(greetAddr);
            var postGreet = loaded.Runtime.CreateInvokerAction<int>(postGreetAddr);

            return new HelloHarness(loaded.Runtime, memory, realloc, greet, postGreet);
        }

        /// <summary>
        /// Typed wrapper for the WIT export
        /// <c>greet: func(name: string) -> string</c>.
        /// </summary>
        public string Greet(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            // Lower the input string via Harness.Runtime.
            StringCoding.LowerUtf8(_memory, name, _reallocInvoke,
                out int inPtr, out int inByteLen);

            // Call greet. Returns the address of an 8-byte
            // (ptr, len) tuple inside linear memory describing the
            // returned string's location + length.
            int retArea = _greetInvoke(inPtr, inByteLen);

            // Lift the return: read (ptr, len) via Harness.Runtime
            // canonical-ABI memory helpers, then decode UTF-8.
            int outPtr = MemoryHelpers.ReadI32LE(_memory, retArea);
            int outLen = MemoryHelpers.ReadI32LE(_memory, retArea + 4);
            string result = StringCoding.LiftUtf8(_memory, outPtr, outLen);

            // Free the returned string's memory.
            _postGreetInvoke(retArea);

            return result;
        }

        // WasmRuntime isn't IDisposable in the public surface;
        // GC + the runtime's own teardown handle cleanup. If the
        // harness ever needs explicit teardown (e.g., for unit-test
        // scope isolation), add an Unload() that drops references
        // explicitly.

        /// <summary>
        /// Bind a stub for every WASI host function the main core
        /// module imports. The spike's <c>greet</c> function is
        /// pure string formatting — it shouldn't actually call any
        /// of these. Throwing on call surfaces a real bug.
        ///
        /// <para>A production harness for a richer component
        /// would wire real WASI implementations here (the existing
        /// <c>WACS.WASI.Preview2</c> bundle, or per-subsystem
        /// stubs the embedder ships). For the spike, throw.</para>
        /// </summary>
        internal static void BindWasiStubs(WasmRuntime runtime)
        {
            // Type 3: (i32) -> ()
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException(
                    "Hello harness does not implement WASI runtime.");

            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/error@0.2.0", "[resource-drop]error"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[resource-drop]pollable"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]input-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]output-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-input@0.2.0", "[resource-drop]terminal-input"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-output@0.2.0", "[resource-drop]terminal-output"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/environment@0.2.0", "get-environment"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/exit@0.2.0", "exit"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[method]pollable.block"), drop);

            // Type 2: (i32, i32) -> () — though some are (i32) -> i32 actually; cross-check.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.check-write"),
                (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.blocking-flush"),
                (_, _, _) => throw new NotSupportedException("stub"));

            // Type 5: (i32, i32, i32, i32) -> ()
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.write"),
                (_, _, _, _, _) => throw new NotSupportedException("stub"));

            // Type 6: (i32) -> i32
            runtime.BindHostFunction<Func<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.subscribe"),
                (_, _) => throw new NotSupportedException("stub"));

            // Type 7: () -> i32 — get-stdin / get-stdout / get-stderr
            Func<ExecContext, int> getHandle = _ =>
                throw new NotSupportedException("Hello harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);

            // Type 3 — get-terminal-stdin/out/err take an out-pointer
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
