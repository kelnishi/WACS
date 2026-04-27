// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>
    /// Orchestrator for <c>wasi:http/types@0.2.3</c> — the
    /// 11 HTTP resources (Fields, OutgoingRequest,
    /// IncomingRequest, OutgoingResponse, IncomingResponse,
    /// OutgoingBody, IncomingBody, FutureIncomingResponse,
    /// FutureTrailers, RequestOptions, ResponseOutparam) plus
    /// the top-level <c>http-error-code</c> function. All
    /// resource methods bind unconditionally — guests typically
    /// receive resource handles from constructors elsewhere
    /// and the resource methods need to be reachable regardless.
    ///
    /// <para>The optional <see cref="IHttpErrorCodeMapper"/>
    /// hook drives the top-level <c>http-error-code</c> function
    /// (io-error → option&lt;error-code&gt;). Pass null to skip
    /// that single binding while still wiring all the resource
    /// methods.</para>
    /// </summary>
    public sealed partial class HttpTypes : IBindable
    {
        private const string Ns = "wasi:http/types@0.2.3";

        private readonly ResourceContext _resources;
        private readonly IHttpErrorCodeMapper? _errorCodeMapper;

        public HttpTypes(ResourceContext resources,
            IHttpErrorCodeMapper? errorCodeMapper = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _errorCodeMapper = errorCodeMapper;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            BindFields(runtime, _resources, alloc);
            BindOutgoingRequest(runtime, _resources, alloc);
            BindIncomingRequest(runtime, _resources, alloc);
            BindOutgoingResponse(runtime, _resources);
            BindIncomingResponse(runtime, _resources);
            BindOutgoingBody(runtime, _resources);
            BindIncomingBody(runtime, _resources);
            BindFutureIncomingResponse(runtime, _resources, alloc);
            BindFutureTrailers(runtime, _resources, alloc);
            BindRequestOptions(runtime, _resources);
            BindResponseOutparam(runtime, _resources);
            if (_errorCodeMapper != null)
                BindHttpErrorCode(runtime, _resources, alloc,
                    _errorCodeMapper);
        }

        // -----------------------------------------------------
        //   shared retArea encoders
        // -----------------------------------------------------
        // Most wasi:http/types result-returning methods use
        // the simplified-error retArea (just the outer Ok disc
        // — Err side never written in v0). Spec-layout
        // (40 bytes, align 8) is reserved for the
        // outgoing-handler's [WasiSpecErrorCode] path which
        // ships in a follow-up commit.

        // result<_, error-code>: 1 byte (just Ok disc).
        private static void WriteOkUnit(byte[] mem, int retArea)
        {
            mem[retArea] = 0;
        }

        // result<u16, error-code>: 4 bytes (disc + 1 pad + u16).
        // Used by outgoing-response.set-status-code where the
        // status-code is u16 wrapped in result<_, ...>.
        // Actually set-status-code returns result<_, _> so this
        // is unused for that — kept for symmetry.

        // result<own<X>, error-code>: 8 bytes (disc + 3 pad + handle).
        private static void WriteOkHandle(byte[] mem, int retArea, int handle)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteI32LE(mem, retArea + 4, handle);
        }

        // option<own<X>>: 8 bytes (disc + 3 pad + handle). Same
        // wire form as result<own<X>, error-code> — the canon
        // lift discriminates by context, not by bytes. null →
        // disc=0; non-null → disc=1 + handle.
        private static void WriteOptionHandle(byte[] mem, int retArea,
            int handle, bool present)
        {
            mem[retArea] = present ? (byte)1 : (byte)0;
            if (!present) return;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteI32LE(mem, retArea + 4, handle);
        }

        // option<u64>: 16 bytes — disc (1B) + 7B pad + u64 (8B).
        // Used by request-options.connect-timeout / first-byte-
        // timeout / between-bytes-timeout (option<duration> in
        // the WIT, where duration is u64 nanoseconds).
        private static void WriteOptionU64(byte[] mem, int retArea,
            ulong? value)
        {
            if (value == null)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, value.Value);
        }

        // option<string>: 12 bytes — disc + 3 pad + ptr@+4 + len@+8.
        // The canon-lower layout the binder wrote in the legacy
        // path. Refetches memory after alloc.
        private static void WriteOptionString(ExecContext ctx,
            int retArea, string? value, Realloc alloc)
        {
            var mem = ctx.Memory();
            if (value == null)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                ctx.Memory, value, alloc);
            mem = ctx.Memory();
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, len);
        }

        // -----------------------------------------------------
        //   variant Method / Scheme — wire shape in/out
        // -----------------------------------------------------

        // variant method (10 cases): wire in flat form is
        // 3 slots — disc(i32) + ptr(i32) + len(i32). Disc 0–8
        // are no-payload (GET..PATCH); disc 9 is other(string).
        private static HttpMethod DecodeHttpMethodFlat(
            byte[] memory, int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new HttpMethodGet();
                case 1: return new HttpMethodHead();
                case 2: return new HttpMethodPost();
                case 3: return new HttpMethodPut();
                case 4: return new HttpMethodDelete();
                case 5: return new HttpMethodConnect();
                case 6: return new HttpMethodOptions();
                case 7: return new HttpMethodTrace();
                case 8: return new HttpMethodPatch();
                case 9:
                    var name = Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new HttpMethodOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown HttpMethod variant disc: " + disc);
            }
        }

        // variant scheme: same flat-3-slot pattern. Disc 0/1
        // are no-payload (HTTP/HTTPS); disc 2 is other(string).
        private static HttpScheme DecodeHttpSchemeFlat(
            byte[] memory, int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new HttpSchemeHttp();
                case 1: return new HttpSchemeHttps();
                case 2:
                    var name = Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new HttpSchemeOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown HttpScheme variant disc: " + disc);
            }
        }

        // Map an HttpMethod instance to (disc, payloadString).
        // Named cases return (disc, null); other(string) returns
        // (9, name).
        private static (byte disc, string? payload) MapHttpMethod(
            HttpMethod method)
        {
            return method switch
            {
                HttpMethodGet     => ((byte)0, (string?)null),
                HttpMethodHead    => ((byte)1, (string?)null),
                HttpMethodPost    => ((byte)2, (string?)null),
                HttpMethodPut     => ((byte)3, (string?)null),
                HttpMethodDelete  => ((byte)4, (string?)null),
                HttpMethodConnect => ((byte)5, (string?)null),
                HttpMethodOptions => ((byte)6, (string?)null),
                HttpMethodTrace   => ((byte)7, (string?)null),
                HttpMethodPatch   => ((byte)8, (string?)null),
                HttpMethodOther o => ((byte)9, (string?)o.Name),
                _ => throw new ArgumentException(
                    "Unknown HttpMethod subclass: " + method.GetType()),
            };
        }

        private static (byte disc, string? payload) MapHttpScheme(
            HttpScheme scheme)
        {
            return scheme switch
            {
                HttpSchemeHttp    => ((byte)0, (string?)null),
                HttpSchemeHttps   => ((byte)1, (string?)null),
                HttpSchemeOther o => ((byte)2, (string?)o.Name),
                _ => throw new ArgumentException(
                    "Unknown HttpScheme subclass: " + scheme.GetType()),
            };
        }

        // Write the variant Method retArea: 12 bytes — 1B disc +
        // 3B pad + 4B ptr + 4B len. Named cases zero the
        // trailing slots; "other" allocates + writes the string.
        private static void WriteHttpMethod(ExecContext ctx,
            int retArea, HttpMethod method, Realloc alloc)
        {
            var (disc, payload) = MapHttpMethod(method);
            var mem = ctx.Memory();
            mem[retArea] = disc;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            if (payload == null)
            {
                MemoryWriter.WriteI32LE(mem, retArea + 4, 0);
                MemoryWriter.WriteI32LE(mem, retArea + 8, 0);
                return;
            }
            var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                ctx.Memory, payload, alloc);
            mem = ctx.Memory();
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, len);
        }

        // option<scheme> retArea: 16 bytes — option disc (1B) +
        // 3B pad + variant disc (1B) + 3B pad + ptr@+8 + len@+12.
        private static void WriteOptionHttpScheme(ExecContext ctx,
            int retArea, HttpScheme? scheme, Realloc alloc)
        {
            var mem = ctx.Memory();
            if (scheme == null)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            var (disc, payload) = MapHttpScheme(scheme);
            mem[retArea + 4] = disc;
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            if (payload == null)
            {
                MemoryWriter.WriteI32LE(mem, retArea + 8, 0);
                MemoryWriter.WriteI32LE(mem, retArea + 12, 0);
                return;
            }
            var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                ctx.Memory, payload, alloc);
            mem = ctx.Memory();
            MemoryWriter.WriteI32LE(mem, retArea + 8, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 12, len);
        }
    }
}
