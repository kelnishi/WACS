// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Wacs.Core.Runtime;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.Types;
using Wacs.WASI.Preview2.DependencyInjection;
using Xunit;

namespace Wacs.WASI.NN.LlamaSharp.Test
{
    // Round-20 (gap 24) regression tests: WasiPreview2RuntimeScope
    // must auto-wire the LlamaSharp backend into BOTH
    // Backends[GGML] and LoadByNameBackend on the DI bundle's
    // WasiNNConfiguration when Wacs.WASI.NN.LlamaSharp is on the
    // load path. Symmetric to the round-19 OnnxRuntime test in
    // Wacs.WASI.NN.OnnxRuntime.Test.
    //
    // These tests don't load any GGUF — the auto-wire's registry
    // builder is fail-soft so an unset/missing
    // $WACS_WASINN_GGUF_DIR yields an empty registry and the
    // backend wires through anyway. What we verify is that the
    // CONFIGURATION carries the backend, not that the backend
    // can actually load a model.
    public class WasiPreview2RuntimeScopeLlamaSharpTests
    {
        // Round-21 / gap-25 regression: TryLoadAssembly must
        // resolve Wacs.WASI.NN.LlamaSharp regardless of which load
        // context placed it in AppDomain. Pre-fix used
        // Assembly.Load(name) only — missed --bind <path>
        // assemblies in LoadFromContext. Post-fix walks
        // AppDomain.CurrentDomain.GetAssemblies() on miss.
        //
        // The pure LoadFromContext path is hard to set up in xunit
        // (any project-referenced assembly lands in the default
        // context, so Assembly.Load(name) succeeds before the
        // fallback fires). What this test verifies:
        //   1. TryLoadAssembly returns the right Assembly object
        //      for a name that's loadable. Smoke check that the
        //      OR-of-two-paths logic doesn't break the happy path.
        //   2. TryLoadAssembly returns null for a clearly-bogus
        //      name. Confirms the AppDomain walk's
        //      `IsDynamic`/malformed-metadata guards don't crash.
        // The full LoadFromContext case is verified empirically in
        // wasi-nn/WACS-GAPS.md round 21 (the CLI's --bind <path>
        // flow lands LlamaSharp in LoadFromContext; the fallback
        // closes the auto-wire path).
        [Fact]
        public void TryLoadAssembly_ResolvesKnownAndReturnsNullForBogus()
        {
            var tryLoad = typeof(WasiPreview2RuntimeScope).GetMethod(
                "TryLoadAssembly",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(tryLoad);

            // Happy path — a referenced assembly resolves.
            var llama = (Assembly?)tryLoad!.Invoke(null,
                new object?[] { "Wacs.WASI.NN.LlamaSharp" });
            Assert.NotNull(llama);
            Assert.Equal("Wacs.WASI.NN.LlamaSharp",
                llama!.GetName().Name);

            // Negative path — clearly-bogus name returns null
            // (caller treats this as "not loadable, skip").
            var miss = (Assembly?)tryLoad.Invoke(null,
                new object?[] { "NonexistentAssemblyName_qZ9_" });
            Assert.Null(miss);
        }

        [Fact]
        public void LoadByNameBackend_AutoWiredFromLlamaSharpAssembly()
        {
            // Capture stderr so a future regression's diagnostic
            // warning shows up in the failure message.
            var stderr = new StringWriter();
            var origErr = Console.Error;
            Console.SetError(stderr);
            try
            {
                var runtime = new WasmRuntime();
                using var scope = new WasiPreview2RuntimeScope(runtime);

                var bundle = Assert.IsType<WasiPreview2NNBundle>(scope.Bundle);

                // Reach the WasiNNConfiguration through the bundle's
                // resolved IGraphFuncs. The DI bundle's GraphFuncsImpl
                // takes the configuration as ctor arg; we don't have a
                // public accessor for the inner config, so we resolve
                // it via the configured service provider's surface.
                // Simpler probe: call IGraphFuncs.LoadByName with a
                // bogus name and assert the error code is *not* the
                // pre-fix gap 24 symptom ("no named-model resolver").
                // If LoadByNameBackend is wired, LlamaSharp's
                // LoadGraphByName fires and reports its own error
                // (NotFound for unknown names) — different shape.
                var graphFuncs = bundle.GraphFuncs;
                Assert.NotNull(graphFuncs);

                var result = graphFuncs.LoadByName("nonexistent-model");

                // Regardless of whether $WACS_WASINN_GGUF_DIR is
                // set in the test environment, "nonexistent-model"
                // shouldn't be in the registry. We assert the
                // error is NOT the pre-fix gap 24 symptom — i.e.
                // the message must NOT mention "no named-model
                // resolver" (that's the byte-flow's error from
                // before LoadByNameBackend was checked).
                Assert.True(result.IsErr);
                var errData = result.Err.Data();
                Assert.DoesNotContain("no named-model resolver", errData,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Console.SetError(origErr);
            }
        }
    }
}
