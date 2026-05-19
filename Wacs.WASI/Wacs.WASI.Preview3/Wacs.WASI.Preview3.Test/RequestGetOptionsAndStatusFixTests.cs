// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Buffers.Binary;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice EE coverage: request.get-options
    /// (option&lt;own&lt;request-options&gt;&gt;) plus a
    /// confirmation that response.set-status-code now has the
    /// correct wire arity (returns result-disc as a flat i32).
    /// </summary>
    public class RequestGetOptionsAndStatusFixTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        [Fact]
        public void BindToRuntime_registers_get_options()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-options"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]response.set-status-code"), out _));
        }

        // ---- request.get-options ---------------------------------

        [Fact]
        public void GetOptions_none_writes_option_disc_zero_and_zero_handle()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var request = new Request(new Fields());
            int handle = host.RequestHandles.Allocate(request);

            const int retptr = 64;
            host.InvokeRequestGetOptions(handle, retptr);

            // option-disc = 0 (none) at +0; handle at +4 stays 0.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4)));
        }

        [Fact]
        public void GetOptions_some_allocates_request_options_handle()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var opts = new RequestOptions();
            opts.SetConnectTimeout(5_000_000_000UL);
            var request = new Request(
                new Fields(), body: null, trailers: null,
                options: opts);
            int handle = host.RequestHandles.Allocate(request);

            const int retptr = 64;
            host.InvokeRequestGetOptions(handle, retptr);

            // option-disc = 1 (some); handle at +4 points to opts.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int optsHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.NotEqual(0, optsHandle);

            // Resolves to the same impl instance — the wire
            // shares ownership of the impl with the parent
            // request (drop semantics on the returned handle
            // are independent of the request's lifetime).
            var resolved = host.RequestOptionsHandles.Get(optsHandle);
            Assert.Same(opts, resolved);
        }

        [Fact]
        public void GetOptions_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var request = new Request(new Fields());
            int handle = host.RequestHandles.Allocate(request);

            var ex = Assert.Throws<System.InvalidOperationException>(
                () => host.InvokeRequestGetOptions(
                    handle, /* not 4-aligned */ 13));
            Assert.Contains("misaligned", ex.Message);
        }

        // ---- response.set-status-code wire arity ------------------

        [Fact]
        public void SetStatusCode_wire_signature_matches_result_return()
        {
            // The Slice EE fix changes the binding from
            //   Action<ExecContext, int, int>
            // to
            //   Func<ExecContext, int, int, int>  (returns result disc)
            //
            // Verify the FunctionType the runtime registers has
            // exactly 1 result slot (the result discriminant).
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[method]response.set-status-code"),
                out var addr));
            var funcType = runtime.GetFunctionType(addr);
            Assert.Equal(1, funcType.ResultType.Types.Length);
            // status-code is u16 widened to i32 + self handle =
            // 2 i32 param slots.
            Assert.Equal(2, funcType.ParameterTypes.Types.Length);
        }
    }
}
