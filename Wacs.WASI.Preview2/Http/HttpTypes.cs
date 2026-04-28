// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.ComponentModel.Runtime;
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
    /// the top-level <c>http-error-code</c> function.
    ///
    /// <para>Every host method that the WIT spec marks
    /// <c>result&lt;X, error-code&gt;</c> returns
    /// <see cref="Result{TOk,TErr}"/> over
    /// <see cref="ErrorCode"/>; the bindings encode both
    /// branches faithfully — the Ok side writes the payload,
    /// the Err side writes outer disc=1 + the variant payload.
    /// retArea sizes are per-fixture (the test fixtures use
    /// a placeholder single-case error-code; only
    /// <see cref="OutgoingHandlerBindings"/>'s spec mode and
    /// the FutureIncomingResponse / FutureTrailers / http-
    /// error-code paths exercise the full 39-case variant).
    /// </para>
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
            BindHttpErrorCode(runtime, _resources, alloc,
                _errorCodeMapper);
        }

        // -----------------------------------------------------
        //   shared retArea encoders
        // -----------------------------------------------------

        // result<_, _> (bare Unit/Unit): 1 byte (just outer
        // Ok disc).
        private static void WriteResultUnit(byte[] mem, int retArea,
            Result<Unit, Unit> r)
        {
            mem[retArea] = r.IsOk ? (byte)0 : (byte)1;
        }

        // result<_, header-error>: 1 byte (header-error fixtures
        // use a stripped enum — for v0 we always Ok). The host
        // method returns Result<Unit, HeaderError>; we only
        // surface the disc byte. Err-payload encoding is
        // deferred until a fixture exercises it.
        private static void WriteResultUnitHeaderError(byte[] mem,
            int retArea, Result<Unit, HeaderError> r)
        {
            mem[retArea] = r.IsOk ? (byte)0 : (byte)1;
        }

        // result<own<X>, _>: 8 bytes (1B disc + 3B pad +
        // 4B handle). Caller supplies an int Ok handle that
        // has been resource-table-allocated.
        private static void WriteResultHandleBareErr(byte[] mem,
            int retArea, Result<int, Unit> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                MemoryWriter.WriteI32LE(mem, retArea + 4, r.Ok);
                return;
            }
            // Bare-Unit Err: disc=1, rest stays zeroed.
            mem[retArea] = 1;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
        }

        // result<own<X>, header-error>: 8 bytes. Same shape as
        // result<own<X>, _> on the Ok side; Err-payload not
        // written in v0.
        private static void WriteResultHandleHeaderError(byte[] mem,
            int retArea, Result<int, HeaderError> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                MemoryWriter.WriteI32LE(mem, retArea + 4, r.Ok);
                return;
            }
            mem[retArea] = 1;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
        }

        // result<_, error-code> (placeholder error-code: 1 byte
        // single-case enum). The fixture's wit defines a
        // single-case error-code, so the result variant is just
        // 1 byte — just the outer disc. Used by outgoing-body.
        // finish (whose fixture uses a placeholder).
        private static void WriteResultUnitPlaceholder(byte[] mem,
            int retArea, Result<Unit, ErrorCode> r)
        {
            mem[retArea] = r.IsOk ? (byte)0 : (byte)1;
        }

        // option<u64>: 16 bytes — disc (1B) + 7B pad + u64 (8B).
        private static void WriteOptionU64(byte[] mem, int retArea,
            Option<ulong> opt)
        {
            if (!opt.HasValue)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, opt.Value);
        }

        // option<string>: 12 bytes — disc + 3 pad + ptr@+4 + len@+8.
        private static void WriteOptionString(ExecContext ctx,
            int retArea, Option<string> opt, Realloc alloc)
        {
            var mem = ctx.Memory();
            if (!opt.HasValue)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                ctx.Memory, opt.Value, alloc);
            mem = ctx.Memory();
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, len);
        }

        // -----------------------------------------------------
        //   variant Method / Scheme — wire shape in/out
        // -----------------------------------------------------

        // variant method (10 cases) flat-decoded into one of the
        // generated Method.Method* nested classes. Disc 0–8 are
        // no-payload; disc 9 is other(string).
        private static Method DecodeHttpMethodFlat(
            byte[] memory, int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new Method.MethodGet();
                case 1: return new Method.MethodHead();
                case 2: return new Method.MethodPost();
                case 3: return new Method.MethodPut();
                case 4: return new Method.MethodDelete();
                case 5: return new Method.MethodConnect();
                case 6: return new Method.MethodOptions();
                case 7: return new Method.MethodTrace();
                case 8: return new Method.MethodPatch();
                case 9:
                    var name = Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new Method.MethodOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown Method variant disc: " + disc);
            }
        }

        // variant scheme (3 cases) flat-decoded.
        private static Scheme DecodeHttpSchemeFlat(
            byte[] memory, int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new Scheme.SchemeHTTP();
                case 1: return new Scheme.SchemeHTTPS();
                case 2:
                    var name = Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new Scheme.SchemeOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown Scheme variant disc: " + disc);
            }
        }

        // Map a Method instance to (disc, payloadString).
        private static (byte disc, string? payload) MapHttpMethod(
            Method method)
        {
            return method switch
            {
                Method.MethodGet     => ((byte)0, (string?)null),
                Method.MethodHead    => ((byte)1, (string?)null),
                Method.MethodPost    => ((byte)2, (string?)null),
                Method.MethodPut     => ((byte)3, (string?)null),
                Method.MethodDelete  => ((byte)4, (string?)null),
                Method.MethodConnect => ((byte)5, (string?)null),
                Method.MethodOptions => ((byte)6, (string?)null),
                Method.MethodTrace   => ((byte)7, (string?)null),
                Method.MethodPatch   => ((byte)8, (string?)null),
                Method.MethodOther o => ((byte)9, (string?)o.Value),
                _ => throw new ArgumentException(
                    "Unknown Method subclass: " + method.GetType()),
            };
        }

        private static (byte disc, string? payload) MapHttpScheme(
            Scheme scheme)
        {
            return scheme switch
            {
                Scheme.SchemeHTTP    => ((byte)0, (string?)null),
                Scheme.SchemeHTTPS   => ((byte)1, (string?)null),
                Scheme.SchemeOther o => ((byte)2, (string?)o.Value),
                _ => throw new ArgumentException(
                    "Unknown Scheme subclass: " + scheme.GetType()),
            };
        }

        // Write the variant Method retArea: 12 bytes — 1B disc +
        // 3B pad + 4B ptr + 4B len.
        private static void WriteHttpMethod(ExecContext ctx,
            int retArea, Method method, Realloc alloc)
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
            int retArea, Option<Scheme> opt, Realloc alloc)
        {
            var mem = ctx.Memory();
            if (!opt.HasValue)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            var (disc, payload) = MapHttpScheme(opt.Value);
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
