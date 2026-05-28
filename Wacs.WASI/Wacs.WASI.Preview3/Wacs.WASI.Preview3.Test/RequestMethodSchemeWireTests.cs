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
    /// Phase 5 Slice CC coverage: request.method and
    /// request.scheme getter/setter pairs. method = bare
    /// variant getter (12-byte retptr) + 3-slot setter arg.
    /// scheme = option&lt;scheme&gt; getter (16-byte retptr) +
    /// 4-slot setter arg (option-disc + variant 3 slots).
    /// </summary>
    public class RequestMethodSchemeWireTests
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
            var request = new Request(new Fields());
            int handle = host.RequestHandles.Allocate(request);
            return (host, request, handle, dispatcher);
        }

        [Fact]
        public void BindToRuntime_registers_method_and_scheme_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-method"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.set-method"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-scheme"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.set-scheme"), out _));
        }

        // ---- method ----------------------------------------------

        [Fact]
        public void GetMethod_default_writes_get_disc()
        {
            var (host, _, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeRequestGetMethod(handle, retptr, realloc);

            // disc=0 (Get) at +0; payload at +4..12 unused (zero).
            Assert.Equal(0, disp.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 4, 4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4)));
        }

        [Fact]
        public void SetMethod_post_then_get_round_trips()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            int disc = host.InvokeRequestSetMethod(
                handle, disc: 2 /* Post */, strPtr: 0, strLen: 0);
            Assert.Equal(0, disc);
            Assert.Equal(HttpMethod.Tag.Post, req.GetMethod().Kind);

            const int retptr = 64;
            host.InvokeRequestGetMethod(handle, retptr, realloc);
            Assert.Equal(2, disp.Memory.AsSpan(retptr, 1)[0]);
        }

        [Fact]
        public void SetMethod_other_round_trips_custom_verb()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int strPtr = 32;
            byte[] verb = Encoding.UTF8.GetBytes("BREW");
            verb.CopyTo(disp.Memory.AsSpan(strPtr, verb.Length));

            int rc = host.InvokeRequestSetMethod(
                handle, disc: 9 /* Other */, strPtr: strPtr,
                strLen: verb.Length);
            Assert.Equal(0, rc);
            Assert.Equal(HttpMethod.Tag.Other, req.GetMethod().Kind);
            Assert.Equal("BREW", req.GetMethod().OtherName);

            const int retptr = 128;
            host.InvokeRequestGetMethod(handle, retptr, realloc);
            Assert.Equal(9, disp.Memory.AsSpan(retptr, 1)[0]);
            int outPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 4, 4));
            int outLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4));
            byte[] buf = new byte[outLen];
            disp.Memory.AsSpan(outPtr, outLen).CopyTo(buf);
            Assert.Equal("BREW", Encoding.UTF8.GetString(buf));
        }

        [Fact]
        public void ReadMethodFromSlots_invalid_disc_throws()
        {
            var (host, _, _, _) = MakeRequestHost();
            var ex = Assert.Throws<HttpException>(
                () => host.ReadMethodFromSlots(99, 0, 0));
            Assert.Equal(HttpErrorCode.HttpRequestMethodInvalid,
                ex.Code);
        }

        // ---- scheme ----------------------------------------------

        [Fact]
        public void GetScheme_default_writes_none_disc()
        {
            var (host, _, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeRequestGetScheme(handle, retptr, realloc);

            // Outer option-disc=0 (none) at +0; +4..16 zero.
            Assert.Equal(0, disp.Memory.AsSpan(retptr, 1)[0]);
        }

        [Fact]
        public void SetScheme_https_then_get_round_trips()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            int rc = host.InvokeRequestSetScheme(
                handle, optDisc: 1, schemeDisc: 1 /* HTTPS */,
                strPtr: 0, strLen: 0);
            Assert.Equal(0, rc);
            Assert.NotNull(req.GetScheme());
            Assert.Equal(HttpScheme.Tag.Https, req.GetScheme()!.Value.Kind);

            const int retptr = 64;
            host.InvokeRequestGetScheme(handle, retptr, realloc);
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]); // some
            Assert.Equal(1, disp.Memory.AsSpan(retptr + 4, 1)[0]); // HTTPS
        }

        [Fact]
        public void SetScheme_other_round_trips_custom_protocol()
        {
            var (host, req, handle, disp) = MakeRequestHost();
            var realloc = new SequentialRealloc(1024);

            const int strPtr = 32;
            byte[] proto = Encoding.UTF8.GetBytes("ws");
            proto.CopyTo(disp.Memory.AsSpan(strPtr, proto.Length));

            int rc = host.InvokeRequestSetScheme(
                handle, optDisc: 1, schemeDisc: 2 /* Other */,
                strPtr: strPtr, strLen: proto.Length);
            Assert.Equal(0, rc);
            Assert.Equal("ws", req.GetScheme()!.Value.OtherName);

            const int retptr = 128;
            host.InvokeRequestGetScheme(handle, retptr, realloc);
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]); // some
            Assert.Equal(2, disp.Memory.AsSpan(retptr + 4, 1)[0]); // Other
            int outPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 8, 4));
            int outLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 12, 4));
            byte[] buf = new byte[outLen];
            disp.Memory.AsSpan(outPtr, outLen).CopyTo(buf);
            Assert.Equal("ws", Encoding.UTF8.GetString(buf));
        }

        [Fact]
        public void SetScheme_none_clears_value()
        {
            var (host, req, handle, _) = MakeRequestHost();
            req.SetScheme(HttpScheme.Http);

            int rc = host.InvokeRequestSetScheme(
                handle, optDisc: 0, schemeDisc: 0,
                strPtr: 0, strLen: 0);
            Assert.Equal(0, rc);
            Assert.Null(req.GetScheme());
        }

        [Fact]
        public void ReadSchemeFromSlots_invalid_disc_throws()
        {
            var (host, _, _, _) = MakeRequestHost();
            var ex = Assert.Throws<HttpException>(
                () => host.ReadSchemeFromSlots(99, 0, 0));
            Assert.Equal(HttpErrorCode.InternalError, ex.Code);
        }
    }
}
