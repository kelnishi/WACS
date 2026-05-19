// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.Clocks;
using Wacs.WASI.Preview3.Filesystem;
using Wacs.WASI.Preview3.Http;
using Wacs.WASI.Preview3.Random;
using Wacs.WASI.Preview3.Resources;
using Wacs.WASI.Preview3.Sockets;

namespace Wacs.WASI.Preview3
{
    /// <summary>
    /// Composite host configuration for WASI Preview 3 imports.
    /// Sibling of <c>WACS.WASI.Preview2.WasiPreview2Host</c>, but
    /// the wire surface is the Component Model async ABI —
    /// stream/future handles routed through
    /// <see cref="ComponentModel.Async.AsyncDispatcher"/>.
    ///
    /// <para><b>v0 scope</b> (vertical slice): <c>wasi:cli/run</c>
    /// + <c>wasi:cli/{stdin,stdout,stderr}</c>. Sockets / http /
    /// filesystem / clocks / random remain to be ported in
    /// Phase 5 once the v0 slice validates against a real
    /// wit-component fixture.</para>
    ///
    /// <para><b>Wiring shape</b>: this class holds the builder's
    /// configured impls. The actual binding to runtime
    /// host-functions happens through the canon-async binder
    /// (Phase 3 G3+H) once a component-instance is loaded —
    /// the binder identifies the wit-component shim module,
    /// extracts canon-op identities, and registers delegates
    /// that route to the configured impls. Today this class
    /// pins the public surface; the wire-level connection
    /// lands when fixture availability lets us validate the
    /// convention.</para>
    /// </summary>
    public sealed class WasiPreview3Host : IBindable
    {
        private readonly WasiPreview3HostBuilder _config;

        // Lazy-cached defaults so repeated property access
        // returns the same instance — required for DI
        // singleton semantics and consistent with the
        // Preview2 host's lifetime model.
        private IStdin? _stdin;
        private IStdout? _stdout;
        private IStderr? _stderr;
        private IMonotonicClock? _monotonic;
        private ISystemClock? _system;
        private IRandom? _random;
        private IInsecure? _insecure;
        private IInsecureSeed? _insecureSeed;
        private IPreopens? _preopens;
        private IIpNameLookup? _nameLookup;
        private IClient? _httpClient;
        private IHandler? _httpHandler;

        /// <summary>
        /// Host-side handle table for <c>wasi:http/types.fields</c>
        /// resources. The guest sees i32 handles; we look up the
        /// CLR <see cref="IFields"/> instance through this table
        /// at every method dispatch.
        ///
        /// <para>One table per host instance — fresh component
        /// instances get fresh tables. The same handle integer
        /// across two host instances refers to two unrelated
        /// fields objects, which matches the per-component
        /// resource-lifetime model.</para>
        /// </summary>
        public HostResourceTable<IFields> FieldsHandles { get; } =
            new HostResourceTable<IFields>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.request-options</c> resources.</summary>
        public HostResourceTable<IRequestOptions> RequestOptionsHandles { get; } =
            new HostResourceTable<IRequestOptions>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.response</c> resources.</summary>
        public HostResourceTable<IResponse> ResponseHandles { get; } =
            new HostResourceTable<IResponse>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.request</c> resources.</summary>
        public HostResourceTable<IRequest> RequestHandles { get; } =
            new HostResourceTable<IRequest>();

        /// <summary>Host-side handle table for
        /// <c>wasi:filesystem/types.descriptor</c> resources.</summary>
        public HostResourceTable<IDescriptor> DescriptorHandles { get; } =
            new HostResourceTable<IDescriptor>();

        /// <summary>Host-side handle table for
        /// <c>wasi:sockets/types.tcp-socket</c> resources.</summary>
        public HostResourceTable<ITcpSocket> TcpSocketHandles { get; } =
            new HostResourceTable<ITcpSocket>();

        /// <summary>Host-side handle table for
        /// <c>wasi:sockets/types.udp-socket</c> resources.</summary>
        public HostResourceTable<IUdpSocket> UdpSocketHandles { get; } =
            new HostResourceTable<IUdpSocket>();

        public WasiPreview3Host() : this(new WasiPreview3HostBuilder()) { }

        public WasiPreview3Host(WasiPreview3HostBuilder builder)
        {
            _config = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public IStdin Stdin => _stdin ??=
            _config.Stdin ?? new StreamBackedStdin(Console.OpenStandardInput());

        public IStdout Stdout => _stdout ??=
            _config.Stdout ?? new StreamBackedSink(Console.OpenStandardOutput());

        public IStderr Stderr => _stderr ??=
            (_config.Stderr as IStderr)
                ?? new StreamBackedSink(Console.OpenStandardError());

        public IMonotonicClock MonotonicClock =>
            _monotonic ??= _config.MonotonicClock ?? new MonotonicClock();

        public ISystemClock SystemClock =>
            _system ??= _config.SystemClock ?? new SystemClock();

        public IRandom Random =>
            _random ??= _config.Random ?? new Random.Random();

        public IInsecure InsecureRandom =>
            _insecure ??= _config.InsecureRandom ?? new InsecureRandom();

        public IInsecureSeed InsecureSeed =>
            _insecureSeed ??= _config.InsecureSeed ?? new InsecureSeedSource();

        /// <summary>
        /// Filesystem preopen set. Defaults to an empty list —
        /// guests with no configured preopens see no
        /// filesystem. Embedders typically populate this via
        /// <see cref="DirectoryPreopens.FromHostPaths"/>.
        /// </summary>
        public IPreopens Preopens =>
            _preopens ??= _config.Preopens
                ?? DirectoryPreopens.FromHostPaths();

        /// <summary>
        /// Hostname → IP address resolver. Defaults to
        /// <see cref="NoNameLookup"/> — guests requesting
        /// resolution see <see cref="ErrorCode.PermanentResolverFailure"/>
        /// until the embedder explicitly opts in (typically by
        /// configuring <see cref="DnsBackedNameLookup"/> or a
        /// custom impl). This default fail-closed posture
        /// prevents accidental leakage of the host's DNS
        /// resolver to untrusted components.
        /// </summary>
        public IIpNameLookup IpNameLookup =>
            _nameLookup ??= _config.IpNameLookup ?? new NoNameLookup();

        /// <summary>
        /// Outbound HTTP client. Default is an
        /// <see cref="HttpBackedClient"/> with a fresh
        /// <see cref="System.Net.Http.HttpClient"/>; embedders
        /// that want pinned cert validation, custom proxies, or
        /// shared client pooling configure their own via the
        /// builder.
        /// </summary>
        public IClient HttpClient =>
            _httpClient ??= _config.HttpClient ?? new HttpBackedClient();

        /// <summary>
        /// Inbound HTTP handler. No default — guests that
        /// import <c>wasi:http/handler</c> for inbound serving
        /// must have an embedder-configured handler. The
        /// canon-async binding throws
        /// <see cref="HttpErrorCode.ConfigurationError"/> at
        /// invocation time when this is null.
        /// </summary>
        public IHandler? HttpHandler =>
            _httpHandler ??= _config.HttpHandler;

        /// <summary>
        /// The component-instance dispatcher the host bindings
        /// route stream/future handles through. Late-bound — set
        /// by the embedder after <c>ComponentInstance.Instantiate</c>
        /// has allocated the dispatcher (it's available on
        /// <c>ComponentInstance.AsyncDispatcher</c>). The host
        /// function delegates registered by <see cref="BindToRuntime"/>
        /// resolve this lazily so a null setting at bind time
        /// doesn't break registration.
        /// </summary>
        public AsyncDispatcher? Dispatcher { get; set; }

        /// <summary>
        /// Registers the WASI Preview 3 host-import surface on
        /// <paramref name="runtime"/>:
        ///
        /// <list type="bullet">
        ///   <item><c>wasi:cli/stdout@0.3.0-rc-2026-03-15
        ///     .write-via-stream(data: stream&lt;u8&gt;) -&gt;
        ///     future&lt;result&lt;_, error-code&gt;&gt;</c> —
        ///     drains the supplied stream handle into
        ///     <see cref="Stdout"/>, returns a future handle the
        ///     guest awaits.</item>
        ///   <item><c>wasi:cli/stderr.write-via-stream</c> —
        ///     same shape, routes to <see cref="Stderr"/>.</item>
        /// </list>
        ///
        /// <para>The delegate bodies resolve
        /// <see cref="Dispatcher"/> at call time, so the
        /// embedder can set it AFTER instantiating the
        /// component (where the dispatcher is created). At call
        /// time the dispatcher must be set or the delegate
        /// throws a clear "Dispatcher not set" diagnostic.</para>
        ///
        /// <para><b>Wire convention:</b> uses
        /// <c>(wasi:cli/stdout@0.3.0-rc-2026-03-15,
        /// write-via-stream)</c> as the import name — wit-
        /// component's lowering of the component's import of
        /// this interface method. The exact spelling awaits
        /// fixture-level verification (same caveat as the
        /// canon-async binder's placeholder convention); if
        /// it differs in real output, the binding-name resolver
        /// hook is the override point. The Slice J commitment
        /// is the binding shape + delegate body, not the wire-
        /// name lockdown.</para>
        ///
        /// <para><c>read-via-stream</c> (stdin) returns
        /// <c>tuple&lt;stream&lt;u8&gt;, future&lt;result&lt;_,
        /// error-code&gt;&gt;&gt;</c>. Per canon-ABI flat-lowering
        /// rules (<c>MAX_FLAT_RESULTS = 1</c>; this tuple flattens
        /// to 2 i32s) the lowered host signature is
        /// <c>(retptr: i32) -&gt; ()</c> — the host writes a
        /// <c>(stream-handle, future-handle)</c> i32 pair into
        /// the component's linear memory at <c>retptr</c>
        /// (8 bytes, 4-aligned).</para>
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));

            runtime.BindHostFunction(
                (StdoutModuleName, "write-via-stream"),
                (Func<ExecContext, int, int>)((_, streamHandle) =>
                    InvokeWriteViaStream(Stdout, streamHandle)));

            runtime.BindHostFunction(
                (StderrModuleName, "write-via-stream"),
                (Func<ExecContext, int, int>)((_, streamHandle) =>
                    InvokeWriteViaStream(Stderr, streamHandle)));

            runtime.BindHostFunction(
                (StdinModuleName, "read-via-stream"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeReadViaStream(Stdin, retptr)));

            // wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15
            //   now: () -> mark (u64)
            //   get-resolution: () -> duration (u64)
            //   wait-until: async (when: mark) -> ()
            //   wait-for: async (how-long: duration) -> ()
            //
            // The async `wait-*` functions bind here as
            // synchronous-blocking shapes: the host-side
            // implementation completes the Task.Delay synchronously
            // from the wasm caller's perspective. The lowered wire
            // signature for an async-func import depends on the
            // caller's canon-lower opts (`async`/`callback`); the
            // sync-blocking form below is the conservative starting
            // point until a real wit-component fixture pins the
            // exact convention.
            runtime.BindHostFunction(
                (MonotonicClockModuleName, "now"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)MonotonicClock.Now())));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "get-resolution"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)MonotonicClock.GetResolution())));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "wait-until"),
                (Action<ExecContext, long>)((_, when) =>
                    MonotonicClock.WaitUntilAsync(
                        unchecked((ulong)when)).GetAwaiter().GetResult()));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "wait-for"),
                (Action<ExecContext, long>)((_, howLong) =>
                    MonotonicClock.WaitForAsync(
                        unchecked((ulong)howLong)).GetAwaiter().GetResult()));

            // wasi:clocks/system-clock@0.3.0-rc-2026-03-15
            //   record instant { seconds: s64, nanoseconds: u32 }
            //   now: () -> instant
            //   get-resolution: () -> duration (u64)
            //
            // instant flat-count = 2 (s64 + u32). Exceeds
            // MAX_FLAT_RESULTS = 1 → lowered shape is
            // (retptr: i32) -> () with the record laid out at
            // retptr (16 bytes, 8-aligned). Wire layout:
            //   +0..8: seconds (s64 LE)
            //   +8..12: nanoseconds (u32 LE)
            //   +12..16: 4-byte tail pad (record's align is 8)
            runtime.BindHostFunction(
                (SystemClockModuleName, "now"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeSystemClockNow(SystemClock, retptr)));

            runtime.BindHostFunction(
                (SystemClockModuleName, "get-resolution"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)SystemClock.GetResolution())));

            // wasi:random — three interfaces. Memory-writing
            // bindings (get-*-bytes, get-insecure-seed) need
            // cabi_realloc to reserve guest space; the resolver
            // captures the runtime here and resolves the export
            // lazily at first call (after instantiation).
            var realloc = new Realloc(runtime);

            // wasi:random/random@0.3.0-rc-2026-03-15
            //   get-random-bytes: func(max-len: u64) -> list<u8>
            //   get-random-u64: func() -> u64
            //
            // list<u8> flat-count = 2 (ptr + len) > MAX_FLAT_RESULTS
            // → retptr layout: ptr (i32) + len (i32). The bytes
            // themselves are allocated via cabi_realloc and the
            // host writes them at the returned ptr.
            runtime.BindHostFunction(
                (RandomModuleName, "get-random-u64"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)Random.GetRandomU64())));

            runtime.BindHostFunction(
                (RandomModuleName, "get-random-bytes"),
                (Action<ExecContext, long, int>)((_, maxLen, retptr) =>
                    InvokeGetRandomBytes(Random, realloc,
                        unchecked((ulong)maxLen), retptr)));

            // wasi:random/insecure@0.3.0-rc-2026-03-15
            //   get-insecure-random-bytes: func(max-len: u64) -> list<u8>
            //   get-insecure-random-u64: func() -> u64
            runtime.BindHostFunction(
                (InsecureRandomModuleName, "get-insecure-random-u64"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)InsecureRandom.GetInsecureRandomU64())));

            runtime.BindHostFunction(
                (InsecureRandomModuleName, "get-insecure-random-bytes"),
                (Action<ExecContext, long, int>)((_, maxLen, retptr) =>
                    InvokeGetInsecureRandomBytes(InsecureRandom, realloc,
                        unchecked((ulong)maxLen), retptr)));

            // wasi:random/insecure-seed@0.3.0-rc-2026-03-15
            //   get-insecure-seed: func() -> tuple<u64, u64>
            //
            // tuple<u64, u64> flat-count = 2 > MAX_FLAT_RESULTS
            // → retptr layout: u64 (8 bytes) + u64 (8 bytes) =
            // 16 bytes, 8-aligned.
            runtime.BindHostFunction(
                (InsecureSeedModuleName, "get-insecure-seed"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeGetInsecureSeed(InsecureSeed, retptr)));

            // wasi:http/types.fields — host-resource lifecycle +
            // representative method dispatch. Constructor allocates
            // a fresh empty Fields and returns its handle; methods
            // take the self handle as first arg and look up the
            // CLR instance through FieldsHandles. Destructor drops.
            //
            // Names use the wit-component convention
            // ([constructor]NAME / [method]NAME.OP /
            // [resource-drop]NAME); the exact spelling awaits
            // real wit-component fixture validation, same caveat
            // as Slice J's stdout binding.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[constructor]fields"),
                (Func<ExecContext, int>)(_ =>
                    FieldsHandles.Allocate(new Fields())));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]fields"),
                (Action<ExecContext, int>)((_, handle) =>
                    InvokeFieldsDrop(handle)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.has"),
                (Func<ExecContext, int, int, int, int>)((_, self, namePtr, nameLen) =>
                    InvokeFieldsHas(self, namePtr, nameLen) ? 1 : 0));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.append"),
                (Action<ExecContext, int, int, int, int, int>)(
                    (_, self, namePtr, nameLen, valuePtr, valueLen) =>
                        InvokeFieldsAppend(self, namePtr, nameLen,
                            valuePtr, valueLen)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.set"),
                (Action<ExecContext, int, int, int, int, int, int>)(
                    (_, self, namePtr, nameLen, listPtr, listCount, retptr) =>
                        InvokeFieldsSet(self, namePtr, nameLen,
                            listPtr, listCount, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.delete"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, namePtr, nameLen, retptr) =>
                        InvokeFieldsDelete(self, namePtr, nameLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.clone"),
                (Func<ExecContext, int, int>)((_, self) =>
                    InvokeFieldsClone(self)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]request"),
                (Action<ExecContext, int>)((_, handle) =>
                    RequestHandles.Drop(handle)));

            // wasi:http/types.request-options — fully wired.
            // All methods are simple primitive round-trips
            // (the option<duration> wire shape lowers to
            // (i32 is-some, i64 value)).
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[constructor]request-options"),
                (Func<ExecContext, int>)(_ =>
                    RequestOptionsHandles.Allocate(new RequestOptions())));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]request-options"),
                (Action<ExecContext, int>)((_, handle) =>
                    RequestOptionsHandles.Drop(handle)));

            BindOptionalDurationTimeout(runtime, "connect-timeout",
                opt => opt.GetConnectTimeout(),
                (opt, v) => opt.SetConnectTimeout(v));
            BindOptionalDurationTimeout(runtime, "first-byte-timeout",
                opt => opt.GetFirstByteTimeout(),
                (opt, v) => opt.SetFirstByteTimeout(v));
            BindOptionalDurationTimeout(runtime, "between-bytes-timeout",
                opt => opt.GetBetweenBytesTimeout(),
                (opt, v) => opt.SetBetweenBytesTimeout(v));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request-options.clone"),
                (Func<ExecContext, int, int>)((_, self) =>
                {
                    var opt = RequireRequestOptions(self);
                    return RequestOptionsHandles.Allocate(opt.Clone());
                }));

            // wasi:http/types.response — drop + status getter/setter.
            // The constructor (static `new`) returns a
            // tuple<response, future<...>> which needs the
            // multi-return shape from Slice K plus future
            // allocation; ships in a later slice. Other simple
            // methods (get-headers) follow once the headers
            // shared-handle resolution model is in place.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]response"),
                (Action<ExecContext, int>)((_, handle) =>
                    ResponseHandles.Drop(handle)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]response.get-status-code"),
                (Func<ExecContext, int, int>)((_, self) =>
                    RequireResponse(self).GetStatusCode()));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]response.set-status-code"),
                (Action<ExecContext, int, int>)((_, self, code) =>
                    RequireResponse(self).SetStatusCode(
                        unchecked((ushort)code))));

            // wasi:http/client.send and wasi:http/handler.handle.
            // Both are async func in the WIT and lower as a
            // canon-async call. Phase 5 binds them sync-blocking
            // (.GetAwaiter().GetResult() on the Task<IResponse>);
            // when the canon-async-func wire shape stabilizes
            // (see Phase 3 Slice L), the binding moves to the
            // cooperative-yield path via the lift adapter.
            //
            // Wire convention: takes a request handle, returns
            // a response handle. The err path throws
            // HttpException; the canon-async binding lowers to
            // result<response, error-code>::err.
            runtime.BindHostFunction(
                (HttpClientModuleName, "send"),
                (Func<ExecContext, int, int>)((_, requestHandle) =>
                    InvokeClientSend(requestHandle)));

            runtime.BindHostFunction(
                (HttpHandlerModuleName, "handle"),
                (Func<ExecContext, int, int>)((_, requestHandle) =>
                    InvokeHandlerHandle(requestHandle)));

            // wasi:filesystem/types.descriptor — drop + the
            // simplest sync method as a representative wire-up.
            // The remaining ~23 methods (stat / open-at /
            // read-via-stream / etc.) follow the same pattern
            // and ship in follow-up slices alongside the
            // canon-async-func wire-shape settling.
            runtime.BindHostFunction(
                (FilesystemTypesModuleName, "[resource-drop]descriptor"),
                (Action<ExecContext, int>)((_, handle) =>
                    DescriptorHandles.Drop(handle)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName, "[method]descriptor.get-flags"),
                (Func<ExecContext, int, int>)((_, self) =>
                    (int)RequireDescriptor(self).GetFlags()));

            // wasi:filesystem/preopens.get-directories — the
            // landmark binding that lets guests discover the
            // host-configured preopen set. Returns
            // list<tuple<descriptor, string>>:
            //
            //   retptr+0: list-ptr (i32)
            //   retptr+4: list-count (i32)
            //
            // List body at list-ptr: count tuples × 12 bytes:
            //
            //   +0: descriptor-handle (i32)
            //   +4: path-string-ptr   (i32)
            //   +8: path-string-len   (i32)
            //
            // Each tuple's path string lives in its own
            // cabi_realloc-allocated guest memory region. We
            // reuse the `realloc` allocator already constructed
            // above for the wasi:random list-returning paths —
            // a single instance per BindToRuntime call caches the
            // cabi_realloc resolution.
            runtime.BindHostFunction(
                (FilesystemPreopensModuleName, "get-directories"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokePreopensGetDirectories(retptr, realloc)));

            // wasi:sockets — resource drops + ip-name-lookup.
            // Full ITcpSocket/IUdpSocket method wire-ups + a
            // System.Net.Sockets-backed default impl ship in
            // a follow-up slice; the resource drops + the DNS
            // lookup are the immediately useful pieces.
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[resource-drop]tcp-socket"),
                (Action<ExecContext, int>)((_, handle) =>
                    TcpSocketHandles.Drop(handle)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[resource-drop]udp-socket"),
                (Action<ExecContext, int>)((_, handle) =>
                    UdpSocketHandles.Drop(handle)));

            // wasi:sockets/ip-name-lookup.resolve-addresses
            //   async func(name: string) -> result<list<ip-address>, error-code>
            //
            // The (name) arg lowers to (i32 ptr, i32 len). The
            // result list flat-lowers to 2 i32s > MAX_FLAT_RESULTS
            // → retptr layout (8 bytes 4-aligned: list-ptr +
            // list-count). Each ip-address variant entry is 18
            // bytes 2-aligned: disc byte (case 0=ipv4, 1=ipv6) +
            // 1-byte align-pad + 16 bytes payload (max(4, 16)).
            // Async-blocking on the Task<IReadOnlyList<IpAddress>>
            // until the canon-async-func wire shape settles.
            runtime.BindHostFunction(
                (IpNameLookupModuleName, "resolve-addresses"),
                (Action<ExecContext, int, int, int>)((_, namePtr, nameLen, retptr) =>
                    InvokeResolveAddresses(namePtr, nameLen, retptr, realloc)));
        }

        // ---- wasi:sockets binding bodies (Slice J) -------------------

        /// <summary>Invoke
        /// <c>wasi:sockets/ip-name-lookup.resolve-addresses</c>'s
        /// body. Reads the hostname from guest memory, blocks
        /// on the Task<IReadOnlyList<IpAddress>>, allocates the
        /// canon-ABI-lowered variant list through cabi_realloc,
        /// writes the outer (list-ptr, list-count) at retptr.</summary>
        public void InvokeResolveAddresses(
            int namePtr, int nameLen, int retptr, ICabiRealloc realloc)
        {
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:sockets/ip-name-lookup.resolve-addresses: " +
                    "dispatcher.Memory must be set before this " +
                    "import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/ip-name-lookup.resolve-addresses: " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate an 8-byte 4-aligned " +
                    "return area.");

            // Read the hostname string.
            string name;
            if (nameLen == 0)
                name = string.Empty;
            else
                name = System.Text.Encoding.UTF8.GetString(
                    memory.AsSpan(namePtr, nameLen));

            // Resolve (sync-blocking).
            var addresses = IpNameLookup
                .ResolveAddressesAsync(name)
                .GetAwaiter().GetResult();

            // Empty list: (0, 0) at retptr.
            int count = addresses.Count;
            if (count == 0)
            {
                var emptyDest = memory.AsSpan(retptr, 8);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(0), 0);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(4), 0);
                return;
            }

            // ip-address wire entry: 18 bytes, 2-aligned.
            //   +0: disc (u8: 0=ipv4, 1=ipv6)
            //   +1: align pad
            //   +2..18: payload (max(ipv4 = 4, ipv6 = 16) = 16 bytes)
            const int entrySize = 18;
            int listPtr = realloc.Allocate(
                align: 2, size: count * entrySize);
            var listSpan = memory.AsSpan(listPtr, count * entrySize);
            for (int i = 0; i < count; i++)
            {
                int off = i * entrySize;
                var addr = addresses[i];
                // Zero the entire entry first to keep the pad
                // bytes deterministic.
                for (int j = 0; j < entrySize; j++)
                    listSpan[off + j] = 0;

                if (addr.Family == IpAddressFamily.Ipv4)
                {
                    listSpan[off + 0] = 0; // disc
                    // payload at +2: 4 bytes.
                    listSpan[off + 2] = addr.V4.A;
                    listSpan[off + 3] = addr.V4.B;
                    listSpan[off + 4] = addr.V4.C;
                    listSpan[off + 5] = addr.V4.D;
                }
                else
                {
                    listSpan[off + 0] = 1; // disc = ipv6
                    // payload at +2: 8 u16s, 16 bytes, network
                    // byte order (BE) to match the WIT tuple
                    // semantics.
                    ushort[] groups = new ushort[]
                    {
                        addr.V6.G0, addr.V6.G1, addr.V6.G2, addr.V6.G3,
                        addr.V6.G4, addr.V6.G5, addr.V6.G6, addr.V6.G7,
                    };
                    for (int slot = 0; slot < 8; slot++)
                    {
                        listSpan[off + 2 + slot * 2 + 0] =
                            (byte)((groups[slot] >> 8) & 0xFF);
                        listSpan[off + 2 + slot * 2 + 1] =
                            (byte)(groups[slot] & 0xFF);
                    }
                }
            }

            // Outer (list-ptr, list-count) at retptr.
            var outerDest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(0), listPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(4), count);
        }

        // ---- wasi:filesystem binding bodies (Slice I) ---------------

        /// <summary>Invoke
        /// <c>wasi:filesystem/preopens.get-directories</c>'s
        /// body. Allocates a descriptor handle per configured
        /// preopen, writes each path string via cabi_realloc,
        /// then allocates the outer tuple-list and writes its
        /// (ptr, count) at the retptr.</summary>
        public void InvokePreopensGetDirectories(int retptr, ICabiRealloc realloc)
        {
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:filesystem/preopens.get-directories: " +
                    "dispatcher.Memory must be set before this " +
                    "import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:filesystem/preopens.get-directories: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate an 8-byte 4-aligned " +
                    "return area.");

            var preopens = Preopens.GetDirectories();
            int count = preopens.Count;
            if (count == 0)
            {
                // Empty list: (ptr=0, count=0) at retptr.
                var emptyDest = memory.AsSpan(retptr, 8);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(0), 0);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(4), 0);
                return;
            }

            // For each preopen, allocate a descriptor handle +
            // a guest-side path-string region. We accumulate the
            // (handle, str-ptr, str-len) triples in a host-side
            // staging array; the final array goes through one
            // cabi_realloc allocation.
            var triples = new (int Handle, int StrPtr, int StrLen)[count];
            for (int i = 0; i < count; i++)
            {
                var pre = preopens[i];
                int handle = DescriptorHandles.Allocate(pre.Descriptor);
                var pathBytes = System.Text.Encoding.UTF8.GetBytes(pre.Path);
                int strPtr = pathBytes.Length == 0
                    ? 0
                    : realloc.Allocate(align: 1, size: pathBytes.Length);
                if (pathBytes.Length > 0)
                    new ReadOnlySpan<byte>(pathBytes)
                        .CopyTo(memory.AsSpan(strPtr, pathBytes.Length));
                triples[i] = (handle, strPtr, pathBytes.Length);
            }

            // Tuple<descriptor, string> wire size: 12 bytes
            // (i32 handle + i32 ptr + i32 len). Tuple-list
            // allocation must be 4-aligned.
            int tupleArrayBytes = count * 12;
            int arrayPtr = realloc.Allocate(align: 4, size: tupleArrayBytes);
            var arraySpan = memory.AsSpan(arrayPtr, tupleArrayBytes);
            for (int i = 0; i < count; i++)
            {
                int off = i * 12;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 0), triples[i].Handle);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 4), triples[i].StrPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 8), triples[i].StrLen);
            }

            // Write the outer (list-ptr, list-count) at retptr.
            var outerDest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(0), arrayPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(4), count);
        }

        private IDescriptor RequireDescriptor(int handle)
        {
            var desc = DescriptorHandles.Get(handle);
            if (desc == null)
                throw new FilesystemException(
                    Filesystem.ErrorCode.BadDescriptor,
                    $"wasi:filesystem/types.descriptor: handle " +
                    $"{handle} is not allocated.");
            return desc;
        }

        // ---- wasi:http/client + handler binding bodies ---------------

        /// <summary>Invoke <c>wasi:http/client.send</c>'s body.
        /// Resolves the request handle, calls
        /// <see cref="IClient.SendAsync(IRequest, System.Threading.CancellationToken)"/>
        /// sync-blocking, allocates a response handle bound to
        /// the lifted response. The async-func cooperative-yield
        /// shape will replace the sync block once the canon-async-
        /// func wire convention stabilizes.</summary>
        public int InvokeClientSend(int requestHandle)
        {
            var request = RequireRequest(requestHandle);
            var response = HttpClient.SendAsync(request)
                .GetAwaiter().GetResult();
            return ResponseHandles.Allocate(response);
        }

        /// <summary>Invoke <c>wasi:http/handler.handle</c>'s body.
        /// Routes the request to the configured
        /// <see cref="IHandler"/>; throws
        /// <see cref="HttpException"/> with
        /// <see cref="HttpErrorCode.ConfigurationError"/> when
        /// no handler is configured — guests importing
        /// <c>wasi:http/handler</c> need an embedder-provided
        /// inbound handler.</summary>
        public int InvokeHandlerHandle(int requestHandle)
        {
            var handler = HttpHandler;
            if (handler == null)
                throw new HttpException(
                    HttpErrorCode.ConfigurationError,
                    "wasi:http/handler.handle: no IHandler " +
                    "configured. Set " +
                    "WasiPreview3HostBuilder.HttpHandler.");
            var request = RequireRequest(requestHandle);
            var response = handler.HandleAsync(request)
                .GetAwaiter().GetResult();
            return ResponseHandles.Allocate(response);
        }

        private IRequest RequireRequest(int handle)
        {
            var req = RequestHandles.Get(handle);
            if (req == null)
                throw new HttpException(
                    HttpErrorCode.InternalError,
                    $"wasi:http/types.request: handle {handle} " +
                    "is not allocated.");
            return req;
        }

        // Helper that registers the get/set pair for an
        // option<duration>-shaped timeout property. The lowered
        // signature is (self, i32 is-some, i64 value) on the
        // setter and returns (i32 is-some, i64 value) on the
        // getter via a 16-byte retArea (option<duration>
        // flat-count = 2 = i32 disc + i64 payload, exceeds
        // MAX_FLAT_RESULTS).
        private void BindOptionalDurationTimeout(
            WasmRuntime runtime, string propertyName,
            Func<IRequestOptions, ulong?> getter,
            Action<IRequestOptions, ulong?> setter)
        {
            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    $"[method]request-options.get-{propertyName}"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestOptionsGetTimeout(
                        self, retptr, getter)));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    $"[method]request-options.set-{propertyName}"),
                (Action<ExecContext, int, int, long>)(
                    (_, self, isSome, value) =>
                        InvokeRequestOptionsSetTimeout(
                            self, isSome, value, setter)));
        }

        // ---- wasi:http/types binding bodies (Slice G additions) ----

        /// <summary>Invoke
        /// <c>[method]request-options.get-{connect,first-byte,between-bytes}-timeout</c>'s
        /// body. Writes the option&lt;duration&gt; at retptr:
        /// (i32 disc, i64 value), 16 bytes 8-aligned.</summary>
        public void InvokeRequestOptionsGetTimeout(
            int self, int retptr,
            Func<IRequestOptions, ulong?> getter)
        {
            var opt = RequireRequestOptions(self);
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new RequestOptionsException(
                    RequestOptionsError.Other,
                    "request-options.get-timeout: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 16-byte 8-aligned " +
                    "return area.");
            var dest = memory.AsSpan(retptr, 16);
            var value = getter(opt);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(
                    dest.Slice(0), value.HasValue ? 1 : 0);
            // 4-byte tail-pad in the disc slot (option<u64>
            // align = 8 forces the payload to start at +8).
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(
                    dest.Slice(8), value ?? 0UL);
        }

        /// <summary>Invoke
        /// <c>[method]request-options.set-{connect,first-byte,between-bytes}-timeout</c>'s
        /// body. The option&lt;duration&gt; param flat-lowers to
        /// (i32 is-some, i64 value).</summary>
        public void InvokeRequestOptionsSetTimeout(
            int self, int isSome, long value,
            Action<IRequestOptions, ulong?> setter)
        {
            var opt = RequireRequestOptions(self);
            setter(opt, isSome != 0
                ? unchecked((ulong)value)
                : (ulong?)null);
        }

        private IRequestOptions RequireRequestOptions(int handle)
        {
            var opt = RequestOptionsHandles.Get(handle);
            if (opt == null)
                throw new RequestOptionsException(
                    RequestOptionsError.Other,
                    $"wasi:http/types.request-options: handle " +
                    $"{handle} is not allocated.");
            return opt;
        }

        private IResponse RequireResponse(int handle)
        {
            var resp = ResponseHandles.Get(handle);
            if (resp == null)
                throw new HttpException(
                    HttpErrorCode.InternalError,
                    $"wasi:http/types.response: handle {handle} " +
                    "is not allocated.");
            return resp;
        }

        // ---- wasi:http/types.fields binding bodies --------------------

        /// <summary>Invoke the
        /// <c>[resource-drop]fields</c> body — release the
        /// handle from <see cref="FieldsHandles"/>. Idempotent:
        /// dropping an absent handle is a no-op (matches the
        /// canon spec's "drop on missing handle is silent").</summary>
        public void InvokeFieldsDrop(int handle)
        {
            FieldsHandles.Drop(handle);
        }

        /// <summary>Invoke <c>[method]fields.has(name)</c> —
        /// looks up the fields instance by self-handle, reads
        /// the name string from guest memory at
        /// <paramref name="namePtr"/>, returns whether the
        /// field exists. Throws when the self handle is
        /// invalid.</summary>
        public bool InvokeFieldsHas(int self, int namePtr, int nameLen)
        {
            var fields = RequireFields(self);
            var name = ReadGuestUtf8(namePtr, nameLen);
            return fields.Has(name);
        }

        /// <summary>Invoke
        /// <c>[method]fields.append(name, value)</c> — reads
        /// both the name (UTF-8 string) and value (byte list)
        /// from guest memory and forwards to
        /// <see cref="IFields.Append"/>. Throws on invalid
        /// handle or on the err path
        /// (<see cref="HeaderException"/> from the impl).</summary>
        public void InvokeFieldsAppend(
            int self, int namePtr, int nameLen,
            int valuePtr, int valueLen)
        {
            var fields = RequireFields(self);
            var name = ReadGuestUtf8(namePtr, nameLen);
            var memory = RequireMemoryForHttp();
            var value = new byte[valueLen];
            if (valueLen > 0)
                memory.AsSpan(valuePtr, valueLen).CopyTo(value);
            fields.Append(name, value);
        }

        /// <summary>Invoke
        /// <c>[method]fields.set(name, list&lt;list&lt;u8&gt;&gt;)</c>.
        /// Reads the name + value list from guest memory,
        /// forwards to <see cref="IFields.Set"/>, writes the
        /// <c>result&lt;_, header-error&gt;</c> at retptr.</summary>
        public void InvokeFieldsSet(
            int self, int namePtr, int nameLen,
            int listPtr, int listCount,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);
            HeaderException? caught = null;
            try
            {
                var fields = RequireFields(self);
                var name = ReadGuestUtf8(namePtr, nameLen);
                var values = ReadListOfByteLists(listPtr, listCount);
                fields.Set(name, values);
            }
            catch (HeaderException ex) { caught = ex; }
            WriteHeaderErrorResult(
                memory.AsSpan(retptr, ResultHeaderErrorSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke <c>[method]fields.delete(name)</c>.
        /// Forwards to <see cref="IFields.Delete"/>, writes
        /// the <c>result&lt;_, header-error&gt;</c> at retptr.</summary>
        public void InvokeFieldsDelete(
            int self, int namePtr, int nameLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);
            HeaderException? caught = null;
            try
            {
                var fields = RequireFields(self);
                fields.Delete(ReadGuestUtf8(namePtr, nameLen));
            }
            catch (HeaderException ex) { caught = ex; }
            WriteHeaderErrorResult(
                memory.AsSpan(retptr, ResultHeaderErrorSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke <c>[method]fields.clone() -&gt;
        /// own&lt;fields&gt;</c>. Returns a fresh handle bound
        /// to the cloned IFields. No err path (the WIT clone
        /// signature has no result wrapper).</summary>
        public int InvokeFieldsClone(int self)
        {
            var fields = RequireFields(self);
            var cloned = fields.Clone();
            return FieldsHandles.Allocate(cloned);
        }

        // ---- result<_, header-error> wire encoding -------------------
        //
        // Canon-ABI layout for `result<_, header-error>` (size 20,
        // align 4):
        //
        //   retptr+0: result-disc (u8) + 3-byte pad
        //   retptr+4: header-error variant (16 bytes, used when disc=1):
        //     +4: header-error-disc (u8) + 3-byte pad
        //     +8: option<string> (12 bytes — only the `other` case):
        //       +8: option-disc (u8) + 3-byte pad
        //       +12: string-ptr (i32)
        //       +16: string-len (i32)

        private const int ResultHeaderErrorSize = 20;

        private static void ValidateResultHeaderErrorRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultHeaderErrorSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.fields: result<_, header-error> " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 20-byte 4-aligned " +
                    "return area.");
        }

        private static void WriteHeaderErrorResult(
            Span<byte> dest, HeaderException? ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex == null)
            {
                dest[0] = 0; // result-disc = ok
                return;
            }
            dest[0] = 1; // result-disc = err
            dest[4] = (byte)ex.Code; // header-error-disc (0..4)
            if (ex.Code == HeaderError.Other && ex.OtherPayload != null)
            {
                dest[8] = 1; // option-disc = some
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(ex.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(align: 1, size: bytes.Length)
                    : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(16), bytes.Length);
            }
            // For unit-payload cases (invalid-syntax / forbidden /
            // immutable / size-exceeded), the bytes at +8..20
            // remain zero from the Clear() above — those fields
            // are payload-irrelevant since the disc selects the
            // unit case.
        }

        // Reads list<list<u8>> from guest memory at listPtr.
        // Each outer-list element is 8 bytes (i32 ptr, i32 len);
        // each inner list is a contiguous byte run at its ptr.
        private byte[][] ReadListOfByteLists(int listPtr, int listCount)
        {
            var memory = RequireMemoryForHttp();
            var result = new byte[listCount][];
            for (int i = 0; i < listCount; i++)
            {
                var entrySpan = memory.AsSpan(listPtr + i * 8, 8);
                int ptr = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32LittleEndian(entrySpan.Slice(0));
                int len = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32LittleEndian(entrySpan.Slice(4));
                var bytes = new byte[len];
                if (len > 0)
                    memory.AsSpan(ptr, len).CopyTo(bytes);
                result[i] = bytes;
            }
            return result;
        }

        private IFields RequireFields(int handle)
        {
            var fields = FieldsHandles.Get(handle);
            if (fields == null)
                throw new HeaderException(
                    HeaderError.Other,
                    $"wasi:http/types.fields: handle {handle} " +
                    "is not allocated.");
            return fields;
        }

        private string ReadGuestUtf8(int ptr, int len)
        {
            var memory = RequireMemoryForHttp();
            if (len == 0) return string.Empty;
            return System.Text.Encoding.UTF8.GetString(
                memory.AsSpan(ptr, len));
        }

        private Wacs.Core.Runtime.Types.MemoryInstance RequireMemoryForHttp()
        {
            var dispatcher = RequireDispatcher();
            return dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:http binding: dispatcher.Memory must be " +
                    "set before any string- or list-marshaling " +
                    "method is invoked.");
        }

        /// <summary>
        /// Invoke <c>wasi:random/random.get-random-bytes</c>'s
        /// host-side delegate body. Allocates
        /// <paramref name="maxLen"/> bytes of guest memory via
        /// <paramref name="realloc"/>, fills it with the
        /// <see cref="IRandom"/>'s output, and writes (ptr, len)
        /// at <paramref name="retptr"/>. Public for test access.
        /// </summary>
        public void InvokeGetRandomBytes(
            IRandom source, ICabiRealloc realloc, ulong maxLen, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var bytes = source.GetRandomBytes(maxLen);
            WriteByteList(realloc, retptr, bytes);
        }

        /// <summary>Same as
        /// <see cref="InvokeGetRandomBytes"/> for the insecure
        /// variant.</summary>
        public void InvokeGetInsecureRandomBytes(
            IInsecure source, ICabiRealloc realloc, ulong maxLen, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var bytes = source.GetInsecureRandomBytes(maxLen);
            WriteByteList(realloc, retptr, bytes);
        }

        /// <summary>
        /// Invoke <c>wasi:random/insecure-seed.get-insecure-seed</c>'s
        /// host-side delegate body. Writes the two u64s at
        /// <paramref name="retptr"/> (16 bytes, 8-aligned).
        /// </summary>
        public void InvokeGetInsecureSeed(IInsecureSeed source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:random/insecure-seed: dispatcher.Memory " +
                    "must be set before this import is invoked.");

            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:random/insecure-seed: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate a 16-byte 8-aligned return area.");

            var (a, b) = source.GetInsecureSeed();
            var dest = memory.AsSpan(retptr, 16);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(0), a);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(8), b);
        }

        // Shared write path for get-*-bytes: allocate guest
        // memory via cabi_realloc, copy the bytes into it,
        // write (ptr, len) into the retArea slot. Spec allows
        // implementations to return fewer bytes than requested
        // (short read) — the wire convention encodes whatever
        // the host actually produced.
        private void WriteByteList(ICabiRealloc realloc, int retptr, byte[] data)
        {
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:random/get-*-bytes: dispatcher.Memory must " +
                    "be set before this import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:random/get-*-bytes: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate an 8-byte 4-aligned return area.");

            int ptr = data.Length == 0
                ? 0
                : realloc.Allocate(align: 1, size: data.Length);
            if (data.Length > 0)
                new ReadOnlySpan<byte>(data)
                    .CopyTo(memory.AsSpan(ptr, data.Length));
            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), ptr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), data.Length);
        }

        /// <summary>
        /// Invoke <c>wasi:clocks/system-clock.now</c>'s host-side
        /// delegate body. Writes the
        /// <see cref="Wacs.WASI.Preview3.Clocks.Instant"/> at
        /// <paramref name="retptr"/> per canon-ABI layout
        /// (16 bytes, 8-aligned: s64 seconds at +0, u32 nanoseconds
        /// at +8, 4-byte tail pad). Public for test access; the
        /// runtime calls this via the bound delegate above.
        /// </summary>
        public void InvokeSystemClockNow(ISystemClock source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:clocks/system-clock.now: dispatcher.Memory " +
                    "must be set before this import is invoked.");

            // record { seconds: s64, nanoseconds: u32 }, align 8,
            // size 16. retptr must be 8-aligned to satisfy the
            // s64 field's alignment.
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:clocks/system-clock.now: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate a 16-byte 8-aligned return area.");

            var instant = source.Now();
            var dest = memory.AsSpan(retptr, 16);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt64LittleEndian(dest.Slice(0), instant.Seconds);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(dest.Slice(8), instant.Nanoseconds);
            // Bytes 12..16 are tail padding; leave as-is (canon
            // ABI doesn't require zeroing).
        }

        /// <summary>
        /// Invoke <c>wasi:cli/stdout.write-via-stream</c>'s
        /// host-side delegate logic directly — same body the
        /// canon-async-shim-routed import calls. Public so tests
        /// can exercise the binding without going through a full
        /// wasm invoke; embedders typically reach this through
        /// the runtime-bound host function instead.
        /// </summary>
        public int InvokeWriteViaStream(IStdout sink, int streamHandle)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var dispatcher = RequireDispatcher();
            var (futureHandle, _) = sink.WriteViaStream(dispatcher, streamHandle);
            return futureHandle;
        }

        /// <summary>Same as <see cref="InvokeWriteViaStream(IStdout, int)"/>
        /// but for stderr.</summary>
        public int InvokeWriteViaStream(IStderr sink, int streamHandle)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var dispatcher = RequireDispatcher();
            var (futureHandle, _) = sink.WriteViaStream(dispatcher, streamHandle);
            return futureHandle;
        }

        /// <summary>
        /// Invoke <c>wasi:cli/stdin.read-via-stream</c>'s host-side
        /// delegate logic. Calls
        /// <see cref="IStdin.ReadViaStream(AsyncDispatcher)"/> to
        /// allocate the stream + future pair and start the
        /// host-side read loop, then writes both handles to the
        /// component's linear memory at
        /// <paramref name="retptr"/>:
        ///
        /// <list type="bullet">
        ///   <item><c>memory[retptr + 0..4]</c> = stream handle (i32 LE)</item>
        ///   <item><c>memory[retptr + 4..8]</c> = future handle (i32 LE)</item>
        /// </list>
        ///
        /// <para>Per canon-ABI: <c>retptr</c> must be 4-byte
        /// aligned and the 8-byte struct must fit within the
        /// memory bounds. Misaligned or out-of-bounds pointers
        /// throw <see cref="InvalidOperationException"/> with a
        /// diagnostic — the wasm caller's contract requires it
        /// to allocate the slot properly.</para>
        ///
        /// <para>Returns nothing — the future the
        /// <see cref="IStdin"/> impl produced fires when reading
        /// finishes; embedders observe completion through that
        /// future's await-able task.</para>
        /// </summary>
        public void InvokeReadViaStream(IStdin source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:cli/stdin.read-via-stream: dispatcher.Memory " +
                    "must be set before this import is invoked. Wire it " +
                    "from ComponentInstance after core-module " +
                    "instantiation.");

            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:cli/stdin.read-via-stream: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate an 8-byte 4-aligned return area.");

            var (streamHandle, futureHandle, _) =
                source.ReadViaStream(dispatcher);

            var dest = memory.AsSpan(retptr, 8);
            StreamMarshal.WriteHandle(dest, 0, streamHandle);
            FutureMarshal.WriteHandle(dest, 4, futureHandle);
        }

        private AsyncDispatcher RequireDispatcher()
        {
            return Dispatcher
                ?? throw new InvalidOperationException(
                    "WasiPreview3Host.Dispatcher must be set before " +
                    "the host's stdio imports are invoked. Set it from " +
                    "ComponentInstance.AsyncDispatcher after instantiation.");
        }

        /// <summary>Wire-level WASI module name for stdout.</summary>
        public const string StdoutModuleName =
            "wasi:cli/stdout@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stderr.</summary>
        public const string StderrModuleName =
            "wasi:cli/stderr@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stdin.</summary>
        public const string StdinModuleName =
            "wasi:cli/stdin@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the monotonic clock.</summary>
        public const string MonotonicClockModuleName =
            "wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the system clock.</summary>
        public const string SystemClockModuleName =
            "wasi:clocks/system-clock@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for cryptographic random.</summary>
        public const string RandomModuleName =
            "wasi:random/random@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for insecure random.</summary>
        public const string InsecureRandomModuleName =
            "wasi:random/insecure@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the insecure-seed
        /// 128-bit DoS-protection seed source.</summary>
        public const string InsecureSeedModuleName =
            "wasi:random/insecure-seed@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:http/types</c> interface (covers fields,
        /// request, response, request-options resources +
        /// shared types).</summary>
        public const string HttpTypesModuleName =
            "wasi:http/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for
        /// <c>wasi:http/client</c> (outbound HTTP).</summary>
        public const string HttpClientModuleName =
            "wasi:http/client@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for
        /// <c>wasi:http/handler</c> (inbound HTTP — guest
        /// provides this when serving).</summary>
        public const string HttpHandlerModuleName =
            "wasi:http/handler@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:filesystem/types</c> interface (descriptor
        /// resource + shared types).</summary>
        public const string FilesystemTypesModuleName =
            "wasi:filesystem/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:filesystem/preopens</c> interface
        /// (get-directories).</summary>
        public const string FilesystemPreopensModuleName =
            "wasi:filesystem/preopens@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:sockets/types</c> interface (tcp-socket +
        /// udp-socket resources + shared types).</summary>
        public const string SocketsTypesModuleName =
            "wasi:sockets/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:sockets/ip-name-lookup</c> interface.</summary>
        public const string IpNameLookupModuleName =
            "wasi:sockets/ip-name-lookup@0.3.0-rc-2026-03-15";
    }

    /// <summary>Fluent builder for <see cref="WasiPreview3Host"/>.</summary>
    public sealed class WasiPreview3HostBuilder
    {
        public IStdin? Stdin { get; set; }
        public IStdout? Stdout { get; set; }
        public IStderr? Stderr { get; set; }
        public IMonotonicClock? MonotonicClock { get; set; }
        public ISystemClock? SystemClock { get; set; }
        public IRandom? Random { get; set; }
        public IInsecure? InsecureRandom { get; set; }
        public IInsecureSeed? InsecureSeed { get; set; }
        public IPreopens? Preopens { get; set; }
        public IIpNameLookup? IpNameLookup { get; set; }
        public IClient? HttpClient { get; set; }
        public IHandler? HttpHandler { get; set; }
    }

    /// <summary>Ergonomic one-liner mirroring
    /// <c>UseWasiPreview2</c>.</summary>
    public static class WasiPreview3RuntimeExtensions
    {
        public static WasiPreview3Host UseWasiPreview3(
            this WasmRuntime runtime,
            Action<WasiPreview3HostBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiPreview3HostBuilder();
            configure?.Invoke(builder);
            var host = new WasiPreview3Host(builder);
            host.BindToRuntime(runtime);
            return host;
        }
    }
}
