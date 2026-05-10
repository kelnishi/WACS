// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.Nn;
using Wacs.WASI.Preview2.DependencyInjection;
using Xunit;

namespace Wacs.WASI.NN.OnnxRuntime.Test
{
    // Regression test for wasi-nn/WACS-GAPS.md gap 20.
    //
    // Symptom (round 13): SLM guests with `--wasip2 --wasi-nn`
    // returned `InvalidEncoding: No backend registered for
    // encoding ONNX` from `graph.load`. Two layers of bug:
    //   1) the post-hoc AutoRegisterOnnxBackend mutation was
    //      hitting a different WasiNNConfiguration instance
    //      from the singleton GraphFuncsImpl resolved
    //   2) the reflective type lookup
    //      `nnAsm.GetType("Wacs.WASI.NN.Types.GraphEncoding")`
    //      was reading from the wrong assembly (the type is
    //      in Wacs.WASI.NN, not Wacs.WASI.NN.DependencyInjection)
    //      and silently returning null — so the configure
    //      delegate was never built and AddWasiNN ran with
    //      a null callback.
    //
    // Fix: ReflectivelyAddWasiNN hands AddWasiNN a pre-built
    // configure callback that runs BEFORE
    // TryAddSingleton(opts.Configuration). The encoding /
    // backend types are derived from AddBackend's parameter
    // signature instead of by name, so a mismatched assembly
    // boundary stops being a silent failure.
    //
    // This test exercises the full DI path end-to-end: it
    // builds the scope, reaches IGraphFuncs through the
    // composite bundle, and confirms a `graph.load` for
    // GraphEncoding.Onnx does NOT short-circuit with
    // InvalidEncoding. (The actual ORT load fails on empty
    // model bytes with RuntimeError / InvalidArgument; that's
    // expected and orthogonal — what matters is that the
    // encoding lookup hit.)
    public class WasiPreview2RuntimeScopeTests
    {
        [Fact]
        public void OnnxBackendRegisteredInBundleConfiguration()
        {
            // Capture the diagnostic stderr the runtime scope writes
            // when OnnxBackend wiring fails — the gap-20 fix surfaces
            // root-cause as a stderr warning so a test failure can
            // see it.
            var stderr = new System.IO.StringWriter();
            var origErr = System.Console.Error;
            System.Console.SetError(stderr);
            try
            {
                var runtime = new WasmRuntime();
                using var scope = new WasiPreview2RuntimeScope(runtime);

                var bundle = Assert.IsType<WasiPreview2NNBundle>(scope.Bundle);
                var graphFuncs = bundle.GraphFuncs;
                Assert.NotNull(graphFuncs);

                var result = graphFuncs.Load(
                    new byte[][] { new byte[0] },
                    GraphEncoding.Onnx,
                    ExecutionTarget.Cpu);

                // The lookup must not fail with InvalidEncoding —
                // that's the gap-20 symptom. Any other outcome
                // (Ok, or Err with a different code) means the
                // encoding-to-backend dictionary saw the
                // OnnxBackend registration.
                if (result.IsErr)
                {
                    var code = result.Err.Code();
                    Assert.True(
                        code != ErrorCode.InvalidEncoding,
                        "Load returned InvalidEncoding (gap-20 symptom). "
                        + "Captured stderr from WasiPreview2RuntimeScope: "
                        + stderr.ToString());
                }
            }
            finally
            {
                System.Console.SetError(origErr);
            }
        }
    }
}
