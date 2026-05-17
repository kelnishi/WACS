// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Text;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
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
            if (componentBytes == null)
                throw new ArgumentNullException(nameof(componentBytes));

            // 1. Parse the .component.wasm wrapper. AOT-safe —
            //    pure byte walk, no reflection.
            Wacs.ComponentModel.Runtime.ComponentModule component;
            using (var ms = new MemoryStream(componentBytes))
                component = ComponentBinaryParser.Parse(ms);

            // 2. Extract the main core module (the first one — for
            //    a single-greet world there's exactly one). A
            //    real harness would walk the component's
            //    `core instance` declarations to pick the right
            //    one; the spike hardcodes "first".
            var coreModuleBytes = component.CoreModuleBinaries.First();

            // 3. Parse + instantiate that core module via
            //    Wacs.Core. AOT-safe — typed runtime.
            var runtime = new WasmRuntime();
            BindWasiStubs(runtime);
            Module coreModule;
            using (var coreMs = new MemoryStream(coreModuleBytes))
                coreModule = BinaryModuleParser.ParseWasm(coreMs);
            var moduleInst = runtime.InstantiateModule(coreModule);
            runtime.RegisterModule("hello", moduleInst);

            // 4. Look up the four exports we need. Memory + three
            //    canonical-ABI functions.
            MemoryInstance? memory = null;
            FuncAddr? reallocAddr = null;
            FuncAddr? greetAddr = null;
            FuncAddr? postGreetAddr = null;
            foreach (var export in moduleInst.Exports)
            {
                switch (export.Name)
                {
                    case "memory":
                        if (export.Value is ExternalValue.Memory mem)
                            memory = runtime.RuntimeStore[mem.Address];
                        break;
                    case "cabi_realloc":
                        if (export.Value is ExternalValue.Function r)
                            reallocAddr = r.Address;
                        break;
                    case "greet":
                        if (export.Value is ExternalValue.Function g)
                            greetAddr = g.Address;
                        break;
                    case "cabi_post_greet":
                        if (export.Value is ExternalValue.Function p)
                            postGreetAddr = p.Address;
                        break;
                }
            }

            if (memory is null)
                throw new InvalidDataException("Component's main core module exports no 'memory'.");
            if (reallocAddr is null)
                throw new InvalidDataException("Component's main core module exports no 'cabi_realloc'.");
            if (greetAddr is null)
                throw new InvalidDataException("Component's main core module exports no 'greet'.");
            if (postGreetAddr is null)
                throw new InvalidDataException("Component's main core module exports no 'cabi_post_greet'.");

            // 5. Cache typed invoker delegates. CreateInvokerFunc
            //    is a generic over the wasm function's parameter
            //    + return arity; all the generic instantiations
            //    used here are statically rooted by these call
            //    sites at AOT-compile time.
            var realloc = runtime.CreateInvokerFunc<int, int, int, int, int>(
                reallocAddr.Value);
            var greet = runtime.CreateInvokerFunc<int, int, int>(
                greetAddr.Value);
            var postGreet = runtime.CreateInvokerAction<int>(
                postGreetAddr.Value);

            return new HelloHarness(runtime, memory, realloc, greet, postGreet);
        }

        /// <summary>
        /// Typed wrapper for the WIT export
        /// <c>greet: func(name: string) -> string</c>.
        /// </summary>
        public string Greet(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            // Lower the input string: allocate space in linear
            // memory via cabi_realloc, copy UTF-8 bytes in.
            var utf8 = Encoding.UTF8.GetBytes(name);
            int inPtr = _reallocInvoke(
                /*old=*/ 0, /*oldLen=*/ 0,
                /*align=*/ 1, /*newLen=*/ utf8.Length);
            if (inPtr == 0)
                throw new OutOfMemoryException("cabi_realloc returned 0 for the input string.");

            // Direct write into the wasm linear memory's backing
            // byte[]. MemoryInstance.Data is byte[]; AOT-clean.
            utf8.CopyTo(_memory.Data, inPtr);

            // Call greet. Returns the address of an 8-byte
            // (ptr, len) tuple inside linear memory describing the
            // returned string's location + length.
            int retArea = _greetInvoke(inPtr, utf8.Length);

            // Lift the return: read 8 bytes from retArea (two
            // little-endian i32: ptr then len).
            int outPtr = ReadI32LE(_memory, retArea);
            int outLen = ReadI32LE(_memory, retArea + 4);
            string result = Encoding.UTF8.GetString(
                _memory.Data, outPtr, outLen);

            // Free the returned string's memory.
            _postGreetInvoke(retArea);

            return result;
        }

        // WasmRuntime isn't IDisposable in the public surface;
        // GC + the runtime's own teardown handle cleanup. If the
        // harness ever needs explicit teardown (e.g., for unit-test
        // scope isolation), add an Unload() that drops references
        // explicitly.

        private static int ReadI32LE(MemoryInstance mem, int offset)
        {
            var data = mem.Data;
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

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
        private static void BindWasiStubs(WasmRuntime runtime)
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
