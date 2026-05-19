// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice BB coverage: request.path-with-query and
    /// request.authority getter/setter pairs. Both share the
    /// option&lt;string&gt; wire shape — getters via a 12-byte
    /// retptr (disc + ptr + len), setters take the 3-slot
    /// variant arg and return the result discriminant as an
    /// i32 flat return.
    /// </summary>
    public class RequestOptionStringWireTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class SequentialRealloc : ICabiRealloc
        {
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                _next += size;
                return ptr;
            }
        }

        private static (WasiPreview3Host host, IRequest req,
            int handle, AsyncDispatcher dispatcher)
            MakeRequestHost()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            var request = new Request(fields);
            int handle = host.RequestHandles.Allocate(request);
            return (host, request, handle, dispatcher);
        }

        [Fact]
        public void BindToRuntime_registers_option_string_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-path-with-query"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.set-path-with-query"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-authority"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.set-authority"), out _));
        }

        // ---- path-with-query --------------------------------------

        [Fact]
        public void GetPathWithQuery_none_writes_zero_disc_and_zero_pair()
        {
            var (host, _, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeRequestGetOptionString(
                handle, retptr, realloc, r => r.GetPathWithQuery());

            Assert.Equal(0, disp.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 4, 4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4)));
        }

        [Fact]
        public void SetPathWithQuery_then_get_round_trips_value()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int valPtr = 32;
            byte[] val = Encoding.UTF8.GetBytes("/api/v1?q=1");
            val.CopyTo(disp.Memory.AsSpan(valPtr, val.Length));

            int disc = host.InvokeRequestSetOptionString(
                handle, optDisc: 1,
                strPtr: valPtr, strLen: val.Length,
                (r, v) => r.SetPathWithQuery(v));
            Assert.Equal(0, disc); // result = ok

            // Impl-side mutation visible.
            Assert.Equal("/api/v1?q=1", req.GetPathWithQuery());

            // Wire getter mirrors it.
            const int retptr = 128;
            host.InvokeRequestGetOptionString(
                handle, retptr, realloc, r => r.GetPathWithQuery());
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]);
            int rPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 4, 4));
            int rLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4));
            byte[] buf = new byte[rLen];
            disp.Memory.AsSpan(rPtr, rLen).CopyTo(buf);
            Assert.Equal("/api/v1?q=1", Encoding.UTF8.GetString(buf));
        }

        [Fact]
        public void SetPathWithQuery_none_clears_value()
        {
            var (host, req, handle, _) = MakeRequestHost();
            req.SetPathWithQuery("/initial");

            int disc = host.InvokeRequestSetOptionString(
                handle, optDisc: 0, strPtr: 0, strLen: 0,
                (r, v) => r.SetPathWithQuery(v));
            Assert.Equal(0, disc);
            Assert.Null(req.GetPathWithQuery());
        }

        // ---- authority --------------------------------------------

        [Fact]
        public void SetAuthority_then_get_round_trips_value()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int valPtr = 32;
            byte[] val = Encoding.UTF8.GetBytes("example.com:8080");
            val.CopyTo(disp.Memory.AsSpan(valPtr, val.Length));

            int disc = host.InvokeRequestSetOptionString(
                handle, optDisc: 1,
                strPtr: valPtr, strLen: val.Length,
                (r, v) => r.SetAuthority(v));
            Assert.Equal(0, disc);
            Assert.Equal("example.com:8080", req.GetAuthority());

            const int retptr = 128;
            host.InvokeRequestGetOptionString(
                handle, retptr, realloc, r => r.GetAuthority());
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]);
            int rPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 4, 4));
            int rLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4));
            byte[] buf = new byte[rLen];
            disp.Memory.AsSpan(rPtr, rLen).CopyTo(buf);
            Assert.Equal("example.com:8080", Encoding.UTF8.GetString(buf));
        }

        [Fact]
        public void SetAuthority_empty_string_round_trips_as_some_empty()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            int disc = host.InvokeRequestSetOptionString(
                handle, optDisc: 1, strPtr: 0, strLen: 0,
                (r, v) => r.SetAuthority(v));
            Assert.Equal(0, disc);
            // some(empty-string) is distinct from none.
            Assert.Equal(string.Empty, req.GetAuthority());

            const int retptr = 64;
            host.InvokeRequestGetOptionString(
                handle, retptr, realloc, r => r.GetAuthority());
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4)));
        }

        [Fact]
        public void GetOptionString_misaligned_retptr_throws()
        {
            var (host, _, handle, _) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeRequestGetOptionString(
                    handle, /* not 4-aligned */ 13, realloc,
                    r => r.GetAuthority()));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
