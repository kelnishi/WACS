// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// End-to-end tests for the direct-linked WASI imports path.
    /// Each test transpiles a hand-crafted core wasm module that
    /// imports a single host function, supplies a host-package
    /// assembly carrying a <c>[WitSource]</c>-tagged interface that
    /// matches the import, and verifies that:
    ///
    /// 1. The transpiler resolves the import via
    ///    <see cref="HostPackageResolver"/>.
    /// 2. The generated module class accepts a bundle ctor param.
    /// 3. The emitted call-site IL bypasses the
    ///    <c>ImportDelegates</c> array entirely — the test plants a
    ///    stub <c>IImports</c> that <em>throws</em> if called, and
    ///    the export still returns the bundle's value cleanly.
    /// </summary>
    public class DirectLinkedImportTests
    {
        // ============== Test host-package surface ==============
        // PUBLIC so HostPackageResolver's GetExportedTypes() walk
        // sees them. The [WitSource] attribute is the contract — it
        // anchors the (Package, Interface) header that the resolver
        // rewrites into the wasm import wire-form module string.
        // The bundle is the typed aggregate that
        // ThinContext.HostBundle holds at runtime; the emitted IL
        // loads a property by interface-name convention (strip "I").

        [WitSource(@"interface env",
            Package = "my:test@1.0.0", Interface = "env")]
        public interface IEnv
        {
            [WitSource(@"get-value: func() -> u64;",
                Package = "my:test@1.0.0", Interface = "env",
                Item = "get-value")]
            ulong GetValue();

            // Exercises i32+i32 → i32 with NARROW CLR types on both
            // sides (uint param, byte param, returns int). Tests the
            // CONV emit path and the param-spill / re-push order.
            [WitSource(@"combine: func(a: u32, b: u8) -> s32;",
                Package = "my:test@1.0.0", Interface = "env",
                Item = "combine")]
            int Combine(uint a, byte b);
        }

        public sealed class TestBundle
        {
            public IEnv Env { get; }
            public TestBundle(IEnv env) { Env = env; }
        }

        // ====== Resource-method test surface ======================
        // [WitSource] Item="counter" anchors a resource interface;
        // its methods carry Item="counter.<method>" — the resolver
        // rewrites that to wire form `[method]counter.<method>`.

        [WitSource(@"interface res-env",
            Package = "my:test@1.0.0", Interface = "res-env")]
        public interface IResEnv
        {
            // Free function in the same interface — proves the
            // free + resource paths coexist in one module.
            [WitSource(@"banner: func() -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "banner")]
            uint Banner();
        }

        [WitSource(@"resource counter { tick: func() -> u32; }",
            Package = "my:test@1.0.0", Interface = "res-env",
            Item = "counter")]
        public interface ICounter
        {
            [WitSource(@"tick: func() -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "counter.tick")]
            uint Tick();
        }

        public sealed class ResBundle
        {
            public IResEnv ResEnv { get; }
            public ResBundle(IResEnv resEnv) { ResEnv = resEnv; }
        }

        // Convention-only resources class — exposes the
        // `object GetResource(Type, int)` and `int AllocateResource(Type, object)`
        // methods the DirectLinkedImportEmit looks up at IL-emit time.
        public sealed class TestResources
        {
            private readonly Dictionary<(Type, int), object>
                _table = new();
            private int _nextHandle = 1;

            public void Register(Type iface, int handle, object impl)
                => _table[(iface, handle)] = impl;

            public object GetResource(Type resourceInterface,
                int handle)
            {
                if (_table.TryGetValue((resourceInterface, handle),
                    out var impl)) return impl;
                throw new InvalidOperationException(
                    "no resource for "
                    + resourceInterface.Name + " handle " + handle);
            }

            public int AllocateResource(Type resourceInterface,
                object instance)
            {
                int h = _nextHandle++;
                _table[(resourceInterface, h)] = instance;
                return h;
            }
        }

        private sealed class FakeResEnv : IResEnv
        {
            public uint Banner() => 0xCAFEu;
        }

        private sealed class FakeCounter : ICounter
        {
            private uint _n;
            public FakeCounter(uint start) { _n = start; }
            public uint Tick() => ++_n;
        }

        // ====== Resource method with own<R> arg ===================
        // [method]X.foo whose typed CLR signature carries another
        // resource interface as a non-self arg. Exercises the
        // composition of the resource-method `this` lookup AND the
        // own<R> param lookup in one IL emit.

        [WitSource(@"resource sink { absorb: func(w: own<widget>) -> u32; }",
            Package = "my:test@1.0.0", Interface = "res-env",
            Item = "sink")]
        public interface ISink
        {
            [WitSource(@"absorb: func(w: own<widget>) -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "sink.absorb")]
            uint Absorb(IWidget widget);
        }

        public sealed class FakeSink : ISink
        {
            // Returns the widget's value so the test can assert
            // both `this` (FakeSink) and the own<R> (FakeWidget)
            // resolved correctly.
            public uint Absorb(IWidget widget) => widget.Read();
        }

        // ====== Resource method with string arg ==================
        // [method]X.foo whose typed CLR sig carries a string. Same
        // shape as wasi:io/streams.write — one of the most common
        // WASI patterns. Wire: (i32 thisHandle, i32 ptr, i32 len).

        [WitSource(@"resource logger { write: func(msg: string); }",
            Package = "my:test@1.0.0", Interface = "res-env",
            Item = "logger")]
        public interface ILogger
        {
            [WitSource(@"write: func(msg: string);",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "logger.write")]
            void Write(string msg);
        }

        public sealed class CapturingLogger : ILogger
        {
            public string? Captured { get; private set; }
            public void Write(string msg) { Captured = msg; }
        }

        // ====== Static + constructor resource method surface =====
        // Static interface methods are C# 8 default static interface
        // methods — supported on netstandard2.1 / LangVersion=9.

        [WitSource(@"resource widget { ... }",
            Package = "my:test@1.0.0", Interface = "res-env",
            Item = "widget")]
        public interface IWidget
        {
            [WitSource(@"read: func() -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "widget.read")]
            uint Read();

            // Static method on the resource (no `this`, no handle).
            // Wire form: [static]widget.default-value.
            [WitSource(@"default-value: static func() -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "[static]widget.default-value")]
            static uint DefaultValue() => 7u;

            // Multi-arg static method — covers the [static]X.foo
            // shape with > 0 primitive args. Wire: 2 i32 slots
            // (no `this`), returns a single i32 result.
            [WitSource(@"combine-static: static func(a: u32, b: u32) -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "[static]widget.combine-static")]
            static uint CombineStatic(uint a, uint b) => a + b * 100u;

            // Zero-arg constructor — wasm returns the i32 handle
            // for the newly-allocated instance. The factory body
            // mints a FakeWidget; the IL allocates a handle for
            // it via the resources class's AllocateResource.
            [WitSource(@"constructor();",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "[constructor]widget")]
            static IWidget Create() => new FakeWidget(99u);
        }

        public sealed class FakeWidget : IWidget
        {
            private readonly uint _v;
            public FakeWidget(uint v) { _v = v; }
            public uint Read() => _v;
        }

        // ====== Resource constructor with own<R> arg surface =====
        // [constructor]bag(seed: own<widget>) → bag — same shape as
        // wasi:http/types.outgoing-request(headers: own<fields>).
        // The factory takes a typed IWidget arg and returns a fresh
        // IBag instance; direct-linked emit lifts the widget handle,
        // calls the factory, then allocates a handle for the new
        // IBag instance.

        [WitSource(@"resource bag {
    constructor(seed: own<widget>);
    inspect: func() -> u32;
}",
            Package = "my:test@1.0.0", Interface = "res-env",
            Item = "bag")]
        public interface IBag
        {
            // Returns the seed widget's value so the test can probe
            // the constructor flow end-to-end.
            [WitSource(@"inspect: func() -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "bag.inspect")]
            uint Inspect();

            // Static factory: the typed CLR side takes IWidget;
            // the wasm wire takes its i32 handle. Direct-linked
            // emit lifts the handle via Resources.GetResource and
            // pushes the resolved IWidget for the factory call.
            [WitSource(@"constructor(seed: own<widget>);",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "[constructor]bag")]
            static IBag Create(IWidget seed) => new FakeBag(seed);
        }

        public sealed class FakeBag : IBag
        {
            private readonly IWidget _seed;
            public FakeBag(IWidget seed) { _seed = seed; }
            public uint Inspect() => _seed.Read();
        }

        // ====== HTTP-handle-style composition surface ============
        // Same shape as wasi:http/outgoing-handler.handle —
        // own<R> + option<own<R>> in one call. The combination
        // covers (a) resource-interface own<R> direct param,
        // (b) Option recursion into own<R>'s 1-slot wire form,
        // (c) total wasm wire of 1 + 2 = 3 slots.

        [WitSource(@"interface http-env",
            Package = "my:test@1.0.0", Interface = "http-env")]
        public interface IHandler
        {
            // Encode (request handle's value, options-present) back
            // as i32 so the test can probe both. Returns:
            //   (req.Read() << 1) | (opts.HasValue ? 1u : 0u)
            // — proves request resolved AND option branch fired.
            [WitSource(@"handle: func(req: own<widget>, opts: option<own<widget>>) -> u32;",
                Package = "my:test@1.0.0", Interface = "http-env",
                Item = "handle")]
            uint Handle(IWidget req, Option<IWidget> opts);
        }

        public sealed class HttpBundle
        {
            public IHandler HttpEnv { get; }
            public HttpBundle(IHandler h) { HttpEnv = h; }
        }

        private sealed class HttpProbe : IHandler
        {
            public uint Handle(IWidget req, Option<IWidget> opts)
                => (req.Read() << 1) | (opts.HasValue ? 1u : 0u);
        }

        // Bundle holds IWidget for the instance-method path; static
        // methods bypass the bundle entirely (called via direct
        // static dispatch on the interface type).
        public sealed class WidgetBundle
        {
            public IWidget Widget { get; }
            public WidgetBundle(IWidget widget) { Widget = widget; }
        }

        // ====== String-param test surface ========================

        [WitSource(@"interface str-env",
            Package = "my:test@1.0.0", Interface = "str-env")]
        public interface IPrinter
        {
            [WitSource(@"print: func(msg: string);",
                Package = "my:test@1.0.0", Interface = "str-env",
                Item = "print")]
            void Print(string msg);
        }

        public sealed class StringBundle
        {
            public IPrinter StrEnv { get; }
            public StringBundle(IPrinter strEnv) { StrEnv = strEnv; }
        }

        private sealed class CapturingPrinter : IPrinter
        {
            public string? Captured { get; private set; }
            public void Print(string msg) { Captured = msg; }
        }

        // ====== byte[] (list<u8>) param test surface =============

        [WitSource(@"interface byte-env",
            Package = "my:test@1.0.0", Interface = "byte-env")]
        public interface IBytePrinter
        {
            [WitSource(@"print-bytes: func(data: list<u8>);",
                Package = "my:test@1.0.0", Interface = "byte-env",
                Item = "print-bytes")]
            void PrintBytes(byte[] data);
        }

        public sealed class ByteBundle
        {
            public IBytePrinter ByteEnv { get; }
            public ByteBundle(IBytePrinter byteEnv)
            { ByteEnv = byteEnv; }
        }

        private sealed class CapturingBytePrinter : IBytePrinter
        {
            public byte[]? Captured { get; private set; }
            public void PrintBytes(byte[] data) { Captured = data; }
        }

        // ====== int[] (list<u32>) param test surface =============

        [WitSource(@"interface int-env",
            Package = "my:test@1.0.0", Interface = "int-env")]
        public interface IIntPrinter
        {
            [WitSource(@"print-ints: func(data: list<u32>);",
                Package = "my:test@1.0.0", Interface = "int-env",
                Item = "print-ints")]
            void PrintInts(int[] data);
        }

        public sealed class IntBundle
        {
            public IIntPrinter IntEnv { get; }
            public IntBundle(IIntPrinter intEnv) { IntEnv = intEnv; }
        }

        private sealed class CapturingIntPrinter : IIntPrinter
        {
            public int[]? Captured { get; private set; }
            public void PrintInts(int[] data) { Captured = data; }
        }

        // ====== list<string> (string[]) param test surface ======
        // wasi:cli/environment.get-arguments style — list of UTF-8
        // strings via ListMarshal.LiftStringList.

        [WitSource(@"interface strs-env",
            Package = "my:test@1.0.0", Interface = "strs-env")]
        public interface IStringsTaker
        {
            [WitSource(@"take-strs: func(items: list<string>);",
                Package = "my:test@1.0.0", Interface = "strs-env",
                Item = "take-strs")]
            void TakeStrs(string[] items);
        }

        public sealed class StringsBundle
        {
            public IStringsTaker StrsEnv { get; }
            public StringsBundle(IStringsTaker s) { StrsEnv = s; }
        }

        private sealed class CapturingStrings : IStringsTaker
        {
            public string[]? Captured { get; private set; }
            public void TakeStrs(string[] items) { Captured = items; }
        }

        // ====== Aggregate-return surface =========================
        // Wasi:clocks/wall-clock.now() → datetime is the canonical
        // shape: a record { u64 seconds, u32 nanoseconds }. The
        // wasm sig adds a trailing i32 retArea ptr param and has
        // 0 returns; the host writes the record fields into memory
        // at retAreaPtr (12 bytes total).

        [WitSource(@"record dt { seconds: u64, nanoseconds: u32 }",
            Package = "my:test@1.0.0", Interface = "agg-env",
            Item = "dt")]
        public sealed class Dt
        {
            public ulong Seconds { get; set; } = default!;
            public uint Nanoseconds { get; set; } = default!;
        }

        [WitSource(@"interface agg-env",
            Package = "my:test@1.0.0", Interface = "agg-env")]
        public interface IClock
        {
            [WitSource(@"now: func() -> dt;",
                Package = "my:test@1.0.0", Interface = "agg-env",
                Item = "now")]
            Dt Now();
        }

        public sealed class ClockBundle
        {
            public IClock AggEnv { get; }
            public ClockBundle(IClock c) { AggEnv = c; }
        }

        private sealed class FixedClock2 : IClock
        {
            private readonly Dt _dt;
            public FixedClock2(Dt dt) { _dt = dt; }
            public Dt Now() => _dt;
        }

        // ====== Aggregate-return: ValueTuple shape ===============

        [WitSource(@"interface tup-ret-env",
            Package = "my:test@1.0.0", Interface = "tup-ret-env")]
        public interface IPairFactory
        {
            [WitSource(@"make-pair: func() -> tuple<u32, u32>;",
                Package = "my:test@1.0.0", Interface = "tup-ret-env",
                Item = "make-pair")]
            (uint, uint) MakePair();
        }

        public sealed class PairBundle
        {
            public IPairFactory TupRetEnv { get; }
            public PairBundle(IPairFactory p) { TupRetEnv = p; }
        }

        private sealed class FixedPair : IPairFactory
        {
            private readonly (uint, uint) _v;
            public FixedPair((uint, uint) v) { _v = v; }
            public (uint, uint) MakePair() => _v;
        }

        // ====== own<R> return shape ==============================
        // Free function returning a resource interface — same shape
        // as wasi:cli/stdout.get-stdout() -> own<output-stream>.
        // Wasm wire is 1 i32 (the handle); IL allocates a fresh
        // handle for the returned instance.

        [WitSource(@"interface own-ret-env",
            Package = "my:test@1.0.0", Interface = "own-ret-env")]
        public interface IWidgetFactory
        {
            [WitSource(@"make-widget: func() -> own<widget>;",
                Package = "my:test@1.0.0", Interface = "own-ret-env",
                Item = "make-widget")]
            IWidget MakeWidget();
        }

        public sealed class FactoryBundle
        {
            public IWidgetFactory OwnRetEnv { get; }
            public FactoryBundle(IWidgetFactory f) { OwnRetEnv = f; }
        }

        private sealed class WidgetMaker : IWidgetFactory
        {
            private readonly uint _seed;
            public WidgetMaker(uint seed) { _seed = seed; }
            public IWidget MakeWidget() => new FakeWidget(_seed);
        }

        // ====== Option<primitive> return shape ===================
        // wasi:cli/terminal-stdin.get-terminal-stdin() -> option<X>
        // is the canonical shape; we test against the simpler
        // option<u32> form to focus on the disc + value layout.

        [WitSource(@"interface optret-env",
            Package = "my:test@1.0.0", Interface = "optret-env")]
        public interface IMaybeFactory
        {
            [WitSource(@"maybe: func() -> option<u32>;",
                Package = "my:test@1.0.0", Interface = "optret-env",
                Item = "maybe")]
            Option<uint> Maybe();
        }

        public sealed class MaybeBundle
        {
            public IMaybeFactory OptretEnv { get; }
            public MaybeBundle(IMaybeFactory m) { OptretEnv = m; }
        }

        private sealed class FixedMaybe : IMaybeFactory
        {
            private readonly Option<uint> _v;
            public FixedMaybe(Option<uint> v) { _v = v; }
            public Option<uint> Maybe() => _v;
        }

        // ====== Result<TOk, TErr> return shape ===================
        // Wire form: u8 disc @0 (0=Ok, 1=Err) + value at offset
        // Align(1, max(align(Ok), align(Err))). For
        // result<u32, u32>: 8 bytes (disc@0, value@4).

        [WitSource(@"interface resret-env",
            Package = "my:test@1.0.0", Interface = "resret-env")]
        public interface IResultFactory
        {
            [WitSource(@"compute: func() -> result<u32, u32>;",
                Package = "my:test@1.0.0", Interface = "resret-env",
                Item = "compute")]
            Result<uint, uint> Compute();
        }

        public sealed class ResultRetBundle
        {
            public IResultFactory ResretEnv { get; }
            public ResultRetBundle(IResultFactory r)
            { ResretEnv = r; }
        }

        private sealed class FixedResult : IResultFactory
        {
            private readonly Result<uint, uint> _v;
            public FixedResult(Result<uint, uint> v) { _v = v; }
            public Result<uint, uint> Compute() => _v;
        }

        // ====== Option<own<R>> return shape ======================
        // Same shape as wasi:cli/terminal-stdin.get-terminal-stdin
        // -> option<own<terminal-input>>. The Some path allocates
        // a handle for the inner instance via Resources; the None
        // path writes disc=0.

        [WitSource(@"interface optown-env",
            Package = "my:test@1.0.0", Interface = "optown-env")]
        public interface IMaybeWidget
        {
            [WitSource(@"maybe-widget: func() -> option<own<widget>>;",
                Package = "my:test@1.0.0", Interface = "optown-env",
                Item = "maybe-widget")]
            Option<IWidget> MaybeWidget();
        }

        public sealed class MaybeWidgetBundle
        {
            public IMaybeWidget OptownEnv { get; }
            public MaybeWidgetBundle(IMaybeWidget m)
            { OptownEnv = m; }
        }

        private sealed class FixedMaybeWidget : IMaybeWidget
        {
            private readonly Option<IWidget> _v;
            public FixedMaybeWidget(Option<IWidget> v) { _v = v; }
            public Option<IWidget> MaybeWidget() => _v;
        }

        // ====== Result<own<R>, primitive> return shape ===========
        // Same shape as wasi:sockets/tcp-create-socket
        // -> result<own<tcp-socket>, error-code>. The Ok side
        // allocates a handle from the resource instance; the Err
        // side stores the primitive error code.

        [WitSource(@"interface socket-env",
            Package = "my:test@1.0.0", Interface = "socket-env")]
        public interface ISocketFactory
        {
            [WitSource(@"create: func() -> result<own<widget>, u32>;",
                Package = "my:test@1.0.0", Interface = "socket-env",
                Item = "create")]
            Result<IWidget, uint> Create();
        }

        public sealed class SocketBundle
        {
            public ISocketFactory SocketEnv { get; }
            public SocketBundle(ISocketFactory s) { SocketEnv = s; }
        }

        private sealed class FixedSocket : ISocketFactory
        {
            private readonly Result<IWidget, uint> _v;
            public FixedSocket(Result<IWidget, uint> v) { _v = v; }
            public Result<IWidget, uint> Create() => _v;
        }

        // ====== Variant return (all empty cases) =================
        // Wire form: u8 disc (no payloads in v0). IL chains
        // isinst checks against each sealed case to determine the
        // disc to write at retArea+0.

        [WitSource(@"variant tag { foo, bar, baz }",
            Package = "my:test@1.0.0", Interface = "varret-env",
            Item = "tag")]
        public abstract class Tag
        {
            public sealed class TagFoo : Tag { }
            public sealed class TagBar : Tag { }
            public sealed class TagBaz : Tag { }
        }

        [WitSource(@"interface varret-env",
            Package = "my:test@1.0.0", Interface = "varret-env")]
        public interface ITagFactory
        {
            [WitSource(@"pick: func() -> tag;",
                Package = "my:test@1.0.0", Interface = "varret-env",
                Item = "pick")]
            Tag Pick();
        }

        public sealed class TagBundle
        {
            public ITagFactory VarretEnv { get; }
            public TagBundle(ITagFactory t) { VarretEnv = t; }
        }

        private sealed class FixedTag : ITagFactory
        {
            private readonly Tag _v;
            public FixedTag(Tag v) { _v = v; }
            public Tag Pick() => _v;
        }

        // ====== Variant return with payload-bearing cases ========
        // variant code { ok, count(u32), max(u32) }: 1 disc + 1
        // u32 joined-flat payload slot (4 bytes payload aligned at
        // offset 4, so total retArea = 8 bytes).

        [WitSource(@"variant code { ok, count(u32), max(u32) }",
            Package = "my:test@1.0.0", Interface = "varret-env",
            Item = "code")]
        public abstract class Code
        {
            public sealed class CodeOk : Code { }
            public sealed class CodeCount : Code
            {
                public uint Value { get; }
                public CodeCount(uint v) { Value = v; }
            }
            public sealed class CodeMax : Code
            {
                public uint Value { get; }
                public CodeMax(uint v) { Value = v; }
            }
        }

        [WitSource(@"interface codeFactory",
            Package = "my:test@1.0.0", Interface = "codeFactory")]
        public interface ICodeFactory
        {
            [WitSource(@"emit: func() -> code;",
                Package = "my:test@1.0.0", Interface = "codeFactory",
                Item = "emit")]
            Code Emit();
        }

        public sealed class CodeBundle
        {
            public ICodeFactory CodeFactory { get; }
            public CodeBundle(ICodeFactory c) { CodeFactory = c; }
        }

        private sealed class FixedCode : ICodeFactory
        {
            private readonly Code _v;
            public FixedCode(Code v) { _v = v; }
            public Code Emit() => _v;
        }

        // ====== Variant return: mixed primitive payload widths ===
        // variant event { tap, beep(u8), tone(u32) }: payloads have
        // different widths (1 byte vs 4 bytes). canon-ABI joined-
        // flat reserves max(align(payload)) for the slot offset and
        // max(size(payload)) for the slot size; each case writes
        // its own width starting at retArea+4.

        [WitSource(@"variant event { tap, beep(u8), tone(u32) }",
            Package = "my:test@1.0.0", Interface = "evtret-env",
            Item = "event")]
        public abstract class Evt
        {
            public sealed class EvtTap : Evt { }
            public sealed class EvtBeep : Evt
            {
                public byte Value { get; }
                public EvtBeep(byte v) { Value = v; }
            }
            public sealed class EvtTone : Evt
            {
                public uint Value { get; }
                public EvtTone(uint v) { Value = v; }
            }
        }

        [WitSource(@"interface evtret-env",
            Package = "my:test@1.0.0", Interface = "evtret-env")]
        public interface IEvtFactory
        {
            [WitSource(@"fire: func() -> event;",
                Package = "my:test@1.0.0", Interface = "evtret-env",
                Item = "fire")]
            Evt Fire();
        }

        public sealed class EvtBundle
        {
            public IEvtFactory EvtretEnv { get; }
            public EvtBundle(IEvtFactory e) { EvtretEnv = e; }
        }

        private sealed class FixedEvt : IEvtFactory
        {
            private readonly Evt _v;
            public FixedEvt(Evt v) { _v = v; }
            public Evt Fire() => _v;
        }

        // ====== Option<tuple<u32, u32>> return ===================
        // Wire form: u8 disc @0 + (i32 a, i32 b) at retArea+4.
        // Some path: disc=1, a@4, b@8. None path: disc=0.

        [WitSource(@"interface opttup-env",
            Package = "my:test@1.0.0", Interface = "opttup-env")]
        public interface IMaybePair
        {
            [WitSource(@"pair: func() -> option<tuple<u32, u32>>;",
                Package = "my:test@1.0.0", Interface = "opttup-env",
                Item = "pair")]
            Option<(uint, uint)> Pair();
        }

        public sealed class MaybePairBundle
        {
            public IMaybePair OpttupEnv { get; }
            public MaybePairBundle(IMaybePair m)
            { OpttupEnv = m; }
        }

        private sealed class FixedMaybePair : IMaybePair
        {
            private readonly Option<(uint, uint)> _v;
            public FixedMaybePair(Option<(uint, uint)> v) { _v = v; }
            public Option<(uint, uint)> Pair() => _v;
        }

        // ====== Option<record-of-primitives> return ==============
        // Wire form: u8 disc @0 + record fields at retArea+4
        // (per-field offsets per canon-ABI alignment rules).

        public sealed class XYPair
        {
            public uint X { get; set; }
            public uint Y { get; set; }
        }

        [WitSource(@"interface optrec-env",
            Package = "my:test@1.0.0", Interface = "optrec-env")]
        public interface IMaybeXY
        {
            [WitSource(@"xy: func() -> option<xy-pair>;",
                Package = "my:test@1.0.0", Interface = "optrec-env",
                Item = "xy")]
            Option<XYPair> Xy();
        }

        public sealed class MaybeXYBundle
        {
            public IMaybeXY OptrecEnv { get; }
            public MaybeXYBundle(IMaybeXY m)
            { OptrecEnv = m; }
        }

        private sealed class FixedMaybeXY : IMaybeXY
        {
            private readonly Option<XYPair> _v;
            public FixedMaybeXY(Option<XYPair> v) { _v = v; }
            public Option<XYPair> Xy() => _v;
        }

        // ====== Option<list<u32>> (Option<int[]>) return =========
        // Wire form: u8 disc @0 + (i32 ptr @4, i32 count @8). Some
        // path: cabi_realloc + memcpy + write disc=1 + (ptr, count).

        [WitSource(@"interface optintl-env",
            Package = "my:test@1.0.0", Interface = "optintl-env")]
        public interface IMaybeInts
        {
            [WitSource(@"list: func() -> option<list<s32>>;",
                Package = "my:test@1.0.0", Interface = "optintl-env",
                Item = "list")]
            Option<int[]> List();
        }

        public sealed class MaybeIntsBundle
        {
            public IMaybeInts OptintlEnv { get; }
            public MaybeIntsBundle(IMaybeInts m)
            { OptintlEnv = m; }
        }

        private sealed class FixedMaybeInts : IMaybeInts
        {
            private readonly Option<int[]> _v;
            public FixedMaybeInts(Option<int[]> v) { _v = v; }
            public Option<int[]> List() => _v;
        }

        // ====== Result<Option<u32>, u32> nested return ===========
        // Wire form: u8 outer_disc @0 + (u8 opt_disc @4 + u32
        // value @8) | (u32 err_value @4). Recursive helper handles
        // the Ok arm's Option write.

        [WitSource(@"interface resopt-env",
            Package = "my:test@1.0.0", Interface = "resopt-env")]
        public interface IResultOptionNum
        {
            [WitSource(@"go: func() -> result<option<u32>, u32>;",
                Package = "my:test@1.0.0", Interface = "resopt-env",
                Item = "go")]
            Result<Option<uint>, uint> Go();
        }

        public sealed class ResultOptionNumBundle
        {
            public IResultOptionNum ResoptoptEnv { get; }
            public ResultOptionNumBundle(IResultOptionNum r)
            { ResoptoptEnv = r; }
        }

        private sealed class FixedResultOptionNum : IResultOptionNum
        {
            private readonly Result<Option<uint>, uint> _v;
            public FixedResultOptionNum(Result<Option<uint>, uint> v)
            { _v = v; }
            public Result<Option<uint>, uint> Go() => _v;
        }

        // ====== Option<Option<u32>> nested return ================
        // Wire form: u8 outer_disc @0 + (u8 inner_disc @4 + u32
        // inner_value @8). The recursive helper handles both
        // levels.

        [WitSource(@"interface optopt-env",
            Package = "my:test@1.0.0", Interface = "optopt-env")]
        public interface IMaybeMaybeNum
        {
            [WitSource(@"num: func() -> option<option<u32>>;",
                Package = "my:test@1.0.0", Interface = "optopt-env",
                Item = "num")]
            Option<Option<uint>> Num();
        }

        public sealed class MaybeMaybeNumBundle
        {
            public IMaybeMaybeNum OptoptEnv { get; }
            public MaybeMaybeNumBundle(IMaybeMaybeNum m)
            { OptoptEnv = m; }
        }

        private sealed class FixedMaybeMaybeNum : IMaybeMaybeNum
        {
            private readonly Option<Option<uint>> _v;
            public FixedMaybeMaybeNum(Option<Option<uint>> v) { _v = v; }
            public Option<Option<uint>> Num() => _v;
        }

        // ====== list<own<R>> (IWidget[]) return =================
        // Per-element AllocateResource mints a handle for each
        // resource instance; the outer (ptr, count) lands at retArea
        // and the packed handle buffer at *(ptr).

        [WitSource(@"interface listown-env",
            Package = "my:test@1.0.0", Interface = "listown-env")]
        public interface IWidgetListSource
        {
            [WitSource(@"all: func() -> list<own<widget>>;",
                Package = "my:test@1.0.0", Interface = "listown-env",
                Item = "all")]
            IWidget[] All();
        }

        public sealed class WidgetListBundle
        {
            public IWidgetListSource ListownEnv { get; }
            public WidgetListBundle(IWidgetListSource s)
            { ListownEnv = s; }
        }

        private sealed class FixedWidgetList : IWidgetListSource
        {
            private readonly IWidget[] _v;
            public FixedWidgetList(IWidget[] v) { _v = v; }
            public IWidget[] All() => _v;
        }

        // ====== list<list<s32>> (int[][]) return ===============
        // Wire form: outer (i32 ptr, i32 count) + inner array of
        // (sub_ptr, sub_len) pairs, each sub_ptr pointing to a
        // packed i32 buffer. Same shape as byte[][] but per-element
        // size = 4 (i32) instead of 1 (u8).

        [WitSource(@"interface listil-env",
            Package = "my:test@1.0.0", Interface = "listil-env")]
        public interface IIntListListSource
        {
            [WitSource(@"all: func() -> list<list<s32>>;",
                Package = "my:test@1.0.0", Interface = "listil-env",
                Item = "all")]
            int[][] All();
        }

        public sealed class IntListListBundle
        {
            public IIntListListSource ListilEnv { get; }
            public IntListListBundle(IIntListListSource s)
            { ListilEnv = s; }
        }

        private sealed class FixedIntListList : IIntListListSource
        {
            private readonly int[][] _v;
            public FixedIntListList(int[][] v) { _v = v; }
            public int[][] All() => _v;
        }

        // ====== list<list<u8>> (byte[][]) return ===============
        // Wire form: outer (i32 ptr, i32 count) + inner array of
        // (sub_ptr, sub_len) pairs at *(ptr) + raw byte buffers at
        // each *(sub_ptr). Mirrors realistic shapes like HTTP body
        // chunks.

        [WitSource(@"interface listbyt-env",
            Package = "my:test@1.0.0", Interface = "listbyt-env")]
        public interface IByteListSource
        {
            [WitSource(@"all: func() -> list<list<u8>>;",
                Package = "my:test@1.0.0", Interface = "listbyt-env",
                Item = "all")]
            byte[][] All();
        }

        public sealed class ByteListBundle
        {
            public IByteListSource ListbytEnv { get; }
            public ByteListBundle(IByteListSource s)
            { ListbytEnv = s; }
        }

        private sealed class FixedByteList : IByteListSource
        {
            private readonly byte[][] _v;
            public FixedByteList(byte[][] v) { _v = v; }
            public byte[][] All() => _v;
        }

        // ====== list<tuple<u32, u32>> return =====================
        // Wire form: outer (i32 ptr, i32 count) at retArea + a
        // contiguous packed array of tuple<u32, u32> elements
        // (each 8 bytes) at *(ptr). Mirrors realistic shapes like
        // wasi:http/types.list-of-tuples.

        [WitSource(@"interface listtup-env",
            Package = "my:test@1.0.0", Interface = "listtup-env")]
        public interface IPairListSource
        {
            [WitSource(@"all: func() -> list<tuple<u32, u32>>;",
                Package = "my:test@1.0.0", Interface = "listtup-env",
                Item = "all")]
            (uint, uint)[] All();
        }

        public sealed class PairListBundle
        {
            public IPairListSource ListtupEnv { get; }
            public PairListBundle(IPairListSource s)
            { ListtupEnv = s; }
        }

        private sealed class FixedPairList : IPairListSource
        {
            private readonly (uint, uint)[] _v;
            public FixedPairList((uint, uint)[] v) { _v = v; }
            public (uint, uint)[] All() => _v;
        }

        // ====== Variant with Result payload ====================
        // variant outcome { okay, fail(result<u32, u32>) }: empty
        // + nested Result payload. Variant emit dispatches the fail
        // case through EmitResultStoreAt at retArea+valueOffset.

        [WitSource(@"variant outcome { okay, fail(result<u32, u32>) }",
            Package = "my:test@1.0.0", Interface = "varres-env",
            Item = "outcome")]
        public abstract class Outcome
        {
            public sealed class OutcomeOkay : Outcome { }
            public sealed class OutcomeFail : Outcome
            {
                public Result<uint, uint> Value { get; }
                public OutcomeFail(Result<uint, uint> v) { Value = v; }
            }
        }

        [WitSource(@"interface varres-env",
            Package = "my:test@1.0.0", Interface = "varres-env")]
        public interface IOutcomeFactory
        {
            [WitSource(@"emit: func() -> outcome;",
                Package = "my:test@1.0.0", Interface = "varres-env",
                Item = "emit")]
            Outcome Emit();
        }

        public sealed class OutcomeBundle
        {
            public IOutcomeFactory VarresEnv { get; }
            public OutcomeBundle(IOutcomeFactory o) { VarresEnv = o; }
        }

        private sealed class FixedOutcome : IOutcomeFactory
        {
            private readonly Outcome _v;
            public FixedOutcome(Outcome v) { _v = v; }
            public Outcome Emit() => _v;
        }

        // ====== Variant with Option payload ====================
        // variant ping { idle, retry(option<u32>) }: empty + nested
        // Option payload. Variant emit dispatches the retry case
        // through EmitOptionStoreAt at retArea+valueOffset.

        [WitSource(@"variant ping { idle, retry(option<u32>) }",
            Package = "my:test@1.0.0", Interface = "varopt-env",
            Item = "ping")]
        public abstract class Ping
        {
            public sealed class PingIdle : Ping { }
            public sealed class PingRetry : Ping
            {
                public Option<uint> Value { get; }
                public PingRetry(Option<uint> v) { Value = v; }
            }
        }

        [WitSource(@"interface varopt-env",
            Package = "my:test@1.0.0", Interface = "varopt-env")]
        public interface IPingFactory
        {
            [WitSource(@"emit: func() -> ping;",
                Package = "my:test@1.0.0", Interface = "varopt-env",
                Item = "emit")]
            Ping Emit();
        }

        public sealed class PingBundle
        {
            public IPingFactory VaroptEnv { get; }
            public PingBundle(IPingFactory p) { VaroptEnv = p; }
        }

        private sealed class FixedPing : IPingFactory
        {
            private readonly Ping _v;
            public FixedPing(Ping v) { _v = v; }
            public Ping Emit() => _v;
        }

        // ====== Variant with tuple payload =====================
        // variant pos { home, point(tuple<u32, u32>) } — tap +
        // tuple-bearing case. valueOffset=4, payload tuple at
        // retArea+4..retArea+11.

        [WitSource(@"variant pos { home, point(tuple<u32, u32>) }",
            Package = "my:test@1.0.0", Interface = "varpos-env",
            Item = "pos")]
        public abstract class Pos
        {
            public sealed class PosHome : Pos { }
            public sealed class PosPoint : Pos
            {
                public (uint, uint) Value { get; }
                public PosPoint((uint, uint) v) { Value = v; }
            }
        }

        [WitSource(@"interface varpos-env",
            Package = "my:test@1.0.0", Interface = "varpos-env")]
        public interface IPosFactory
        {
            [WitSource(@"emit: func() -> pos;",
                Package = "my:test@1.0.0", Interface = "varpos-env",
                Item = "emit")]
            Pos Emit();
        }

        public sealed class PosBundle
        {
            public IPosFactory VarposEnv { get; }
            public PosBundle(IPosFactory p) { VarposEnv = p; }
        }

        private sealed class FixedPos : IPosFactory
        {
            private readonly Pos _v;
            public FixedPos(Pos v) { _v = v; }
            public Pos Emit() => _v;
        }

        // ====== Result<tuple<u32,u32>, primitive> return =========

        [WitSource(@"interface restup-env",
            Package = "my:test@1.0.0", Interface = "restup-env")]
        public interface IPairResult
        {
            [WitSource(@"go: func() -> result<tuple<u32, u32>, u32>;",
                Package = "my:test@1.0.0", Interface = "restup-env",
                Item = "go")]
            Result<(uint, uint), uint> Go();
        }

        public sealed class PairResultBundle
        {
            public IPairResult RestupEnv { get; }
            public PairResultBundle(IPairResult r) { RestupEnv = r; }
        }

        private sealed class FixedPairResult : IPairResult
        {
            private readonly Result<(uint, uint), uint> _v;
            public FixedPairResult(Result<(uint, uint), uint> v) { _v = v; }
            public Result<(uint, uint), uint> Go() => _v;
        }

        // ====== Variant return: string-bearing payload =========
        // variant note { silent, label(string) }: empty + string
        // payload. canon-ABI joined-flat reserves max(align)=4 and
        // max(size)=8 (ptr+len) at retArea+4. The label arm goes
        // through cabi_realloc + StoreString (same as Result<string,
        // X> Ok arm).

        [WitSource(@"variant note { silent, label(string) }",
            Package = "my:test@1.0.0", Interface = "noteret-env",
            Item = "note")]
        public abstract class Note
        {
            public sealed class NoteSilent : Note { }
            public sealed class NoteLabel : Note
            {
                public string Value { get; }
                public NoteLabel(string v) { Value = v; }
            }
        }

        [WitSource(@"interface noteret-env",
            Package = "my:test@1.0.0", Interface = "noteret-env")]
        public interface INoteFactory
        {
            [WitSource(@"sound: func() -> note;",
                Package = "my:test@1.0.0", Interface = "noteret-env",
                Item = "sound")]
            Note Sound();
        }

        public sealed class NoteBundle
        {
            public INoteFactory NoteretEnv { get; }
            public NoteBundle(INoteFactory n) { NoteretEnv = n; }
        }

        private sealed class FixedNote : INoteFactory
        {
            private readonly Note _v;
            public FixedNote(Note v) { _v = v; }
            public Note Sound() => _v;
        }

        // ====== String return shape (via cabi_realloc) ===========
        // Common WASI shape — the host returns a string, the
        // direct-linked emit calls the guest's cabi_realloc to
        // allocate a buffer, copies the UTF-8 bytes, and writes
        // (ptr, len) at retArea. Same shape as
        // wasi:cli/environment.get-cwd or any string-returning
        // accessor.

        [WitSource(@"interface str-ret-env",
            Package = "my:test@1.0.0", Interface = "str-ret-env")]
        public interface IGreeter
        {
            [WitSource(@"greet: func() -> string;",
                Package = "my:test@1.0.0", Interface = "str-ret-env",
                Item = "greet")]
            string Greet();
        }

        public sealed class GreetBundle
        {
            public IGreeter StrRetEnv { get; }
            public GreetBundle(IGreeter g) { StrRetEnv = g; }
        }

        private sealed class FixedGreeter : IGreeter
        {
            private readonly string _v;
            public FixedGreeter(string v) { _v = v; }
            public string Greet() => _v;
        }

        // ====== byte[] return shape (via cabi_realloc) ===========

        [WitSource(@"interface bytes-ret-env",
            Package = "my:test@1.0.0", Interface = "bytes-ret-env")]
        public interface IByteSource
        {
            [WitSource(@"give: func() -> list<u8>;",
                Package = "my:test@1.0.0", Interface = "bytes-ret-env",
                Item = "give")]
            byte[] Give();
        }

        public sealed class ByteSourceBundle
        {
            public IByteSource BytesRetEnv { get; }
            public ByteSourceBundle(IByteSource g)
            { BytesRetEnv = g; }
        }

        private sealed class FixedByteSource : IByteSource
        {
            private readonly byte[] _v;
            public FixedByteSource(byte[] v) { _v = v; }
            public byte[] Give() => _v;
        }

        // ====== string[] return shape (via cabi_realloc) =========
        // Wire form: i32 ptr @0 + i32 count @4. *(ptr) is an array
        // of (i32 ptr, i32 len) pairs (8 bytes per element); each
        // string ptr points to a UTF-8 buffer. Two-level allocation
        // — outer once + inner once per string.

        [WitSource(@"interface strs-ret-env",
            Package = "my:test@1.0.0", Interface = "strs-ret-env")]
        public interface IStringSource
        {
            [WitSource(@"give: func() -> list<string>;",
                Package = "my:test@1.0.0", Interface = "strs-ret-env",
                Item = "give")]
            string[] Give();
        }

        public sealed class StringSourceBundle
        {
            public IStringSource StrsRetEnv { get; }
            public StringSourceBundle(IStringSource g)
            { StrsRetEnv = g; }
        }

        private sealed class FixedStringSource : IStringSource
        {
            private readonly string[] _v;
            public FixedStringSource(string[] v) { _v = v; }
            public string[] Give() => _v;
        }

        // ====== bool[] return shape (via cabi_realloc) ===========
        // Wire form: i32 ptr + i32 count + packed u8 bytes (0/1).
        // Same machinery as byte[] return — bool is 1-byte and
        // MemoryMarshal.AsBytes<bool> round-trips canonically.

        [WitSource(@"interface boolarr-env",
            Package = "my:test@1.0.0", Interface = "boolarr-env")]
        public interface IBoolSource
        {
            [WitSource(@"give: func() -> list<bool>;",
                Package = "my:test@1.0.0", Interface = "boolarr-env",
                Item = "give")]
            bool[] Give();
        }

        public sealed class BoolSourceBundle
        {
            public IBoolSource BoolarrEnv { get; }
            public BoolSourceBundle(IBoolSource g)
            { BoolarrEnv = g; }
        }

        private sealed class FixedBoolSource : IBoolSource
        {
            private readonly bool[] _v;
            public FixedBoolSource(bool[] v) { _v = v; }
            public bool[] Give() => _v;
        }

        // ====== int[] return shape (via cabi_realloc) ============
        // Wire form: i32 ptr @0 + i32 count @4 (count = element
        // count, NOT byte count). cabi_realloc gets element-aligned
        // (align=4 for int) and byte_count = count * 4.

        [WitSource(@"interface ints-ret-env",
            Package = "my:test@1.0.0", Interface = "ints-ret-env")]
        public interface IIntSource
        {
            [WitSource(@"give: func() -> list<s32>;",
                Package = "my:test@1.0.0", Interface = "ints-ret-env",
                Item = "give")]
            int[] Give();
        }

        public sealed class IntSourceBundle
        {
            public IIntSource IntsRetEnv { get; }
            public IntSourceBundle(IIntSource g)
            { IntsRetEnv = g; }
        }

        private sealed class FixedIntSource : IIntSource
        {
            private readonly int[] _v;
            public FixedIntSource(int[] v) { _v = v; }
            public int[] Give() => _v;
        }

        // ====== long[] return shape (via cabi_realloc) ===========
        // Wire form: i32 ptr @0 + i32 count @4 (count = element
        // count). cabi_realloc gets align=8, byte_count = count * 8.

        [WitSource(@"interface longs-ret-env",
            Package = "my:test@1.0.0", Interface = "longs-ret-env")]
        public interface ILongSource
        {
            [WitSource(@"give: func() -> list<s64>;",
                Package = "my:test@1.0.0", Interface = "longs-ret-env",
                Item = "give")]
            long[] Give();
        }

        public sealed class LongSourceBundle
        {
            public ILongSource LongsRetEnv { get; }
            public LongSourceBundle(ILongSource g)
            { LongsRetEnv = g; }
        }

        private sealed class FixedLongSource : ILongSource
        {
            private readonly long[] _v;
            public FixedLongSource(long[] v) { _v = v; }
            public long[] Give() => _v;
        }

        // ====== Option<string> return ============================
        // Wire form: u8 disc @0 + (i32 ptr, i32 len) at retArea+4.
        // Some path: cabi_realloc + memcpy + write disc=1 + (ptr, len).
        // None path: write disc=0; ptr/len slots stay default.

        [WitSource(@"interface optstr-ret-env",
            Package = "my:test@1.0.0", Interface = "optstr-ret-env")]
        public interface IMaybeName
        {
            [WitSource(@"name: func() -> option<string>;",
                Package = "my:test@1.0.0", Interface = "optstr-ret-env",
                Item = "name")]
            Option<string> Name();
        }

        public sealed class MaybeNameBundle
        {
            public IMaybeName OptstrRetEnv { get; }
            public MaybeNameBundle(IMaybeName m)
            { OptstrRetEnv = m; }
        }

        private sealed class FixedMaybeName : IMaybeName
        {
            private readonly Option<string> _v;
            public FixedMaybeName(Option<string> v) { _v = v; }
            public Option<string> Name() => _v;
        }

        // ====== Result<string, primitive> return =================
        // Wire form: u8 disc @0 + joined-flat (i32 ptr, i32 len)
        // at retArea+4. Ok path: cabi_realloc + memcpy + write
        // disc=0 + (ptr, len). Err path: write disc=1 + u32 at
        // retArea+4. Mirrors realistic WASI shapes like
        // wasi:filesystem/types.read-link → result<string, error-code>.

        [WitSource(@"interface resstr-env",
            Package = "my:test@1.0.0", Interface = "resstr-env")]
        public interface IResStr
        {
            [WitSource(@"compute: func() -> result<string, u32>;",
                Package = "my:test@1.0.0", Interface = "resstr-env",
                Item = "compute")]
            Result<string, uint> Compute();
        }

        public sealed class ResStrBundle
        {
            public IResStr ResstrEnv { get; }
            public ResStrBundle(IResStr r) { ResstrEnv = r; }
        }

        private sealed class FixedResStr : IResStr
        {
            private readonly Result<string, uint> _v;
            public FixedResStr(Result<string, uint> v) { _v = v; }
            public Result<string, uint> Compute() => _v;
        }

        // ====== Result<primitive, byte[]> return =================
        // Symmetric to ResStr but exercises the byte[] arm on the
        // Err side. Wire form: u8 disc @0 + joined-flat
        // (i32 ptr, i32 len) | u32 at retArea+4.

        [WitSource(@"interface resbyt-env",
            Package = "my:test@1.0.0", Interface = "resbyt-env")]
        public interface IResByt
        {
            [WitSource(@"compute: func() -> result<u32, list<u8>>;",
                Package = "my:test@1.0.0", Interface = "resbyt-env",
                Item = "compute")]
            Result<uint, byte[]> Compute();
        }

        public sealed class ResBytBundle
        {
            public IResByt ResbytEnv { get; }
            public ResBytBundle(IResByt r) { ResbytEnv = r; }
        }

        private sealed class FixedResByt : IResByt
        {
            private readonly Result<uint, byte[]> _v;
            public FixedResByt(Result<uint, byte[]> v) { _v = v; }
            public Result<uint, byte[]> Compute() => _v;
        }

        // ====== Variant param test surface =======================
        // Mirrors the source-generator-emitted shape for WIT
        // variants (HttpMethod, IpAddress, etc.):
        //   - abstract base class with [WitSource("variant ...")]
        //   - nested sealed classes per case in declaration order
        //   - empty cases: no body
        //   - payload cases: public Value { get; } + ctor(payload)

        [WitSource(@"variant signal { tick, beep(u32), name(string) }",
            Package = "my:test@1.0.0", Interface = "var-env",
            Item = "signal")]
        public abstract class Signal
        {
            public sealed class SignalTick : Signal { }
            public sealed class SignalBeep : Signal
            {
                public uint Value { get; }
                public SignalBeep(uint value) { Value = value; }
            }
            public sealed class SignalName : Signal
            {
                public string Value { get; }
                public SignalName(string value) { Value = value; }
            }
        }

        [WitSource(@"interface var-env",
            Package = "my:test@1.0.0", Interface = "var-env")]
        public interface ISignalTaker
        {
            // Returns a probe value the test can assert against.
            // Encodes both the case kind AND the payload contents
            // back as i32:
            //   Tick      → 0xA000_0000
            //   Beep(v)   → 0xB000_0000 | (v & 0x0FFF_FFFF)
            //   Name(s)   → 0xC000_0000 | (s.Length & 0x0FFF_FFFF)
            [WitSource(@"take-signal: func(s: signal) -> u32;",
                Package = "my:test@1.0.0", Interface = "var-env",
                Item = "take-signal")]
            uint TakeSignal(Signal s);
        }

        public sealed class SignalBundle
        {
            public ISignalTaker VarEnv { get; }
            public SignalBundle(ISignalTaker s) { VarEnv = s; }
        }

        private sealed class SignalProbe : ISignalTaker
        {
            public uint TakeSignal(Signal s) => s switch
            {
                Signal.SignalTick _ => 0xA000_0000u,
                Signal.SignalBeep b => 0xB000_0000u | (b.Value & 0x0FFF_FFFFu),
                Signal.SignalName n => 0xC000_0000u | ((uint)n.Value.Length & 0x0FFF_FFFFu),
                _ => 0xFFFF_FFFFu,
            };
        }

        // ====== Option<T> param test surface =====================
        // Source-gen convention uses Option<T> from
        // Wacs.ComponentModel.Runtime (NOT C# Nullable<T>) — option
        // is a 2-case variant in canonical ABI, distinct from the
        // C# null-reference / Nullable<T> shape.

        [WitSource(@"interface opt-env",
            Package = "my:test@1.0.0", Interface = "opt-env")]
        public interface IOptTaker
        {
            [WitSource(@"take-opt: func(o: option<u32>);",
                Package = "my:test@1.0.0", Interface = "opt-env",
                Item = "take-opt")]
            void TakeOpt(Option<uint> opt);
        }

        public sealed class OptBundle
        {
            public IOptTaker OptEnv { get; }
            public OptBundle(IOptTaker optEnv) { OptEnv = optEnv; }
        }

        private sealed class CapturingOptTaker : IOptTaker
        {
            public Option<uint> Last { get; private set; }
            public void TakeOpt(Option<uint> opt) { Last = opt; }
        }

        // ====== Option<string> param test surface ================
        // Aggregate inner type — disc + (ptr, len) for a 3-slot
        // wire form. The Some path lifts via StringMarshal.LiftUtf8
        // (recursing into EmitLiftForType for the inner string).

        [WitSource(@"interface optstr-env",
            Package = "my:test@1.0.0", Interface = "optstr-env")]
        public interface IOptStrTaker
        {
            [WitSource(@"take-optstr: func(o: option<string>);",
                Package = "my:test@1.0.0", Interface = "optstr-env",
                Item = "take-optstr")]
            void TakeOptStr(Option<string> opt);
        }

        public sealed class OptStrBundle
        {
            public IOptStrTaker OptStrEnv { get; }
            public OptStrBundle(IOptStrTaker s) { OptStrEnv = s; }
        }

        private sealed class CapturingOptStrTaker : IOptStrTaker
        {
            public Option<string> Last { get; private set; }
            public void TakeOptStr(Option<string> opt) { Last = opt; }
        }

        // ====== own<R> as direct param test surface =============
        // A free function that takes a resource-typed CLR param.
        // Wasm wire is a single i32 handle; the IL looks up the
        // typed instance via ctx.Resources (same machinery as
        // resource-instance-method `this`).

        [WitSource(@"interface own-env",
            Package = "my:test@1.0.0", Interface = "own-env")]
        public interface IOwnTaker
        {
            // The resource-typed param (IWidget — already declared
            // in res-env) lowers to a single i32 handle on the wire.
            [WitSource(@"take-widget: func(w: own<widget>) -> u32;",
                Package = "my:test@1.0.0", Interface = "own-env",
                Item = "take-widget")]
            uint TakeWidget(IWidget widget);

            // Option<own<R>> — recursive composition of Option<T> +
            // resource-interface lift. Wasm wire is (i32 disc, i32 handle).
            [WitSource(@"take-opt-widget: func(w: option<own<widget>>) -> u32;",
                Package = "my:test@1.0.0", Interface = "own-env",
                Item = "take-opt-widget")]
            uint TakeOptWidget(Option<IWidget> widget);
        }

        public sealed class OwnBundle
        {
            public IOwnTaker OwnEnv { get; }
            public OwnBundle(IOwnTaker o) { OwnEnv = o; }
        }

        private sealed class WidgetReader : IOwnTaker
        {
            public uint TakeWidget(IWidget widget) => widget.Read();

            // Returns the widget's value when Some, or 0 when None.
            // Lets the test assert via the wasm i32 result whether
            // direct-linked emit threaded the inner-type lift
            // correctly through the Option Some branch.
            public uint TakeOptWidget(Option<IWidget> opt)
                => opt.HasValue ? opt.Value.Read() : 0u;
        }

        // ====== Result<TOk, TErr> param test surface =============
        // Wasm pattern: result<u32, u32> — both sides 1×i32. The
        // emit dispatches on disc (0=Ok, 1=Err) and constructs
        // via Result<TOk,TErr>::FromOk(T) / FromErr(T).

        [WitSource(@"interface res-env",
            Package = "my:test@1.0.0", Interface = "res-env")]
        public interface IResultTaker
        {
            // Encode the side + payload back as wasm i32 so the
            // test can probe via the export's return value:
            //   Ok(v)  → 0xA000_0000 | (v & 0x0FFF_FFFF)
            //   Err(v) → 0xE000_0000 | (v & 0x0FFF_FFFF)
            [WitSource(@"take-result: func(r: result<u32, u32>) -> u32;",
                Package = "my:test@1.0.0", Interface = "res-env",
                Item = "take-result")]
            uint TakeResult(Result<uint, uint> r);
        }

        public sealed class ResultBundle
        {
            public IResultTaker ResEnv { get; }
            public ResultBundle(IResultTaker r) { ResEnv = r; }
        }

        private sealed class ResultProbe : IResultTaker
        {
            public uint TakeResult(Result<uint, uint> r)
                => r.IsOk
                    ? 0xA000_0000u | (r.Ok & 0x0FFF_FFFFu)
                    : 0xE000_0000u | (r.Err & 0x0FFF_FFFFu);
        }

        // ====== Tuple<u32, u32> param test surface ===============

        [WitSource(@"interface tup-env",
            Package = "my:test@1.0.0", Interface = "tup-env")]
        public interface ITupleTaker
        {
            // tuple<u32, u32> wire form is 2 i32 slots; the typed
            // CLR side is ValueTuple<uint, uint> (i.e. (uint, uint)).
            [WitSource(@"take-tup: func(t: tuple<u32, u32>) -> u32;",
                Package = "my:test@1.0.0", Interface = "tup-env",
                Item = "take-tup")]
            uint TakeTup((uint, uint) t);
        }

        public sealed class TupleBundle
        {
            public ITupleTaker TupEnv { get; }
            public TupleBundle(ITupleTaker t) { TupEnv = t; }
        }

        private sealed class TupleAdder : ITupleTaker
        {
            // Returns Item1 * 256 + Item2 so the test can probe
            // both elements made it through with the right order.
            public uint TakeTup((uint, uint) t)
                => (t.Item1 << 8) | t.Item2;
        }

        // ====== tuple<u32, string> param =========================
        // Mixed-element tuples — Item1 is i32 (1 slot), Item2 is
        // string (2 slots: ptr+len). Total wasm slots = 3.

        [WitSource(@"interface mtup-env",
            Package = "my:test@1.0.0", Interface = "mtup-env")]
        public interface IMixedTupleTaker
        {
            [WitSource(@"take-mtup: func(t: tuple<u32, string>) -> u32;",
                Package = "my:test@1.0.0", Interface = "mtup-env",
                Item = "take-mtup")]
            uint TakeMixed((uint, string) t);
        }

        public sealed class MixedTupleBundle
        {
            public IMixedTupleTaker MtupEnv { get; }
            public MixedTupleBundle(IMixedTupleTaker m)
            { MtupEnv = m; }
        }

        private sealed class MixedTupleProbe : IMixedTupleTaker
        {
            // (Item1 * 1000) + Item2.Length so the test can assert
            // both elements made it through.
            public uint TakeMixed((uint, string) t)
                => (t.Item1 * 1000u) + (uint)t.Item2.Length;
        }

        // ====== Record param test surface ========================
        // Matches the WitHostInterfaceGenerator emission shape:
        // sealed class with public auto-properties + parameterless
        // ctor. Wire form is the concatenation of each property's
        // flat-slot count in declaration order.

        [WitSource(@"record point { x: u32, y: u32 }",
            Package = "my:test@1.0.0", Interface = "rec-env",
            Item = "point")]
        public sealed class Point
        {
            public uint X { get; set; } = default!;
            public uint Y { get; set; } = default!;
        }

        [WitSource(@"interface rec-env",
            Package = "my:test@1.0.0", Interface = "rec-env")]
        public interface IPointTaker
        {
            [WitSource(@"take-point: func(p: point) -> u32;",
                Package = "my:test@1.0.0", Interface = "rec-env",
                Item = "take-point")]
            uint TakePoint(Point p);
        }

        public sealed class PointBundle
        {
            public IPointTaker RecEnv { get; }
            public PointBundle(IPointTaker p) { RecEnv = p; }
        }

        private sealed class PointHasher : IPointTaker
        {
            // (X<<8)|Y so the test can probe field order.
            public uint TakePoint(Point p) => (p.X << 8) | p.Y;
        }

        // ====== enum + flags param test surface ==================
        // WIT enums lower as their underlying integral wire form
        // (typically u8 → i32). Flags use uint backing to match
        // the source generator's emission.

        [WitSource(@"enum color { red, green, blue }",
            Package = "my:test@1.0.0", Interface = "enum-env",
            Item = "color")]
        public enum Color : byte
        {
            Red = 0,
            Green = 1,
            Blue = 2,
        }

        [WitSource(@"flags perms { read, write, exec }",
            Package = "my:test@1.0.0", Interface = "enum-env",
            Item = "perms")]
        [Flags]
        public enum Perms : uint
        {
            None = 0,
            Read = 1u << 0,
            Write = 1u << 1,
            Exec = 1u << 2,
        }

        [WitSource(@"interface enum-env",
            Package = "my:test@1.0.0", Interface = "enum-env")]
        public interface IEnumTaker
        {
            // Encode (color, perms) back as i32 so the test can
            // probe both made it through with the right typing.
            [WitSource(@"take-ef: func(c: color, p: perms) -> u32;",
                Package = "my:test@1.0.0", Interface = "enum-env",
                Item = "take-ef")]
            uint TakeEnumFlags(Color c, Perms p);
        }

        public sealed class EnumBundle
        {
            public IEnumTaker EnumEnv { get; }
            public EnumBundle(IEnumTaker e) { EnumEnv = e; }
        }

        private sealed class EnumProbe : IEnumTaker
        {
            // ((byte)c << 16) | (uint)p — caller asserts both
            // came through with their typed values intact.
            public uint TakeEnumFlags(Color c, Perms p)
                => ((uint)(byte)c << 16) | (uint)p;
        }

        // ====== enum-return test surface =========================
        // Free function returning a Color enum. The CLR enum value
        // shares stack form with its underlying type, so the same
        // primitive-return path that handles `byte` returns works
        // directly for `Color : byte`.

        [WitSource(@"interface enumret-env",
            Package = "my:test@1.0.0", Interface = "enumret-env")]
        public interface IColorPicker
        {
            [WitSource(@"pick: func() -> color;",
                Package = "my:test@1.0.0", Interface = "enumret-env",
                Item = "pick")]
            Color Pick();
        }

        public sealed class EnumRetBundle
        {
            public IColorPicker EnumretEnv { get; }
            public EnumRetBundle(IColorPicker p) { EnumretEnv = p; }
        }

        private sealed class GreenPicker : IColorPicker
        {
            public Color Pick() => Color.Green;
        }

        private sealed class FakeEnv : IEnv
        {
            private readonly ulong _v;
            public FakeEnv(ulong v) { _v = v; }
            public ulong GetValue() => _v;
            // Distinct compute so the test asserts both sides made
            // it through with the right CONV: a is uint (wide), b
            // is byte (narrow → conv.u1).
            public int Combine(uint a, byte b)
                => unchecked((int)(a * 1000u + b));
        }

        // ============== Wasm fixture =============================
        //
        // (module
        //   (type $t (func (result i64)))
        //   (import "my:test/env@1.0.0" "get-value" (func $imp (type $t)))
        //   (func (export "call_get") (result i64)
        //     call $imp))
        //
        // Sections (byte-by-byte, all sizes single-byte LEB):
        //   1  type:    1×(func ()→i64)
        //   2  import:  "my:test/env@1.0.0"."get-value" : type 0
        //   3  func:    1 local function of type 0
        //   7  export:  "call_get" → func 1
        //  10  code:    body = call 0; end
        private static byte[] BuildDirectLinkedFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — () → i64
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7E,
            // Import section
            0x02, 0x1F, 0x01,
            // module name: "my:test/env@1.0.0"   (17 bytes)
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E,
            0x30,
            // entity name: "get-value"           (9 bytes)
            0x09,
            0x67, 0x65, 0x74, 0x2D, 0x76, 0x61, 0x6C, 0x75, 0x65,
            // desc: func, typeidx 0
            0x00, 0x00,
            // Function section: 1 local function — type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_get" → func 1 (after the import)
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x65, 0x74,
            0x00, 0x01,
            // Code section: body = call 0; end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // Same wasm shape as the string-return fixture but for
        // Option<string>. retArea is 12 bytes (u8 disc + 3 padding
        // + i32 ptr + i32 len). The export reads disc OR (when
        // disc=1) reads the first byte at the ptr.
        private static byte[] BuildOptionStringReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types (same shape as string-return)
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import (name : type 1)
            // module: "my:test/optstr-ret-env@1.0.0" (28)
            // size = 1 + 1 + 28 + 1 + 4 + 2 = 37 = 0x25
            0x02, 0x25, 0x01,
            0x1C,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x73, 0x74, 0x72, 0x2D, 0x72,
            0x65, 0x74, 0x2D, 0x65, 0x6E, 0x76, 0x40, 0x31,
            0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x6E, 0x61, 0x6D, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            // funcs[0] = type 0 (cabi_realloc)
            // funcs[1] = type 2 (call_name_disc)
            // funcs[2] = type 2 (call_name_first_byte)
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            // call_name_disc (14): 1+14+1+1 = 17
            // call_name_first_byte (20): 1+20+1+1 = 23
            // cabi_realloc (12): 1+12+1+1 = 15
            // size = 1 + 17 + 23 + 15 = 56 = 0x38
            0x07, 0x38, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x61, 0x6D,
            0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x61, 0x6D,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            // body0 cabi_realloc:
            //   locals(1) + global.get(2) + global.get(2) + local.get(2) + i32.add(1) + global.set(2) + end(1) = 11
            // body1 call_name_disc: locals + i32.const + call + i32.const + i32.load8_u@0 + end = 11
            // body2 call_name_first_byte:
            //   locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load align=2 offset=4(3) + i32.load8_u align=0 offset=0(3) + end(1) = 14
            // sizes: 12, 12, 15
            // section size = 1 + 12 + 12 + 15 = 40 = 0x28
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,    // i32.load align=2 offset=4 → ptr at retArea+4
            0x2D, 0x00, 0x00,                 // i32.load8_u → byte at *(ptr)
            0x0B,
        };

        // Result<string, u32> retArea (joined-flat at offset 4):
        //   disc @0 (u8)
        //   Ok  : i32 ptr @4 + i32 len @8
        //   Err : u32        @4
        // Three probes: disc, first byte at *(ptr@retArea+4) (Ok),
        // i32 @retArea+4 (Err arm value or Ok ptr).
        // (module
        //   (type $tCabi (func (param i32 i32 i32 i32) (result i32)))
        //   (type $tCompute (func (param i32)))   ;; compute(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_*  → u32
        //   (import "my:test/resstr-env@1.0.0" "compute"
        //           (func $imp (type $tCompute)))
        //   (memory 1)
        //   (global $next (mut i32) (i32.const 32))
        //   (func $cabi_realloc (type $tCabi)
        //     global.get $next; global.get $next; local.get 3;
        //     i32.add; global.set $next)
        //   (func (export "call_compute_disc") (result i32)
        //     i32.const 16; call $imp;
        //     i32.const 16; i32.load8_u)
        //   (func (export "call_compute_first_byte") (result i32)
        //     i32.const 16; call $imp;
        //     i32.const 16; i32.load offset=4; i32.load8_u)
        //   (func (export "call_compute_int4") (result i32)
        //     i32.const 16; call $imp;
        //     i32.const 16; i32.load offset=4)
        //   (export "cabi_realloc" (func $cabi_realloc)))
        private static byte[] BuildResultStringReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types (same shape as optstr-ret-env)
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import (compute : type 1)
            // module: "my:test/resstr-env@1.0.0" (24)
            // entity: "compute" (7)
            // size = 1 + 1 + 24 + 1 + 7 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x73, 0x74, 0x72, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x07,
            0x63, 0x6F, 0x6D, 0x70, 0x75, 0x74, 0x65,
            0x00, 0x01,
            // Function section: 4 local funcs
            //   funcs[0] = type 0 (cabi_realloc)
            //   funcs[1..3] = type 2
            0x03, 0x05, 0x04, 0x00, 0x02, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 4 exports
            //   call_compute_disc       (17): 1+17+1+1 = 20
            //   call_compute_first_byte (23): 1+23+1+1 = 26
            //   call_compute_int4       (17): 1+17+1+1 = 20
            //   cabi_realloc            (12): 1+12+1+1 = 15
            // size = 1 + 20 + 26 + 20 + 15 = 82 = 0x52
            0x07, 0x52, 0x04,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x17,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x66, 0x69, 0x72,
            0x73, 0x74, 0x5F, 0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x69, 0x6E, 0x74, 0x34,
            0x00, 0x04,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 4 bodies
            //   body0 cabi_realloc        : size 11 → 12
            //   body1 call_compute_disc   : size 11 → 12
            //   body2 call_compute_first_byte: size 14 → 15
            //   body3 call_compute_int4   : size 11 → 12
            // size = 1 + 12 + 12 + 15 + 12 = 52 = 0x34
            0x0A, 0x34, 0x04,
            // body0 cabi_realloc
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            // body1 call_compute_disc
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            // body2 call_compute_first_byte
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,    // i32.load align=2 offset=4 → ptr
            0x2D, 0x00, 0x00,                 // i32.load8_u → byte at *(ptr)
            0x0B,
            // body3 call_compute_int4
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,    // i32.load align=2 offset=4 → i32 at retArea+4
            0x0B,
        };

        // Result<u32, list<u8>> — symmetric to ResultString but the
        // byte[] arm rides the Err side. Wire form identical: u8
        // disc + joined-flat (i32 ptr | u32) at offset 4. Same wasm
        // shape, only the import module/entity strings change.
        private static byte[] BuildResultByteArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/resbyt-env@1.0.0" (24)
            // entity: "compute" (7)
            // size = 1 + 1 + 24 + 1 + 7 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x62, 0x79, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x07,
            0x63, 0x6F, 0x6D, 0x70, 0x75, 0x74, 0x65,
            0x00, 0x01,
            // Function section: 4 local funcs
            0x03, 0x05, 0x04, 0x00, 0x02, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 4 exports (same names as ResultString
            // fixture — byte at *(ptr) is the Err byte[] first byte
            // when in Err branch; int4 is the Ok u32 value)
            0x07, 0x52, 0x04,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x17,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x66, 0x69, 0x72,
            0x73, 0x74, 0x5F, 0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x69, 0x6E, 0x74, 0x34,
            0x00, 0x04,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 4 bodies (identical to ResultString)
            0x0A, 0x34, 0x04,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
        };

        // Same wasm shape as the string-return fixture, just with
        // a different module + entity name. The cabi_realloc
        // export and bump-allocator pattern are identical.
        private static byte[] BuildByteArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types (same as string-return)
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import (give : type 1)
            // size = 1 + 1 + 27 + 1 + 4 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/bytes-ret-env@1.0.0" (27)
            0x1B,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x62, 0x79, 0x74, 0x65, 0x73, 0x2D, 0x72, 0x65,
            0x74, 0x2D, 0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E,
            0x30, 0x2E, 0x30,
            // entity: "give" (4)
            0x04,
            0x67, 0x69, 0x76, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            // funcs[0] = type 0 (cabi_realloc)
            // funcs[1] = type 2 (call_give_len)
            // funcs[2] = type 2 (call_give_first_byte)
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory section: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global section: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            // call_give_len (13): 1+13+1+1 = 16
            // call_give_first_byte (20): 1+20+1+1 = 23
            // cabi_realloc (12): 1+12+1+1 = 15
            // size = 1 + 16 + 23 + 15 = 55 = 0x37
            0x07, 0x37, 0x03,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x6C, 0x65, 0x6E,
            0x00, 0x02,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (same as string-return)
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x00,
            0x2D, 0x00, 0x00,
            0x0B,
        };

        // bool[] return: same wire as byte[] (i32 ptr + i32 count
        // + raw bytes per element, 0/1). The second probe reads
        // load8_u at *(ptr+0) just like byte[].
        private static byte[] BuildBoolArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/boolarr-env@1.0.0" (25)
            // entity: "give" (4)
            // size = 1 + 1 + 25 + 1 + 4 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x62, 0x6F, 0x6F, 0x6C, 0x61, 0x72, 0x72, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x67, 0x69, 0x76, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_give_count       (15): 1+15+1+1 = 18
            //   call_give_first_byte  (20): 1+20+1+1 = 23
            //   cabi_realloc          (12): 15
            // size = 1 + 18 + 23 + 15 = 57 = 0x39
            0x07, 0x39, 0x03,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (12/12/15)
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x00,    // ptr at retArea+0
            0x2D, 0x00, 0x00,                 // load8_u → first byte
            0x0B,
        };

        // Same shape as the byte[] fixture but for list<s32> — the
        // second probe reads an i32 (4-byte LE) at *(ptr+0) instead
        // of u8.
        private static byte[] BuildIntArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/ints-ret-env@1.0.0" (26)
            // entity: "give" (4)
            // size = 1 + 1 + 26 + 1 + 4 + 2 = 35 = 0x23
            0x02, 0x23, 0x01,
            0x1A,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x69, 0x6E, 0x74, 0x73, 0x2D, 0x72, 0x65, 0x74,
            0x2D, 0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30,
            0x2E, 0x30,
            0x04,
            0x67, 0x69, 0x76, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory section: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global section: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_give_len       (13): 1+13+1+1 = 16
            //   call_give_first_int (19): 1+19+1+1 = 22
            //   cabi_realloc        (12): 1+12+1+1 = 15
            // size = 1 + 16 + 22 + 15 = 54 = 0x36
            0x07, 0x36, 0x03,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x6C, 0x65, 0x6E,
            0x00, 0x02,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x69, 0x6E, 0x74,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            //   body0 cabi_realloc       : 11 → 12
            //   body1 call_give_len      : 11 → 12
            //   body2 call_give_first_int: 14 → 15
            // size = 1 + 12 + 12 + 15 = 40 = 0x28
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x00,    // ptr at retArea+0
            0x28, 0x02, 0x00,                  // i32.load → first int
            0x0B,
        };

        // list<string> retArea: i32 ptr @0 + i32 count @4. *(ptr)
        // is an array of (i32 sptr, i32 slen) pairs; *(sptr) is the
        // UTF-8 buffer. Probes:
        //   call_give_count           → outer count
        //   call_give_first_byte      → first byte of first string
        // The byte probe chases:
        //   retArea → load i32 offset=0  → outer_ptr
        //   outer_ptr → load i32 offset=0 → first sptr
        //   first_sptr → load8_u offset=0 → first byte
        private static byte[] BuildStringArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/strs-ret-env@1.0.0" (26)
            // entity: "give" (4)
            // size = 1 + 1 + 26 + 1 + 4 + 2 = 35 = 0x23
            0x02, 0x23, 0x01,
            0x1A,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x73, 0x74, 0x72, 0x73, 0x2D, 0x72, 0x65, 0x74,
            0x2D, 0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30,
            0x2E, 0x30,
            0x04,
            0x67, 0x69, 0x76, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_give_count       (15): 1+15+1+1 = 18
            //   call_give_first_byte  (20): 1+20+1+1 = 23
            //   cabi_realloc          (12): 15
            // size = 1 + 18 + 23 + 15 = 57 = 0x39
            0x07, 0x39, 0x03,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            //   body0 cabi_realloc          : 11 → 12
            //   body1 call_give_count       : 11 → 12
            //   body2 call_give_first_byte  : 17 → 18
            //     locals(1)+i32.const(2)+call(2)+i32.const(2)+
            //     i32.load(3)+i32.load(3)+i32.load8_u(3)+end(1)=17
            // size = 1 + 12 + 12 + 18 = 43 = 0x2B
            0x0A, 0x2B, 0x03,
            // body0 cabi_realloc
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            // body1 call_give_count
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            // body2 call_give_first_byte (chase outer + inner ptrs)
            0x11, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr at retArea+0
            0x28, 0x02, 0x00,                 // first sptr
            0x2D, 0x00, 0x00,                 // first byte
            0x0B,
        };

        // Same shape as int[] fixture but for list<s64>. The second
        // probe reads i64 at *(ptr+0) and wraps to i32 to fit the
        // probe's () → i32 result type.
        private static byte[] BuildLongArrayReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/longs-ret-env@1.0.0" (27)
            // entity: "give" (4)
            // size = 1 + 1 + 27 + 1 + 4 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            0x1B,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6C, 0x6F, 0x6E, 0x67, 0x73, 0x2D, 0x72, 0x65,
            0x74, 0x2D, 0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E,
            0x30, 0x2E, 0x30,
            0x04,
            0x67, 0x69, 0x76, 0x65,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory section: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global section: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_give_len           (13): 16
            //   call_give_first_long_lo (23): 1+23+1+1 = 26
            //   cabi_realloc            (12): 15
            // size = 1 + 16 + 26 + 15 = 58 = 0x3A
            0x07, 0x3A, 0x03,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x6C, 0x65, 0x6E,
            0x00, 0x02,
            0x17,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x69, 0x76,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x6C, 0x6F, 0x6E, 0x67, 0x5F, 0x6C, 0x6F,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            //   body0 cabi_realloc          : 11 → 12
            //   body1 call_give_len         : 11 → 12
            //   body2 call_give_first_long_lo: 14 → 15
            //     (reads low 32 bits of first long via i32.load —
            //      same body bytes as the int fixture; for an LE
            //      long like 0x42 the low i32 is 0x42 too)
            // size = 1 + 12 + 12 + 15 = 40 = 0x28
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x00,
            0x28, 0x02, 0x00,
            0x0B,
        };

        // (module
        //   (type $tCabi (func (param i32 i32 i32 i32) (result i32)))
        //   (type $tGreet (func (param i32)))     ;; greet(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_*  → u32
        //   (type $tEntryWithIdx (func (param i32) (result i32)))
        //   (import "my:test/str-ret-env@1.0.0" "greet"
        //           (func $imp (type $tGreet)))
        //   (memory 1)
        //   (global $next (mut i32) (i32.const 32))   ;; bump allocator start
        //   (func $cabi_realloc (type $tCabi)
        //     global.get $next     ;; capture current ptr (becomes return)
        //     global.get $next
        //     local.get 3          ;; new_len
        //     i32.add
        //     global.set $next     ;; bump
        //   )
        //   (func (export "call_greet_len") (result i32)
        //     i32.const 16; call $imp
        //     i32.const 16; i32.load offset=4)        ;; len @ retArea+4
        //   (func (export "call_greet_first_byte") (result i32)
        //     i32.const 16; call $imp
        //     i32.const 16; i32.load                  ;; ptr @ retArea+0
        //     i32.load8_u)                            ;; first byte @ ptr
        //   (export "cabi_realloc" (func $cabi_realloc))
        //
        // The host writes (ptr, len) at retArea+0/+4 after
        // calling cabi_realloc to allocate a buffer at the
        // bump-allocator's current position and copying the
        // UTF-8 bytes into it.
        private static byte[] BuildStringReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 4 types
            // 0: (i32 i32 i32 i32) → i32 (8 bytes)
            // 1: (i32) → void (4)
            // 2: () → i32 (4)
            // 3: (i32) → i32 (5)  — unused, kept for layout simplicity
            //   wait, we don't need type 3. Cut to 3 types.
            // Total: 1 + 8 + 4 + 4 = 17 = 0x11
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import (greet : type 1)
            // size = 1 + 1 + 25 + 1 + 5 + 2 = 35 = 0x23
            0x02, 0x23, 0x01,
            // module: "my:test/str-ret-env@1.0.0" (25)
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x73, 0x74, 0x72, 0x2D, 0x72, 0x65, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "greet" (5)
            0x05,
            0x67, 0x72, 0x65, 0x65, 0x74,
            0x00, 0x01,
            // Function section: 3 local funcs
            // funcs[0] = type 0 (cabi_realloc)
            // funcs[1] = type 2 (call_greet_len)
            // funcs[2] = type 2 (call_greet_first_byte)
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory section: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global section: 1 mutable i32 init to 32
            // size = count(1) + type(1) + mut(1) + init(2) + end(1) = 6
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            // call_greet_len (14): 1+14+1+1 = 17
            // call_greet_first_byte (21): 1+21+1+1 = 24
            // cabi_realloc (12): 1+12+1+1 = 15
            // size = 1 + 17 + 24 + 15 = 57 = 0x39
            0x07, 0x39, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x72, 0x65,
            0x65, 0x74, 0x5F, 0x6C, 0x65, 0x6E,
            0x00, 0x02,
            0x15,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x72, 0x65,
            0x65, 0x74, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74,
            0x5F, 0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            // body0 cabi_realloc:
            //   locals(1) + global.get(2) + global.get(2) + local.get(2) + i32.add(1) + global.set(2) + end(1) = 11
            // body1 call_greet_len:
            //   locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load align=2 offset=4(3) + end(1) = 11
            // body2 call_greet_first_byte:
            //   locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load align=2 offset=0(3) + i32.load8_u align=0 offset=0(3) + end(1) = 14
            // sizes: 12, 12, 15
            // section size = 1 + 12 + 12 + 15 = 40 = 0x28
            0x0A, 0x28, 0x03,
            // body0:
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            // body1:
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            // body2:
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x00,
            0x2D, 0x00, 0x00,
            0x0B,
        };

        // (module
        //   (type $tEmit (func (param i32)))      ;; emit(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_*  → u32
        //   (import "my:test/codeFactory@1.0.0" "emit"
        //           (func $imp (type $tEmit)))
        //   (memory 1)
        //   (func (export "call_code_disc") (result i32)
        //     i32.const 16; call $imp
        //     i32.const 16; i32.load8_u)
        //   (func (export "call_code_value") (result i32)
        //     i32.const 16; call $imp
        //     i32.const 16; i32.load offset=4))   ;; payload @ retArea+4
        //
        // variant code retArea = u8 disc + u32 payload (8 bytes).
        // Two exports probe each slot.
        private static byte[] BuildVariantPayloadReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 25 + 1 + 4 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/codeFactory@1.0.0" (25)
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x63, 0x6F, 0x64, 0x65, 0x46, 0x61, 0x63, 0x74,
            0x6F, 0x72, 0x79, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "emit" (4)
            0x04,
            0x65, 0x6D, 0x69, 0x74,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 2 exports
            // call_code_disc (14): 1+14+1+1 = 17
            // call_code_value (15): 1+15+1+1 = 18
            // size = 1 + 17 + 18 = 36 = 0x24
            0x07, 0x24, 0x02,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x64,
            0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x64,
            0x65, 0x5F, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies
            // body0: locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load8_u(3) + end(1) = 11
            // body1: locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load(3) + end(1) = 11
            0x0A, 0x19, 0x02,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
        };

        // Option<tuple<u32,u32>> retArea: u8 disc @0 + u32 a @4 +
        // u32 b @8. Three probes per slot. No cabi_realloc needed.
        private static byte[] BuildOptionTupleReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/opttup-env@1.0.0" (24)
            // entity: "pair" (4)
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x74, 0x75, 0x70, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x70, 0x61, 0x69, 0x72,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_pair_disc (14): 1+14+1+1 = 17
            //   call_pair_a    (11): 1+11+1+1 = 14
            //   call_pair_b    (11): 1+11+1+1 = 14
            // size = 1 + 17 + 14 + 14 = 46 = 0x2E
            0x07, 0x2E, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x61, 0x69,
            0x72, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x0B,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x61, 0x69,
            0x72, 0x5F, 0x61,
            0x00, 0x02,
            0x0B,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x61, 0x69,
            0x72, 0x5F, 0x62,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 bytes → 12 framed)
            // size = 1 + 12 + 12 + 12 = 37 = 0x25
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,
            0x0B,
        };

        // Same shape as Option<tuple> fixture but for option<xy-pair>
        // (record-of-primitives). Module "optrec-env" + entity "xy".
        // Wire form is identical (u8 disc + 2 u32 fields).
        private static byte[] BuildOptionRecordReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/optrec-env@1.0.0" (24)
            // entity: "xy" (2)
            // size = 1 + 1 + 24 + 1 + 2 + 2 = 31 = 0x1F
            0x02, 0x1F, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x72, 0x65, 0x63, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x02,
            0x78, 0x79,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_xy_disc (12): 1+12+1+1 = 15
            //   call_xy_a    (9):  1+9+1+1 = 12
            //   call_xy_b    (9):  1+9+1+1 = 12
            // size = 1 + 15 + 12 + 12 = 40 = 0x28
            0x07, 0x28, 0x03,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x78, 0x79, 0x5F,
            0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x78, 0x79, 0x5F,
            0x61,
            0x00, 0x02,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x78, 0x79, 0x5F,
            0x62,
            0x00, 0x03,
            // Code section: same as Option<tuple>
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,
            0x0B,
        };

        // Result<Option<u32>, u32> retArea:
        //   @0: outer disc (u8)
        //   @4: Ok arm: inner Option<u32> disc (u8)
        //         Err arm: u32 (low byte coincides with offset 4)
        //   @8: Ok+Some arm: inner u32 value
        // Probes per slot.
        private static byte[] BuildResultOptionReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/resopt-env@1.0.0" (24)
            // entity: "go" (2)
            // size = 1 + 1 + 24 + 1 + 2 + 2 = 31 = 0x1F
            0x02, 0x1F, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x6F, 0x70, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x02,
            0x67, 0x6F,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_go_outer_disc (18): 21
            //   call_go_inner_disc (18): 21
            //   call_go_value      (13): 16
            // size = 1 + 21 + 21 + 16 = 59 = 0x3B
            0x07, 0x3B, 0x03,
            0x12,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x6F, 0x75, 0x74, 0x65, 0x72, 0x5F, 0x64, 0x69,
            0x73, 0x63,
            0x00, 0x01,
            0x12,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x69, 0x6E, 0x6E, 0x65, 0x72, 0x5F, 0x64, 0x69,
            0x73, 0x63,
            0x00, 0x02,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 → 12)
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x04,    // load8_u offset=4
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,    // i32.load offset=8
            0x0B,
        };

        // Option<Option<u32>> retArea: u8 outer_disc @0 +
        // (u8 inner_disc @4 + u32 inner_value @8). Probes per slot.
        // No cabi_realloc needed.
        private static byte[] BuildOptionOptionReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/optopt-env@1.0.0" (24)
            // entity: "num" (3)
            // size = 1 + 1 + 24 + 1 + 3 + 2 = 32 = 0x20
            0x02, 0x20, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x6F, 0x70, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x03,
            0x6E, 0x75, 0x6D,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_num_outer_disc (19): 1+19+1+1 = 22
            //   call_num_inner_disc (19): 1+19+1+1 = 22
            //   call_num_value      (14): 1+14+1+1 = 17
            // size = 1 + 22 + 22 + 17 = 62 = 0x3E
            0x07, 0x3E, 0x03,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x75, 0x6D,
            0x5F, 0x6F, 0x75, 0x74, 0x65, 0x72, 0x5F, 0x64,
            0x69, 0x73, 0x63,
            0x00, 0x01,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x75, 0x6D,
            0x5F, 0x69, 0x6E, 0x6E, 0x65, 0x72, 0x5F, 0x64,
            0x69, 0x73, 0x63,
            0x00, 0x02,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x75, 0x6D,
            0x5F, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 → 12)
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x04,    // load8_u offset=4 (inner disc)
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,    // i32.load offset=8 (inner value)
            0x0B,
        };

        // list<own<widget>> retArea: i32 ptr @0 + i32 count @4.
        // *(ptr) is a packed i32 handle buffer (4 bytes per handle).
        // Probes count + first handle.
        private static byte[] BuildListOfOwnReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/listown-env@1.0.0" (25)
            // entity: "all" (3)
            // size = 1 + 1 + 25 + 1 + 3 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6C, 0x69, 0x73, 0x74, 0x6F, 0x77, 0x6E, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x03,
            0x61, 0x6C, 0x6C,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_all_count        (14): 17
            //   call_all_first_handle (21): 24
            //   cabi_realloc          (12): 15
            // size = 1 + 17 + 24 + 15 = 57 = 0x39
            0x07, 0x39, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x15,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F, 0x68,
            0x61, 0x6E, 0x64, 0x6C, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (12/12/15)
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr
            0x28, 0x02, 0x00,                 // first handle (i32 at *ptr+0)
            0x0B,
        };

        // list<list<s32>> retArea: i32 ptr @0 + i32 count @4.
        // *(ptr) is array of (sub_ptr, sub_len) pairs; each
        // *(sub_ptr) is a packed i32 buffer.
        private static byte[] BuildListOfIntListReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/listil-env@1.0.0" (24)
            // entity: "all" (3)
            // size = 1 + 1 + 24 + 1 + 3 + 2 = 32 = 0x20
            0x02, 0x20, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6C, 0x69, 0x73, 0x74, 0x69, 0x6C, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x03,
            0x61, 0x6C, 0x6C,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_all_count      (14): 17
            //   call_all_first_int  (18): 21
            //   cabi_realloc        (12): 15
            // size = 1 + 17 + 21 + 15 = 54 = 0x36
            0x07, 0x36, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x12,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F, 0x69,
            0x6E, 0x74,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (12/12/18)
            0x0A, 0x2B, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x11, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr
            0x28, 0x02, 0x00,                 // first sub_ptr
            0x28, 0x02, 0x00,                 // first int (i32 at *sub_ptr+0)
            0x0B,
        };

        // list<list<u8>> retArea: i32 ptr @0 + i32 count @4.
        // *(ptr) is array of (sub_ptr, sub_len) pairs (8 bytes each);
        // each *(sub_ptr) is a raw byte buffer. Two-level cabi_realloc:
        // outer pair-array + per-sub byte buffer.
        private static byte[] BuildListOfByteListReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/listbyt-env@1.0.0" (25)
            // entity: "all" (3)
            // size = 1 + 1 + 25 + 1 + 3 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6C, 0x69, 0x73, 0x74, 0x62, 0x79, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x03,
            0x61, 0x6C, 0x6C,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_all_count       (14): 17
            //   call_all_first_byte  (19): 22
            //   cabi_realloc         (12): 15
            // size = 1 + 17 + 22 + 15 = 55 = 0x37
            0x07, 0x37, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F, 0x62,
            0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (12/12/18)
            0x0A, 0x2B, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x11, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr
            0x28, 0x02, 0x00,                 // first sub_ptr
            0x2D, 0x00, 0x00,                 // first byte
            0x0B,
        };

        // Option<list<s32>> retArea: u8 disc @0 + (i32 ptr @4, i32
        // count @8). Some path: cabi_realloc + memcpy + write
        // disc=1 + (ptr, count). None: write disc=0. Probes:
        //   call_list_disc          → disc byte
        //   call_list_first_int     → first int (chases ptr)
        private static byte[] BuildOptionIntListReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/optintl-env@1.0.0" (25)
            // entity: "list" (4)
            // size = 1 + 1 + 25 + 1 + 4 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x69, 0x6E, 0x74, 0x6C, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x6C, 0x69, 0x73, 0x74,
            0x00, 0x01,
            // Function section: 3 local funcs
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 3 exports
            //   call_list_disc      (14): 17
            //   call_list_first_int (19): 22
            //   cabi_realloc        (12): 15
            // size = 1 + 17 + 22 + 15 = 55 = 0x37
            0x07, 0x37, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6C, 0x69, 0x73,
            0x74, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6C, 0x69, 0x73,
            0x74, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x69, 0x6E, 0x74,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies (12/12/15)
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,    // ptr at retArea+4
            0x28, 0x02, 0x00,                 // first int (i32 at *ptr+0)
            0x0B,
        };

        // list<tuple<u32,u32>> retArea: i32 ptr @0 + i32 count @4.
        // *(ptr) is a packed array of (u32, u32) — 8 bytes per
        // element. cabi_realloc allocates the outer buffer; the
        // emit loops per-element to write a@elemBase, b@elemBase+4.
        // Probes: count, first.a (ptr→load i32@0→load i32@0),
        // first.b (ptr→load i32@0→load i32@4).
        private static byte[] BuildListOfTupleReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/listtup-env@1.0.0" (25)
            // entity: "all" (3)
            // size = 1 + 1 + 25 + 1 + 3 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6C, 0x69, 0x73, 0x74, 0x74, 0x75, 0x70, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x03,
            0x61, 0x6C, 0x6C,
            0x00, 0x01,
            // Function section: 4 local funcs
            //   funcs[0] = type 0 (cabi_realloc)
            //   funcs[1..3] = type 2
            0x03, 0x05, 0x04, 0x00, 0x02, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 4 exports
            //   call_all_count   (14): 17
            //   call_all_first_a (16): 19
            //   call_all_first_b (16): 19
            //   cabi_realloc     (12): 15
            // size = 1 + 17 + 19 + 19 + 15 = 71 = 0x47
            0x07, 0x47, 0x04,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x63, 0x6F, 0x75, 0x6E, 0x74,
            0x00, 0x02,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F, 0x61,
            0x00, 0x03,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F, 0x62,
            0x00, 0x04,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 4 bodies
            //   body0 cabi_realloc      : 11 → 12
            //   body1 call_all_count    : 11 → 12
            //   body2 call_all_first_a  : 14 → 15
            //   body3 call_all_first_b  : 14 → 15
            // size = 1 + 12 + 12 + 15 + 15 = 55 = 0x37
            0x0A, 0x37, 0x04,
            // body0 cabi_realloc
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            // body1 call_all_count
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            // body2 call_all_first_a (chase outer ptr → first.a)
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr
            0x28, 0x02, 0x00,                 // first.a (i32 at *ptr+0)
            0x0B,
            // body3 call_all_first_b (chase outer ptr → first.b)
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x00,                 // outer_ptr
            0x28, 0x02, 0x04,                 // first.b (i32 at *ptr+4)
            0x0B,
        };

        // Variant with Result<u32, u32> payload (outcome { okay,
        // fail(result<u32, u32>) }). retArea: u8 outer disc @0 +
        // Result at +4 (inner disc @4 + u32 value @8). Probes:
        //   call_outcome_disc       → outer disc
        //   call_outcome_inner_disc → Result disc (Ok=0/Err=1)
        //   call_outcome_value      → arm u32 value @8
        private static byte[] BuildVariantResultPayloadFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/varres-env@1.0.0" (24)
            // entity: "emit" (4)
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x76, 0x61, 0x72, 0x72, 0x65, 0x73, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x65, 0x6D, 0x69, 0x74,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_outcome_disc       (17): 20
            //   call_outcome_inner_disc (23): 26
            //   call_outcome_value      (18): 21
            // size = 1 + 20 + 26 + 21 = 68 = 0x44
            0x07, 0x44, 0x03,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6F, 0x75, 0x74,
            0x63, 0x6F, 0x6D, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x17,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6F, 0x75, 0x74,
            0x63, 0x6F, 0x6D, 0x65, 0x5F, 0x69, 0x6E, 0x6E,
            0x65, 0x72, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x12,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6F, 0x75, 0x74,
            0x63, 0x6F, 0x6D, 0x65, 0x5F, 0x76, 0x61, 0x6C,
            0x75, 0x65,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 → 12)
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x04,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,
            0x0B,
        };

        // Variant with Option<u32> payload (ping { idle,
        // retry(option<u32>) }). retArea: u8 outer disc @0 +
        // Option<u32> at +4 (inner disc @4 + u32 value @8).
        private static byte[] BuildVariantOptionPayloadFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/varopt-env@1.0.0" (24)
            // entity: "emit" (4)
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x76, 0x61, 0x72, 0x6F, 0x70, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x65, 0x6D, 0x69, 0x74,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_ping_disc       (14): 17
            //   call_ping_inner_disc (20): 23
            //   call_ping_value      (15): 18
            // size = 1 + 17 + 23 + 18 = 59 = 0x3B
            0x07, 0x3B, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x69, 0x6E,
            0x67, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x69, 0x6E,
            0x67, 0x5F, 0x69, 0x6E, 0x6E, 0x65, 0x72, 0x5F,
            0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x69, 0x6E,
            0x67, 0x5F, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 → 12)
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x04,    // load8_u offset=4 (inner disc)
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,    // i32.load offset=8 (value)
            0x0B,
        };

        // Variant with tuple<u32,u32> payload (pos { home,
        // point(tuple<u32,u32>) }). retArea: u8 disc + tuple
        // a@4, b@8. Probes disc, a@4, b@8.
        private static byte[] BuildVariantTuplePayloadFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/varpos-env@1.0.0" (24)
            // entity: "emit" (4)
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x76, 0x61, 0x72, 0x70, 0x6F, 0x73, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x65, 0x6D, 0x69, 0x74,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_pos_disc (13): 1+13+1+1 = 16
            //   call_pos_a    (10): 1+10+1+1 = 13
            //   call_pos_b    (10): 1+10+1+1 = 13
            // size = 1 + 16 + 13 + 13 = 43 = 0x2B
            0x07, 0x2B, 0x03,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x6F, 0x73,
            0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x0A,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x6F, 0x73,
            0x5F, 0x61,
            0x00, 0x02,
            0x0A,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x6F, 0x73,
            0x5F, 0x62,
            0x00, 0x03,
            // Code section: 3 bodies (each 11 → 12)
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,
            0x0B,
        };

        // Result<tuple<u32,u32>, u32> retArea: u8 disc @0 +
        // joined-flat (a@4, b@8) | u32@4 at retArea+4. Probes
        // disc, a@4, b@8 — for Err the b@8 slot is uninitialized.
        private static byte[] BuildResultTupleReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/restup-env@1.0.0" (24)
            // entity: "go" (2)
            // size = 1 + 1 + 24 + 1 + 2 + 2 = 31 = 0x1F
            0x02, 0x1F, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x74, 0x75, 0x70, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x02,
            0x67, 0x6F,
            0x00, 0x00,
            // Function section: 3 local funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            //   call_go_disc (12): 15
            //   call_go_a    (9):  12
            //   call_go_b    (9):  12
            // size = 1 + 15 + 12 + 12 = 40 = 0x28
            0x07, 0x28, 0x03,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x61,
            0x00, 0x02,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x6F, 0x5F,
            0x62,
            0x00, 0x03,
            // Code section: 3 bodies
            0x0A, 0x25, 0x03,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x08,
            0x0B,
        };

        // Variant with string-bearing payload (note { silent,
        // label(string) }). retArea: u8 disc @0 + (i32 ptr, i32 len)
        // at offset 4. Probes:
        //   call_note_disc        → disc byte
        //   call_note_first_byte  → first byte of label string
        //                           (chases ptr at retArea+4)
        // Same wasm shape as the Option<string> fixture (4 funcs
        // including cabi_realloc); only module/entity strings change.
        private static byte[] BuildVariantStringPayloadFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            0x01, 0x11, 0x03,
            0x60, 0x04, 0x7F, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/noteret-env@1.0.0" (25)
            // entity: "sound" (5)
            // size = 1 + 1 + 25 + 1 + 5 + 2 = 35 = 0x23
            0x02, 0x23, 0x01,
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6E, 0x6F, 0x74, 0x65, 0x72, 0x65, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x05,
            0x73, 0x6F, 0x75, 0x6E, 0x64,
            0x00, 0x01,
            // Function section: 3 local funcs
            //   funcs[0] = type 0 (cabi_realloc)
            //   funcs[1] = type 2 (call_note_disc)
            //   funcs[2] = type 2 (call_note_first_byte)
            0x03, 0x04, 0x03, 0x00, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Global: bump allocator at 32
            0x06, 0x06, 0x01,
            0x7F, 0x01, 0x41, 0x20, 0x0B,
            // Export section: 4 exports
            //   call_note_disc       (14): 1+14+1+1 = 17
            //   call_note_first_byte (20): 1+20+1+1 = 23
            //   cabi_realloc         (12): 15
            // size = 1 + 17 + 23 + 15 = 56 = 0x38
            0x07, 0x38, 0x03,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x74,
            0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x14,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x74,
            0x65, 0x5F, 0x66, 0x69, 0x72, 0x73, 0x74, 0x5F,
            0x62, 0x79, 0x74, 0x65,
            0x00, 0x03,
            0x0C,
            0x63, 0x61, 0x62, 0x69, 0x5F, 0x72, 0x65, 0x61,
            0x6C, 0x6C, 0x6F, 0x63,
            0x00, 0x01,
            // Code section: 3 bodies
            0x0A, 0x28, 0x03,
            0x0B, 0x00,
            0x23, 0x00,
            0x23, 0x00,
            0x20, 0x03,
            0x6A,
            0x24, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0E, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x2D, 0x00, 0x00,
            0x0B,
        };

        // Same shape as VariantPayloadReturn fixture but for the
        // mixed-width "event" variant — payloads u8 vs u32. The
        // probe loads the same i32 at retArea+4; for u8 payloads
        // only the low byte is meaningful (higher bytes start as
        // zero in fresh wasm memory). Module/entity differ only.
        private static byte[] BuildVariantMixedPayloadFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // module: "my:test/evtret-env@1.0.0" (24)
            // entity: "fire" (4)
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x76, 0x74, 0x72, 0x65, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x04,
            0x66, 0x69, 0x72, 0x65,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 2 exports
            //   call_evt_disc  (13): 1+13+1+1 = 16
            //   call_evt_value (14): 1+14+1+1 = 17
            // size = 1 + 16 + 17 = 34 = 0x22
            0x07, 0x22, 0x02,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x65, 0x76, 0x74,
            0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x65, 0x76, 0x74,
            0x5F, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies (same as code variant)
            0x0A, 0x19, 0x02,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
        };

        // (module
        //   (type $tPick (func (param i32)))      ;; pick(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_pick_disc → u8
        //   (import "my:test/varret-env@1.0.0" "pick"
        //           (func $imp (type $tPick)))
        //   (memory 1)
        //   (func (export "call_pick_disc") (result i32)
        //     i32.const 16    ;; retArea
        //     call $imp
        //     i32.const 16
        //     i32.load8_u))   ;; → disc
        //
        // Variant return wire form (all empty cases): just 1 byte
        // at retArea+0 — the disc index into the case list.
        private static byte[] BuildVariantReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 24 + 1 + 4 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            // module: "my:test/varret-env@1.0.0" (24)
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x76, 0x61, 0x72, 0x72, 0x65, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "pick" (4)
            0x04,
            0x70, 0x69, 0x63, 0x6B,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_pick_disc" (14) → func 1
            0x07, 0x12, 0x01,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x69, 0x63,
            0x6B, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            // Code: locals=0, i32.const 16, call 0, i32.const 16, i32.load8_u, end
            0x0A, 0x0D, 0x01, 0x0B,
            0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
        };

        // (module
        //   (type $tCreate (func (param i32)))    ;; create(retArea) → ()
        //   (type $tRead (func (param i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/socket-env@1.0.0" "create"
        //           (func $imp_create (type $tCreate)))
        //   (import "my:test/res-env@1.0.0" "[method]widget.read"
        //           (func $imp_read (type $tRead)))
        //   (memory 1)
        //   (func (export "call_create_disc") (result i32)
        //     i32.const 16; call $imp_create
        //     i32.const 16; i32.load8_u)
        //   (func (export "call_create_value") (result i32)
        //     i32.const 16; call $imp_create
        //     i32.const 16; i32.load offset=4)   ;; raw value @ retArea+4
        //   (func (export "call_create_ok_read") (result i32)
        //     i32.const 16; call $imp_create
        //     i32.const 16; i32.load offset=4    ;; handle (Ok side)
        //     call $imp_read)                    ;; → widget.read
        //
        // Three exports:
        //   call_create_disc  reads u8 disc
        //   call_create_value reads i32 raw at value slot
        //   call_create_ok_read calls widget.read on the value
        //                       interpreted as a handle (Ok side)
        private static byte[] BuildResultOwnReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            // 0: (i32) → void (4)
            // 1: (i32) → i32 (5)
            // 2: () → i32 (4)
            // size = 1 + 4 + 5 + 4 = 14 = 0x0E
            0x01, 0x0E, 0x03,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 2 imports
            // imp0: my:test/socket-env@1.0.0 (24) . create (6) : type 0
            //   = 1+24+1+6+2 = 34
            // imp1: my:test/res-env@1.0.0 (21) . [method]widget.read (19) : type 1
            //   = 1+21+1+19+2 = 44
            // size = 1 + 34 + 44 = 79 = 0x4F
            0x02, 0x4F, 0x02,
            // imp0
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x73, 0x6F, 0x63, 0x6B, 0x65, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x06,
            0x63, 0x72, 0x65, 0x61, 0x74, 0x65,
            0x00, 0x00,
            // imp1
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x72,
            0x65, 0x61, 0x64,
            0x00, 0x01,
            // Function section: 3 funcs of type 2
            0x03, 0x04, 0x03, 0x02, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 3 exports
            // call_create_disc (16):  1+16+1+1 = 19
            // call_create_value (17): 1+17+1+1 = 20
            // call_create_ok_read (19): 1+19+1+1 = 22
            // size = 1 + 19 + 20 + 22 = 62 = 0x3E
            0x07, 0x3E, 0x03,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x72, 0x65,
            0x61, 0x74, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x72, 0x65,
            0x61, 0x74, 0x65, 0x5F, 0x76, 0x61, 0x6C, 0x75,
            0x65,
            0x00, 0x03,
            0x13,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x72, 0x65,
            0x61, 0x74, 0x65, 0x5F, 0x6F, 0x6B, 0x5F, 0x72,
            0x65, 0x61, 0x64,
            0x00, 0x04,
            // Code section: 3 bodies
            // body0 (read disc):  locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load8_u(3) + end(1) = 11
            // body1 (read value): locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load(3) + end(1) = 11
            // body2 (read+call):  locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load(3) + call(2) + end(1) = 13
            // size = 1 + 12 + 12 + 14 = 39 = 0x27
            0x0A, 0x27, 0x03,
            // body 0:
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x2D, 0x00, 0x00,
            0x0B,
            // body 1:
            0x0B, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x0B,
            // body 2:
            0x0D, 0x00,
            0x41, 0x10, 0x10, 0x00,
            0x41, 0x10, 0x28, 0x02, 0x04,
            0x10, 0x01,
            0x0B,
        };

        // (module
        //   (type $tMaybe (func (param i32)))     ;; maybe-widget(retArea) → ()
        //   (type $tRead (func (param i32) (result i32))) ;; widget.read(h) → u32
        //   (type $tEntry (func (result i32)))    ;; call_*() → u32
        //   (import "my:test/optown-env@1.0.0" "maybe-widget"
        //           (func $imp_maybe (type $tMaybe)))
        //   (import "my:test/res-env@1.0.0" "[method]widget.read"
        //           (func $imp_read (type $tRead)))
        //   (memory 1)
        //   (func (export "call_maybe_disc") (result i32)
        //     i32.const 16; call $imp_maybe
        //     i32.const 16; i32.load8_u)
        //   (func (export "call_maybe_read_handle") (result i32)
        //     i32.const 16; call $imp_maybe
        //     i32.const 16; i32.load offset=4    ;; handle
        //     call $imp_read)                    ;; → widget.read(handle)
        //
        // The Some path mints a handle via Resources.AllocateResource
        // for the returned widget; the read call resolves the handle
        // back to the same instance and reads its value. Round-trip
        // success proves the disc + handle layout fired correctly.
        private static byte[] BuildOptionOwnReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 3 types
            // 0: (i32) → void (4)
            // 1: (i32) → i32 (5)
            // 2: () → i32 (4)
            // size = 1 + 4 + 5 + 4 = 14 = 0x0E
            0x01, 0x0E, 0x03,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 2 imports
            // imp0: my:test/optown-env@1.0.0 (24) . maybe-widget (12) : type 0
            //   = 1+24+1+12+2 = 40
            // imp1: my:test/res-env@1.0.0 (21) . [method]widget.read (19) : type 1
            //   = 1+21+1+19+2 = 44
            // size = 1 + 40 + 44 = 85 = 0x55
            0x02, 0x55, 0x02,
            // imp0
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x6F, 0x77, 0x6E, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x0C,
            0x6D, 0x61, 0x79, 0x62, 0x65, 0x2D, 0x77, 0x69,
            0x64, 0x67, 0x65, 0x74,
            0x00, 0x00,
            // imp1
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x72,
            0x65, 0x61, 0x64,
            0x00, 0x01,
            // Function section: 2 funcs of type 2
            0x03, 0x03, 0x02, 0x02, 0x02,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 2 exports
            // call_maybe_disc (15): 1+15+1+1 = 18
            // call_maybe_read_handle (22): 1+22+1+1 = 25
            // size = 1 + 18 + 25 = 44 = 0x2C
            0x07, 0x2C, 0x02,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x61, 0x79,
            0x62, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x02,
            0x16,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x61, 0x79,
            0x62, 0x65, 0x5F, 0x72, 0x65, 0x61, 0x64, 0x5F,
            0x68, 0x61, 0x6E, 0x64, 0x6C, 0x65,
            0x00, 0x03,
            // Code section: 2 bodies
            // body0 (read disc): locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load8_u(3) + end(1) = 11
            // body1 (read handle then call read): locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load(3) + call(2) + end(1) = 13
            // size = 1 + 12 + 14 = 27 = 0x1B
            0x0A, 0x1B, 0x02,
            // body 0:
            0x0B, 0x00,
            0x41, 0x10,
            0x10, 0x00,
            0x41, 0x10,
            0x2D, 0x00, 0x00,
            0x0B,
            // body 1:
            0x0D, 0x00,
            0x41, 0x10,
            0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x04,
            0x10, 0x01,
            0x0B,
        };

        // (module
        //   (type $tCompute (func (param i32)))   ;; compute(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_compute_disc/value → u32
        //   (import "my:test/resret-env@1.0.0" "compute"
        //           (func $imp (type $tCompute)))
        //   (memory 1)
        //   (func (export "call_compute_disc") (result i32)
        //     i32.const 16  ;; retArea
        //     call $imp
        //     i32.const 16
        //     i32.load8_u)  ;; → disc
        //   (func (export "call_compute_value") (result i32)
        //     i32.const 16
        //     call $imp
        //     i32.const 16
        //     i32.load offset=4))  ;; → value @ retArea+4
        //
        // Same retArea shape as Option<u32> (8 bytes), but disc=0
        // means Ok(value), disc=1 means Err(value).
        private static byte[] BuildResultReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32) → void, () → i32
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 24 + 1 + 7 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/resret-env@1.0.0" (24)
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x72, 0x65, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "compute" (7)
            0x07,
            0x63, 0x6F, 0x6D, 0x70, 0x75, 0x74, 0x65,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 2 exports
            // call_compute_disc (17): 1+17+1+1 = 20
            // call_compute_value (18): 1+18+1+1 = 21
            // size = 1 + 20 + 21 = 42 = 0x2A
            0x07, 0x2A, 0x02,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x12,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x70, 0x75, 0x74, 0x65, 0x5F, 0x76, 0x61, 0x6C,
            0x75, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies
            // body0 (read disc): locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load8_u(3) + end(1) = 11
            // body1 (read value): locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load align=2 offset=4(3) + end(1) = 11
            // size = 1 + 12 + 12 = 25 = 0x19
            0x0A, 0x19, 0x02,
            0x0B, 0x00,
            0x41, 0x10,
            0x10, 0x00,
            0x41, 0x10,
            0x2D, 0x00, 0x00,
            0x0B,
            0x0B, 0x00,
            0x41, 0x10,
            0x10, 0x00,
            0x41, 0x10,
            0x28, 0x02, 0x04,
            0x0B,
        };

        // (module
        //   (type $tMaybe (func (param i32)))     ;; maybe(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_maybe_disc/value → u32
        //   (import "my:test/optret-env@1.0.0" "maybe"
        //           (func $imp (type $tMaybe)))
        //   (memory 1)
        //   (func (export "call_maybe_disc") (result i32)
        //     i32.const 16  ;; retArea
        //     call $imp
        //     i32.const 16
        //     i32.load8_u)  ;; → disc
        //   (func (export "call_maybe_value") (result i32)
        //     i32.const 16
        //     call $imp
        //     i32.const 16
        //     i32.load offset=4)) ;; → value (at retArea+4)
        //
        // Option<u32> retArea: u8 disc @0 + u32 value @4 (aligned).
        // Two exports probe each field independently.
        private static byte[] BuildOptionReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: (i32) → void (4)
            // 1: () → i32 (4)
            // size = 1 + 4 + 4 = 9 = 0x09
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 24 + 1 + 5 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/optret-env@1.0.0" (24)
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x72, 0x65, 0x74, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "maybe" (5)
            0x05,
            0x6D, 0x61, 0x79, 0x62, 0x65,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: 2 exports
            // call_maybe_disc (15): 1+15+1+1 = 18
            // call_maybe_value (16): 1+16+1+1 = 19
            // size = 1 + 18 + 19 = 38 = 0x26
            0x07, 0x26, 0x02,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x61, 0x79,
            0x62, 0x65, 0x5F, 0x64, 0x69, 0x73, 0x63,
            0x00, 0x01,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x61, 0x79,
            0x62, 0x65, 0x5F, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies
            // body0 = locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load8_u(3) + end(1) = 11
            //   wait — i32.load8_u align=0 offset=0 = 3 bytes (0x2D, 0x00, 0x00)
            // body1 = locals(1) + i32.const(2) + call(2) + i32.const(2) + i32.load align=2 offset=4(3) + end(1) = 11
            // size = 1 + 12 + 12 = 25 = 0x19
            0x0A, 0x19, 0x02,
            // body 0:
            0x0B, 0x00,
            0x41, 0x10,             // i32.const 16
            0x10, 0x00,             // call 0
            0x41, 0x10,             // i32.const 16
            0x2D, 0x00, 0x00,       // i32.load8_u align=0 offset=0
            0x0B,
            // body 1:
            0x0B, 0x00,
            0x41, 0x10,             // i32.const 16
            0x10, 0x00,             // call 0
            0x41, 0x10,             // i32.const 16
            0x28, 0x02, 0x04,       // i32.load align=2 offset=4
            0x0B,
        };

        // (module
        //   (type $tMake (func (result i32)))         ;; make-widget() → handle
        //   (type $tRead (func (param i32) (result i32))) ;; widget.read(h) → u32
        //   (import "my:test/own-ret-env@1.0.0" "make-widget"
        //           (func $imp_make (type $tMake)))
        //   (import "my:test/res-env@1.0.0" "[method]widget.read"
        //           (func $imp_read (type $tRead)))
        //   (func (export "call_make_read") (result i32)
        //     call $imp_make    ;; → handle
        //     call $imp_read))  ;; → u32 from that handle
        //
        // make-widget allocates a fresh widget instance via the
        // host factory; the IL allocates a handle (Resources.
        // AllocateResource) and returns it. The subsequent
        // resource-method call resolves that handle and reads the
        // seed value back. Round-trip success proves the own<R>
        // return path mints handles correctly.
        private static byte[] BuildOwnReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: () → i32 (4)
            // 1: (i32) → i32 (5)
            // size = 1 + 4 + 5 = 10 = 0x0A
            0x01, 0x0A, 0x02,
            0x60, 0x00, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            // Import section: 2 imports
            // imp0: my:test/own-ret-env@1.0.0 (25) . make-widget (11) : type 0
            //   = 1+25+1+11+2 = 40
            // imp1: my:test/res-env@1.0.0 (21) . [method]widget.read (19) : type 1
            //   = 1+21+1+19+2 = 44
            // size = 1 + 40 + 44 = 85 = 0x55
            0x02, 0x55, 0x02,
            // imp0
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x77, 0x6E, 0x2D, 0x72, 0x65, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x0B,
            0x6D, 0x61, 0x6B, 0x65, 0x2D, 0x77, 0x69, 0x64,
            0x67, 0x65, 0x74,
            0x00, 0x00,
            // imp1
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x72,
            0x65, 0x61, 0x64,
            0x00, 0x01,
            // Function section: 1 func of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export: "call_make_read" (14) → func 2 (after 2 imports)
            0x07, 0x12, 0x01,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x61, 0x6B,
            0x65, 0x5F, 0x72, 0x65, 0x61, 0x64,
            0x00, 0x02,
            // Code: locals=0, call 0, call 1, end
            // body = locals(1) + call(2)*2 + end(1) = 6
            0x0A, 0x08, 0x01, 0x06,
            0x00, 0x10, 0x00, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $tMakePair (func (param i32)))  ;; make-pair(retArea) → ()
        //   (type $tEntry (func (result i32)))    ;; call_pair_sum() → u32
        //   (import "my:test/tup-ret-env@1.0.0" "make-pair"
        //           (func $imp (type $tMakePair)))
        //   (memory 1)
        //   (func (export "call_pair_sum") (result i32)
        //     i32.const 64    ;; retArea ptr
        //     call $imp       ;; → host writes (Item1:u32@0, Item2:u32@4)
        //     i32.const 64
        //     i32.load        ;; → Item1
        //     i32.const 64
        //     i32.load offset=4
        //     i32.add)        ;; → Item1 + Item2
        //
        // tuple<u32, u32> retArea write: 8 bytes (2 i32s, no
        // padding). Export reads both back via i32.load and adds —
        // assertion proves both fields landed at the right offset.
        private static byte[] BuildTupleReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: (i32) → void (4)
            // 1: () → i32 (4)
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 25 + 1 + 9 + 2 = 39 = 0x27
            0x02, 0x27, 0x01,
            // module: "my:test/tup-ret-env@1.0.0" (25)
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x74, 0x75, 0x70, 0x2D, 0x72, 0x65, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "make-pair" (9)
            0x09,
            0x6D, 0x61, 0x6B, 0x65, 0x2D, 0x70, 0x61, 0x69, 0x72,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_pair_sum" (13) → func 1
            0x07, 0x11, 0x01,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x61, 0x69,
            0x72, 0x5F, 0x73, 0x75, 0x6D,
            0x00, 0x01,
            // Code section: 1 body
            // retArea = 16 (single-byte signed LEB128 — value < 0x40
            // so bit 6 clear, no sign-extension surprises).
            // body = locals(1) + i32.const(2) + call(2) + i32.const(2)
            //      + i32.load(3) + i32.const(2) + i32.load(3) + i32.add(1) + end(1) = 17
            0x0A, 0x13, 0x01, 0x11,
            0x00,
            0x41, 0x10,              // i32.const 16
            0x10, 0x00,              // call 0
            0x41, 0x10,              // i32.const 16
            0x28, 0x02, 0x00,        // i32.load align=2 offset=0
            0x41, 0x10,              // i32.const 16
            0x28, 0x02, 0x04,        // i32.load align=2 offset=4
            0x6A,                    // i32.add
            0x0B,
        };

        // (module
        //   (type $tNow (func (param i32)))    ;; now(retArea) → ()
        //   (type $tEntry (func (result i64))) ;; call_now() → seconds (u64)
        //   (import "my:test/agg-env@1.0.0" "now" (func $imp (type $tNow)))
        //   (memory 1)
        //   (func (export "call_now_seconds") (result i64)
        //     i32.const 32   ;; retArea ptr (away from any active data)
        //     call $imp      ;; → host writes (seconds:u64@0, nanoseconds:u32@8)
        //     i32.const 32
        //     i64.load))     ;; → seconds
        //
        // 12-byte aggregate write at offset 32. Export reads
        // seconds back via i64.load — proves the host serialized
        // it correctly via PrimitiveStore.StoreU64 at retArea+0.
        private static byte[] BuildAggregateReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: (i32) → void (4 bytes)
            // 1: () → i64 (4 bytes)
            // size = 1 + 4 + 4 = 9 = 0x09
            0x01, 0x09, 0x02,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7E,
            // Import section
            // size = 1 + 1 + 21 + 1 + 3 + 2 = 29 = 0x1D
            0x02, 0x1D, 0x01,
            // module: "my:test/agg-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x61, 0x67, 0x67, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "now" (3)
            0x03,
            0x6E, 0x6F, 0x77,
            0x00, 0x00,
            // Function section: 1 local func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_now_seconds" (16) → func 1 (after 1 import)
            0x07, 0x14, 0x01,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x77,
            0x5F, 0x73, 0x65, 0x63, 0x6F, 0x6E, 0x64, 0x73,
            0x00, 0x01,
            // Code section: 1 body
            // body = locals(1) + i32.const 32(2) + call(2) + i32.const 32(2) + i64.load align=3 offset=0(3) + end(1) = 11
            // 0x29 = i64.load — opcode requires alignment & offset varuint
            0x0A, 0x0D, 0x01, 0x0B,
            0x00,
            0x41, 0x20,         // i32.const 32
            0x10, 0x00,         // call 0
            0x41, 0x20,         // i32.const 32
            0x29, 0x03, 0x00,   // i64.load align=3 offset=0
            0x0B,
        };

        // (module
        //   (type $tSig (func (param i32 i32 i32) (result i32))) ;; (disc, s1, s2) → u32
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/var-env@1.0.0" "take-signal" (func $imp (type $tSig)))
        //   (memory 1)
        //   (data (i32.const 0) "hi")           ;; 2-byte string
        //   (func (export "call_tick") (result i32)
        //     i32.const 0   ;; disc = SignalTick (case 0)
        //     i32.const 0   ;; slot1 (ignored)
        //     i32.const 0   ;; slot2 (ignored)
        //     call $imp)
        //   (func (export "call_beep") (result i32)
        //     i32.const 1   ;; disc = SignalBeep (case 1)
        //     i32.const 7   ;; payload value
        //     i32.const 0   ;; slot2 (ignored)
        //     call $imp)
        //   (func (export "call_name") (result i32)
        //     i32.const 2   ;; disc = SignalName (case 2)
        //     i32.const 0   ;; ptr (memory offset 0)
        //     i32.const 2   ;; len ("hi")
        //     call $imp))
        //
        // Wire = (disc, then joined-flat over max payload). Beep's
        // u32 payload sits in slot1; Name's (ptr, len) sits in
        // slot1+slot2; Tick has no payload so the joined-flat
        // slots are inert padding for it.
        private static byte[] BuildVariantParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            //   0: (i32 i32 i32) → i32 (7 bytes)
            //   1: () → i32 (4 bytes)
            // size = 1 + 7 + 4 = 12 = 0x0C
            0x01, 0x0C, 0x02,
            0x60, 0x03, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 21 + 1 + 11 + 2 = 37 = 0x25
            0x02, 0x25, 0x01,
            // module: "my:test/var-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x76, 0x61, 0x72, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-signal" (11)
            0x0B,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x73, 0x69, 0x67,
            0x6E, 0x61, 0x6C,
            0x00, 0x00,
            // Function section: 3 funcs of type 1
            0x03, 0x04, 0x03, 0x01, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 3 exports
            // Each: namelen(1) + name(N) + descByte(1) + funcIdx(1)
            // call_tick (9): 1+9+1+1 = 12
            // call_beep (9): 12
            // call_name (9): 12
            // size = count(1) + 12*3 = 37 = 0x25
            0x07, 0x25, 0x03,
            0x09, 0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x69, 0x63, 0x6B, 0x00, 0x01,
            0x09, 0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x62, 0x65, 0x65, 0x70, 0x00, 0x02,
            0x09, 0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x61, 0x6D, 0x65, 0x00, 0x03,
            // Code section: 3 bodies, each = locals(1) + 3*i32const(2) + call(2) + end(1) = 10
            // size = 1 + 11*3 = 34 = 0x22
            0x0A, 0x22, 0x03,
            // call_tick: i32.const 0, i32.const 0, i32.const 0, call 0
            0x0A, 0x00, 0x41, 0x00, 0x41, 0x00, 0x41, 0x00, 0x10, 0x00, 0x0B,
            // call_beep: i32.const 1, i32.const 7, i32.const 0, call 0
            0x0A, 0x00, 0x41, 0x01, 0x41, 0x07, 0x41, 0x00, 0x10, 0x00, 0x0B,
            // call_name: i32.const 2, i32.const 0, i32.const 2, call 0
            0x0A, 0x00, 0x41, 0x02, 0x41, 0x00, 0x41, 0x02, 0x10, 0x00, 0x0B,
            // Data: "hi" at offset 0 (2 bytes)
            0x0B, 0x08, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x02,
            0x68, 0x69,
        };

        // (module
        //   (type $t0 (func (param i32 i32)))   ;; void TakeStrs(string[])
        //   (type $t1 (func))                    ;; void call_print_strs()
        //   (import "my:test/strs-env@1.0.0" "take-strs" (func $imp (type $t0)))
        //   (memory 1)
        //   ;; data at offset 0: 2 (ptr,len) pairs + "hi" + "hello"
        //   ;;   offset 0:  ptr=16 (i32 LE), len=2 (i32 LE)
        //   ;;   offset 8:  ptr=18 (i32 LE), len=5 (i32 LE)
        //   ;;   offset 16: "hi"
        //   ;;   offset 18: "hello"
        //   (data (i32.const 0) "\10\00\00\00\02\00\00\00\12\00\00\00\05\00\00\00hihello")
        //   (func (export "call_print_strs")
        //     i32.const 0    ;; listPtr
        //     i32.const 2    ;; count
        //     call $imp))
        //
        // ListMarshal.LiftStringList(memory, listPtr=0, count=2)
        // walks 2 (ptr, len) pairs starting at offset 0 and lifts
        // "hi" and "hello" via UTF-8.
        private static byte[] BuildStringListParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32 i32) → void, () → void
            0x01, 0x09, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section
            // size = 1 + 1 + 22 + 1 + 9 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/strs-env@1.0.0" (22)
            0x16,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x73, 0x74, 0x72, 0x73, 0x2D, 0x65, 0x6E, 0x76,
            0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-strs" (9)
            0x09,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x73, 0x74, 0x72, 0x73,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_print_strs" (15) → func 1
            0x07, 0x13, 0x01,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x72, 0x69,
            0x6E, 0x74, 0x5F, 0x73, 0x74, 0x72, 0x73,
            0x00, 0x01,
            // Code: locals=0, i32.const 0, i32.const 2, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x00, 0x41, 0x02, 0x10, 0x00, 0x0B,
            // Data: active mem 0, offset 0, 23 bytes (2 pairs * 8B + "hi"(2) + "hello"(5))
            0x0B, 0x1D, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x17,
            0x10, 0x00, 0x00, 0x00,  // ptr=16
            0x02, 0x00, 0x00, 0x00,  // len=2
            0x12, 0x00, 0x00, 0x00,  // ptr=18
            0x05, 0x00, 0x00, 0x00,  // len=5
            0x68, 0x69,              // "hi"
            0x68, 0x65, 0x6C, 0x6C, 0x6F,  // "hello"
        };

        // (module
        //   (type $t0 (func (param i32 i32)))   ;; void PrintInts(int[])
        //   (type $t1 (func))                    ;; void call_print_ints()
        //   (import "my:test/int-env@1.0.0" "print-ints" (func $imp (type $t0)))
        //   (memory 1)
        //   (data (i32.const 0) "\0a\00\00\00\14\00\00\00\1e\00\00\00\28\00\00\00")
        //   (func (export "call_print_ints")
        //     i32.const 0    ;; ptr
        //     i32.const 4    ;; element count (NOT byte length)
        //     call $imp))
        //
        // Same wire shape as byte[] (ptr + len), but len is the
        // ELEMENT count (4) and the bytes occupy 4*4=16 bytes in
        // memory. Direct-linked emit uses ListMarshal.LiftPrim<int>
        // (resolved via ResolveLiftPrimMethod cache) to materialize
        // the int[].
        private static byte[] BuildIntArrayParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section
            0x01, 0x09, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section
            // size = 1 + 1 + 21 + 1 + 10 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/int-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x69, 0x6E, 0x74, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "print-ints" (10)
            0x0A,
            0x70, 0x72, 0x69, 0x6E, 0x74, 0x2D, 0x69, 0x6E,
            0x74, 0x73,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_print_ints" (15) → func 1
            0x07, 0x13, 0x01,
            0x0F,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x72, 0x69,
            0x6E, 0x74, 0x5F, 0x69, 0x6E, 0x74, 0x73,
            0x00, 0x01,
            // Code: locals=0, i32.const 0, i32.const 4, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x00, 0x41, 0x04, 0x10, 0x00, 0x0B,
            // Data: 1 active segment, mem 0, offset 0,
            // 16 bytes = 4 little-endian i32: 10, 20, 30, 40
            0x0B, 0x16, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x10,
            0x0A, 0x00, 0x00, 0x00,
            0x14, 0x00, 0x00, 0x00,
            0x1E, 0x00, 0x00, 0x00,
            0x28, 0x00, 0x00, 0x00,
        };

        // (module
        //   (type $t (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/res-env@1.0.0" "[static]widget.combine-static"
        //     (func $imp (type $t)))
        //   (func (export "call_combine") (result i32)
        //     i32.const 5
        //     i32.const 7
        //     call $imp))   ;; → CombineStatic(5, 7) = 5 + 7*100 = 705
        //
        // Multi-arg static: 2 i32 slots in, 1 i32 out, no `this`.
        private static byte[] BuildStaticMultiArgFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: (i32 i32) → i32 (6)
            // 1: () → i32 (4)
            // size = 1 + 6 + 4 = 11 = 0x0B
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 21 + 1 + 29 + 2 = 55 = 0x37
            0x02, 0x37, 0x01,
            // module: "my:test/res-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "[static]widget.combine-static" (29)
            0x1D,
            0x5B, 0x73, 0x74, 0x61, 0x74, 0x69, 0x63, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x63,
            0x6F, 0x6D, 0x62, 0x69, 0x6E, 0x65, 0x2D, 0x73,
            0x74, 0x61, 0x74, 0x69, 0x63,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_combine" (12) → func 1
            0x07, 0x10, 0x01,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x62, 0x69, 0x6E, 0x65,
            0x00, 0x01,
            // Code: locals=0, i32.const 5, i32.const 7, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x05, 0x41, 0x07, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $t (func (result i32)))
        //   (import "my:test/res-env@1.0.0" "[static]widget.default-value"
        //     (func $imp (type $t)))
        //   (func (export "call_def") (result i32) call $imp))
        //
        // Static resource method: no leading handle, no `this`. The
        // emitted IL emits `call IWidget::DefaultValue` (static
        // dispatch on the default static interface method).
        private static byte[] BuildStaticResourceFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: () → i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = count(1) + modlen(1) + mod(21) + entlen(1) + ent(28) + desc(2) = 54 = 0x36
            0x02, 0x36, 0x01,
            // module: "my:test/res-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "[static]widget.default-value" (28)
            0x1C,
            0x5B, 0x73, 0x74, 0x61, 0x74, 0x69, 0x63, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x64,
            0x65, 0x66, 0x61, 0x75, 0x6C, 0x74, 0x2D, 0x76,
            0x61, 0x6C, 0x75, 0x65,
            // desc: func, type 0
            0x00, 0x00,
            // Function section: 1 func of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_def" (8) → func 1
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x64, 0x65, 0x66,
            0x00, 0x01,
            // Code section: locals=0, call 0, end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $t0 (func (result i32)))           ;; constructor + export
        //   (type $t1 (func (param i32) (result i32))) ;; read
        //   (import "my:test/res-env@1.0.0" "[constructor]widget"
        //     (func $imp_ctor (type $t0)))
        //   (import "my:test/res-env@1.0.0" "[method]widget.read"
        //     (func $imp_read (type $t1)))
        //   (func (export "call_create_read") (result i32)
        //     call $imp_ctor          ;; leaves handle on stack
        //     call $imp_read))         ;; consumes handle, returns value
        //
        // Constructor: zero-arg, returns i32 handle. The IL invokes
        // the static factory `IWidget::Create()`, allocates a handle
        // via Resources.AllocateResource(typeof(IWidget), instance),
        // returns the handle as the wasm i32 result. The instance
        // method that immediately follows resolves the freshly-
        // allocated handle and reads the underlying value.
        private static byte[] BuildConstructorAndInstanceFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            0x01, 0x0A, 0x02,
            0x60, 0x00, 0x01, 0x7F,            // type 0: () → i32
            0x60, 0x01, 0x7F, 0x01, 0x7F,      // type 1: (i32) → i32
            // Import section: 2 imports
            // size = count(1) + import0(44) + import1(44) = 89 = 0x59
            0x02, 0x59, 0x02,
            // Import 0: ".../res-env@1.0.0" "[constructor]widget" : type 0
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x63, 0x6F, 0x6E, 0x73, 0x74, 0x72, 0x75,
            0x63, 0x74, 0x6F, 0x72, 0x5D, 0x77, 0x69, 0x64,
            0x67, 0x65, 0x74,
            0x00, 0x00,
            // Import 1: ".../res-env@1.0.0" "[method]widget.read" : type 1
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x77, 0x69, 0x64, 0x67, 0x65, 0x74, 0x2E, 0x72,
            0x65, 0x61, 0x64,
            0x00, 0x01,
            // Function section: 1 func of type 0 (() → i32)
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_create_read" (16) → func 2
            0x07, 0x14, 0x01,
            0x10,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x72, 0x65,
            0x61, 0x74, 0x65, 0x5F, 0x72, 0x65, 0x61, 0x64,
            0x00, 0x02,
            // Code section: locals=0, call 0, call 1, end
            0x0A, 0x08, 0x01, 0x06,
            0x00, 0x10, 0x00, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $t (func (result i32)))
        //   (import "my:test/enumret-env@1.0.0" "pick"
        //           (func $imp (type $t)))
        //   (func (export "call_pick") (result i32) call $imp))
        //
        // Color : byte enum returned via the primitive-return path —
        // the CLR enum value shares the i32 stack form with its
        // underlying byte/u8, so the typed callvirt's return slot
        // is the wasm i32 result directly.
        private static byte[] BuildEnumReturnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: () → i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 25 + 1 + 4 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/enumret-env@1.0.0" (25)
            0x19,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x75, 0x6D, 0x72, 0x65, 0x74, 0x2D,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "pick" (4)
            0x04,
            0x70, 0x69, 0x63, 0x6B,
            0x00, 0x00,
            // Function section: 1 func of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export: "call_pick" (9) → func 1
            0x07, 0x0D, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x69, 0x63, 0x6B,
            0x00, 0x01,
            // Code: locals=0, call 0, end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tEf (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/enum-env@1.0.0" "take-ef"
        //           (func $imp (type $tEf)))
        //   (func (export "call_ef") (result i32)
        //     i32.const 1   ;; Color.Green (=1)
        //     i32.const 5   ;; Perms.Read | Perms.Exec (=5)
        //     call $imp))   ;; → EnumProbe: (1<<16)|5 = 0x10005
        //
        // Both wasm slots are i32. The CLR side has Color (byte
        // underlying) and Perms (uint underlying) — direct-linked
        // emit treats both as their underlying type for slot count
        // and conversion: Color gets conv.u1 narrowing (matches
        // the byte underlying); Perms shares i32 stack form with
        // uint so no conv needed.
        private static byte[] BuildEnumFlagsParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 22 + 1 + 7 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/enum-env@1.0.0" (22)
            0x16,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x75, 0x6D, 0x2D, 0x65, 0x6E, 0x76,
            0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-ef" (7)
            0x07,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x65, 0x66,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_ef" (7) → func 1
            0x07, 0x0B, 0x01,
            0x07,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x65, 0x66,
            0x00, 0x01,
            // Code: locals=0, i32.const 1, i32.const 5, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x01, 0x41, 0x05, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tRec (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/rec-env@1.0.0" "take-point"
        //           (func $imp (type $tRec)))
        //   (func (export "call_point") (result i32)
        //     i32.const 0x12   ;; X
        //     i32.const 0x34   ;; Y
        //     call $imp))      ;; → PointHasher: (0x12<<8)|0x34 = 0x1234
        //
        // record point { x: u32, y: u32 } wire form: 2 i32 slots.
        // Direct-linked emit constructs Point via parameterless
        // ctor, then sets X = lift_at_cursor_0, Y = lift_at_cursor_1
        // before pushing the instance for the typed callvirt.
        private static byte[] BuildRecordParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 10 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/rec-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x63, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-point" (10)
            0x0A,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x70, 0x6F, 0x69,
            0x6E, 0x74,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_point" (10) → func 1
            0x07, 0x0E, 0x01,
            0x0A,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x6F, 0x69,
            0x6E, 0x74,
            0x00, 0x01,
            // Code: locals=0, i32.const 0x12, i32.const 0x34, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x12, 0x41, 0x34, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tMtup (func (param i32 i32 i32) (result i32))) ;; (Item1, ptr, len) → u32
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/mtup-env@1.0.0" "take-mtup"
        //           (func $imp (type $tMtup)))
        //   (memory 1)
        //   (data (i32.const 0) "hi")
        //   (func (export "call_mtup") (result i32)
        //     i32.const 5      ;; Item1
        //     i32.const 0      ;; ptr
        //     i32.const 2      ;; len ("hi")
        //     call $imp))      ;; → MixedTupleProbe yields 5*1000+2 = 5002
        //
        // tuple<u32, string> wire = i32 + i32 + i32 = 3 slots.
        private static byte[] BuildMixedTupleParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // 0: (i32 i32 i32) → i32 (7 bytes)
            // 1: () → i32 (4 bytes)
            // size = 1 + 7 + 4 = 12 = 0x0C
            0x01, 0x0C, 0x02,
            0x60, 0x03, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section
            // size = 1 + 1 + 22 + 1 + 9 + 2 = 36 = 0x24
            0x02, 0x24, 0x01,
            // module: "my:test/mtup-env@1.0.0" (22)
            0x16,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6D, 0x74, 0x75, 0x70, 0x2D, 0x65, 0x6E, 0x76,
            0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-mtup" (9)
            0x09,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x6D, 0x74, 0x75, 0x70,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_mtup" (9) → func 1
            0x07, 0x0D, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x74, 0x75, 0x70,
            0x00, 0x01,
            // Code: locals=0, i32.const 5, i32.const 0, i32.const 2, call 0, end
            0x0A, 0x0C, 0x01, 0x0A,
            0x00,
            0x41, 0x05,
            0x41, 0x00,
            0x41, 0x02,
            0x10, 0x00,
            0x0B,
            // Data: "hi" at offset 0
            0x0B, 0x08, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x02,
            0x68, 0x69,
        };

        // (module
        //   (type $tTup (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/tup-env@1.0.0" "take-tup"
        //           (func $imp (type $tTup)))
        //   (func (export "call_tup") (result i32)
        //     i32.const 0x05   ;; t.Item1
        //     i32.const 0x07   ;; t.Item2
        //     call $imp))      ;; → TupleAdder yields (5<<8)|7 = 0x507
        //
        // tuple<u32, u32> wire = 2 i32 slots concatenated. Direct-
        // linked emit lifts each element via recursive
        // EmitLiftForType (primitives in this case), then calls
        // ValueTuple<uint, uint>'s 2-arg ctor.
        private static byte[] BuildTupleParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32) → i32 (6 bytes)
            // type 1: () → i32 (4 bytes)
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 8 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/tup-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x74, 0x75, 0x70, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-tup" (8)
            0x08,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x74, 0x75, 0x70,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_tup" (8) → func 1
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x75, 0x70,
            0x00, 0x01,
            // Code: locals=0, i32.const 5, i32.const 7, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x05, 0x41, 0x07, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tRes (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/res-env@1.0.0" "take-result"
        //           (func $imp (type $tRes)))
        //   (func (export "call_ok") (result i32)
        //     i32.const 0     ;; disc=Ok
        //     i32.const 0x42  ;; payload
        //     call $imp)
        //   (func (export "call_err") (result i32)
        //     i32.const 1     ;; disc=Err
        //     i32.const 0x55  ;; payload
        //     call $imp))
        //
        // result<u32, u32> wire form: (i32 disc, i32 payload).
        // ResultProbe encodes the side+payload back as i32 so the
        // test can assert both branches from the export's return.
        private static byte[] BuildResultParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32) → i32 (6 bytes)
            // type 1: () → i32 (4 bytes)
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 11 + 2 = 37 = 0x25
            0x02, 0x25, 0x01,
            // module: "my:test/res-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-result" (11)
            0x0B,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x72, 0x65, 0x73,
            0x75, 0x6C, 0x74,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Export section: 2 exports
            // size = 1 + (1+7+1+1) + (1+8+1+1) = 22 = 0x16
            0x07, 0x16, 0x02,
            0x07,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6F, 0x6B,
            0x00, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x65, 0x72, 0x72,
            0x00, 0x02,
            // Code section: 2 bodies
            // Payloads must fit single-byte signed LEB128 (i.e.
            // value < 0x40 / bit 6 clear) to encode in one byte.
            // Use 0x12 (Ok) and 0x34 (Err) which round-trip cleanly.
            // size = 1 + 9 + 9 = 19 = 0x13
            0x0A, 0x13, 0x02,
            // call_ok:  locals=0, i32.const 0, i32.const 0x12, call 0, end
            0x08, 0x00, 0x41, 0x00, 0x41, 0x12, 0x10, 0x00, 0x0B,
            // call_err: locals=0, i32.const 1, i32.const 0x34, call 0, end
            0x08, 0x00, 0x41, 0x01, 0x41, 0x34, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tOptOwn (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/own-env@1.0.0" "take-opt-widget"
        //           (func $imp (type $tOptOwn)))
        //   (func (export "call_take_some") (result i32)
        //     i32.const 1   ;; disc = Some
        //     i32.const 7   ;; handle
        //     call $imp)
        //   (func (export "call_take_none") (result i32)
        //     i32.const 0   ;; disc = None
        //     i32.const 0   ;; handle (ignored)
        //     call $imp))
        //
        // option<own<widget>> wire form: (i32 disc, i32 handle).
        // Direct-linked emit recurses into EmitLiftForType for the
        // inner own<widget>, threading the resourcesType so the
        // resource-handle lookup can fire inside the Some branch.
        private static byte[] BuildOptionOwnFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32) → i32 (6 bytes)
            // type 1: () → i32 (4 bytes)
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 15 + 2 = 41 = 0x29
            0x02, 0x29, 0x01,
            // module: "my:test/own-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x77, 0x6E, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-opt-widget" (15)
            0x0F,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x6F, 0x70, 0x74,
            0x2D, 0x77, 0x69, 0x64, 0x67, 0x65, 0x74,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Export section: 2 exports
            // size = 1 + (1+14+1+1) + (1+14+1+1) = 35 = 0x23
            0x07, 0x23, 0x02,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x61, 0x6B,
            0x65, 0x5F, 0x73, 0x6F, 0x6D, 0x65,
            0x00, 0x01,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x61, 0x6B,
            0x65, 0x5F, 0x6E, 0x6F, 0x6E, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies
            // size = 1 + 9 + 9 = 19 = 0x13
            0x0A, 0x13, 0x02,
            0x08, 0x00, 0x41, 0x01, 0x41, 0x07, 0x10, 0x00, 0x0B,
            0x08, 0x00, 0x41, 0x00, 0x41, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tOwn (func (param i32) (result i32)))  ;; uint TakeWidget(IWidget)
        //   (type $tEntry (func (result i32)))             ;; uint call_take()
        //   (import "my:test/own-env@1.0.0" "take-widget"
        //           (func $imp (type $tOwn)))
        //   (func (export "call_take") (result i32)
        //     i32.const 7   ;; the resource handle the test pre-registers
        //     call $imp))
        //
        // own<widget> wire is a single i32 (the handle). Direct-
        // linked emit detects IWidget as a resource interface from
        // the resolver and emits the same lookup machinery as the
        // resource-instance-method `this`: load Resources, call
        // GetResource(typeof(IWidget), handle), cast to IWidget,
        // pass as the typed param.
        private static byte[] BuildOwnParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32) → i32 (5 bytes), type 1: () → i32 (4 bytes)
            // size = count(1) + type0(5) + type1(4) = 10 = 0x0A
            0x01, 0x0A, 0x02,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 11 + 2 = 37 = 0x25
            0x02, 0x25, 0x01,
            // module: "my:test/own-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x77, 0x6E, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-widget" (11)
            0x0B,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x77, 0x69, 0x64,
            0x67, 0x65, 0x74,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_take" (9) → func 1
            0x07, 0x0D, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x61, 0x6B, 0x65,
            0x00, 0x01,
            // Code: locals=0, i32.const 7 (handle), call 0, end
            0x0A, 0x08, 0x01, 0x06,
            0x00, 0x41, 0x07, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tOptStr (func (param i32 i32 i32)))  ;; void TakeOptStr(Option<string>)
        //   (type $tEntry (func))
        //   (import "my:test/optstr-env@1.0.0" "take-optstr"
        //           (func $imp (type $tOptStr)))
        //   (memory 1)
        //   (data (i32.const 0) "hello")
        //   (func (export "call_some_str")
        //     i32.const 1; i32.const 0; i32.const 5; call $imp)
        //   (func (export "call_none_str")
        //     i32.const 0; i32.const 0; i32.const 0; call $imp))
        //
        // option<string> wire is (i32 disc, i32 ptr, i32 len) = 3
        // slots. The Some path lifts via StringMarshal.LiftUtf8
        // (recursing into EmitLiftForType for the inner string)
        // then constructs Option<string>::Some(s).
        private static byte[] BuildOptionStringParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32 i32) → void  (6 bytes)
            // type 1: () → void  (3 bytes)
            0x01, 0x0A, 0x02,
            0x60, 0x03, 0x7F, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section
            // size = 1 + 1 + 24 + 1 + 11 + 2 = 40 = 0x28
            0x02, 0x28, 0x01,
            // module: "my:test/optstr-env@1.0.0" (24)
            0x18,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x73, 0x74, 0x72, 0x2D, 0x65,
            0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-optstr" (11)
            0x0B,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x6F, 0x70, 0x74,
            0x73, 0x74, 0x72,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: 2 exports
            // size = 1 + (1+13+1+1) + (1+13+1+1) = 33 = 0x21
            0x07, 0x21, 0x02,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x73, 0x6F, 0x6D,
            0x65, 0x5F, 0x73, 0x74, 0x72,
            0x00, 0x01,
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x6E,
            0x65, 0x5F, 0x73, 0x74, 0x72,
            0x00, 0x02,
            // Code section: 2 bodies
            // size = 1 + 11 + 11 = 23 = 0x17
            0x0A, 0x17, 0x02,
            // body 0 (call_some_str): locals=0, i32.const 1, i32.const 0, i32.const 5, call 0, end (10 bytes)
            0x0A, 0x00, 0x41, 0x01, 0x41, 0x00, 0x41, 0x05, 0x10, 0x00, 0x0B,
            // body 1 (call_none_str): locals=0, i32.const 0, i32.const 0, i32.const 0, call 0, end (10 bytes)
            0x0A, 0x00, 0x41, 0x00, 0x41, 0x00, 0x41, 0x00, 0x10, 0x00, 0x0B,
            // Data: "hello" at offset 0
            0x0B, 0x0B, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x05,
            0x68, 0x65, 0x6C, 0x6C, 0x6F,
        };

        // (module
        //   (type $tOpt (func (param i32 i32)))   ;; void TakeOpt(Option<u32>)
        //   (type $tEntry (func))                  ;; void call_some/none()
        //   (import "my:test/opt-env@1.0.0" "take-opt" (func $imp (type $tOpt)))
        //   (func (export "call_some")
        //     i32.const 1   ;; disc = Some
        //     i32.const 42  ;; value
        //     call $imp)
        //   (func (export "call_none")
        //     i32.const 0   ;; disc = None
        //     i32.const 0   ;; value (ignored when disc=0)
        //     call $imp))
        //
        // option<u32> wire form is (i32 disc, i32 value). Direct-
        // linked emit branches on the disc local: if non-zero,
        // calls Option<uint>::Some(value); if zero, fetches
        // Option<uint>::None.
        private static byte[] BuildOptionParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            0x01, 0x09, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 8 + 2 = 34 = 0x22
            0x02, 0x22, 0x01,
            // module: "my:test/opt-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x6F, 0x70, 0x74, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "take-opt" (8)
            0x08,
            0x74, 0x61, 0x6B, 0x65, 0x2D, 0x6F, 0x70, 0x74,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Export section: 2 exports
            // size = 1 + (1+9+1+1) + (1+9+1+1) = 25 = 0x19
            0x07, 0x19, 0x02,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x73, 0x6F, 0x6D, 0x65,
            0x00, 0x01,
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x6E, 0x65,
            0x00, 0x02,
            // Code section: 2 bodies
            // size = 1 + 9 + 9 = 19 = 0x13
            0x0A, 0x13, 0x02,
            // body 0 (call_some): locals=0, i32.const 1, i32.const 42, call 0, end (8 bytes)
            0x08, 0x00, 0x41, 0x01, 0x41, 0x2A, 0x10, 0x00, 0x0B,
            // body 1 (call_none): locals=0, i32.const 0, i32.const 0, call 0, end (8 bytes)
            0x08, 0x00, 0x41, 0x00, 0x41, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tBytes (func (param i32 i32)))   ;; void PrintBytes(byte[])
        //   (type $tEntry (func))                    ;; void call_print()
        //   (import "my:test/byte-env@1.0.0" "print-bytes" (func $imp (type $tBytes)))
        //   (memory 1)
        //   (data (i32.const 0) "hello")
        //   (func (export "call_print")
        //     i32.const 0; i32.const 5; call $imp))
        //
        // Same wire shape as the string fixture (i32 ptr + i32 len)
        // but the typed C# IBytePrinter.PrintBytes(byte[]) lifts
        // via ListMarshal.LiftPrim<byte> instead of
        // StringMarshal.LiftUtf8.
        private static byte[] BuildByteArrayParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32 i32) → void, () → void
            0x01, 0x09, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section: 1 import
            // size = count(1) + modlen(1) + mod(22) + entlen(1) + ent(11) + desc(2) = 38 = 0x26
            0x02, 0x26, 0x01,
            // module: "my:test/byte-env@1.0.0" (22)
            0x16,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x62, 0x79, 0x74, 0x65, 0x2D, 0x65, 0x6E, 0x76,
            0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "print-bytes" (11)
            0x0B,
            0x70, 0x72, 0x69, 0x6E, 0x74, 0x2D, 0x62, 0x79,
            0x74, 0x65, 0x73,
            // desc: func, type 0
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_print" → func 1
            0x07, 0x0E, 0x01,
            0x0A,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x72, 0x69, 0x6E, 0x74,
            0x00, 0x01,
            // Code: locals=0, i32.const 0, i32.const 5, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x00, 0x41, 0x05, 0x10, 0x00, 0x0B,
            // Data: 1 active segment, mem 0, offset 0, "hello"
            0x0B, 0x0B, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x05,
            0x68, 0x65, 0x6C, 0x6C, 0x6F,
        };

        // (module
        //   (type $tString (func (param i32 i32)))   ;; void Print(string)
        //   (type $tEntry (func))                     ;; void call_print()
        //   (import "my:test/str-env@1.0.0" "print" (func $imp (type $tString)))
        //   (memory 1)
        //   (data (i32.const 0) "hello")
        //   (func (export "call_print")
        //     i32.const 0  ;; ptr
        //     i32.const 5  ;; len
        //     call $imp))
        //
        // The wasm "print" import has 2 wasm slots (ptr + len) but
        // the typed C# IPrinter.Print(string) has a single string
        // param. CanonicalSlotCount maps string→2, so CanEmitDirect
        // accepts the binding; Emit reads ctx.Memories[0].Data
        // and lifts via StringMarshal.LiftUtf8.
        private static byte[] BuildStringParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            //   type 0: (i32 i32) → void
            //   type 1: () → void
            0x01, 0x09, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section: 1 import
            // size = count(1) + modlen(1) + mod(21) + entlen(1) + ent(5) + desc(2) = 31 = 0x1F
            0x02, 0x1F, 0x01,
            // module: "my:test/str-env@1.0.0" (21 bytes)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x73, 0x74, 0x72, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "print" (5 bytes)
            0x05,
            0x70, 0x72, 0x69, 0x6E, 0x74,
            // desc: func, type 0
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory section: 1 memory, no max, min 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export section: "call_print" → func 1
            0x07, 0x0E, 0x01,
            0x0A,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x70, 0x72, 0x69, 0x6E, 0x74,
            0x00, 0x01,
            // Code section: 1 body
            // body = locals(0), i32.const 0, i32.const 5, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x00, 0x41, 0x05, 0x10, 0x00, 0x0B,
            // Data section: 1 active segment, mem 0, offset 0, "hello"
            0x0B, 0x0B, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x05,
            0x68, 0x65, 0x6C, 0x6C, 0x6F,
        };

        // (module
        //   (type $tHandle (func (param i32 i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/http-env@1.0.0" "handle"
        //     (func $imp (type $tHandle)))
        //   (func (export "call_with_opts") (result i32)
        //     i32.const 30    ;; request handle (FakeWidget(50))
        //     i32.const 1     ;; option disc = Some
        //     i32.const 31    ;; opts handle (any non-null)
        //     call $imp)      ;; → (50<<1)|1 = 101
        //   (func (export "call_no_opts") (result i32)
        //     i32.const 30    ;; request handle
        //     i32.const 0     ;; option disc = None
        //     i32.const 0     ;; opts handle (ignored)
        //     call $imp))     ;; → (50<<1)|0 = 100
        //
        // Handles must fit single-byte signed LEB128 (value < 0x40)
        // to avoid sign-extension. 30 and 31 round-trip cleanly.
        //
        // 3-slot wire (request + option-disc + option-handle).
        // Direct-linked emit lifts request via Resources, then
        // recursively lifts option<own<widget>>: brfalse on disc,
        // Some path resolves opts handle via Resources too.
        private static byte[] BuildHttpHandleFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32 i32) → i32 (7 bytes)
            // type 1: () → i32 (4 bytes)
            // size = count(1) + 7 + 4 = 12 = 0x0C
            0x01, 0x0C, 0x02,
            0x60, 0x03, 0x7F, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 22 + 1 + 6 + 2 = 33 = 0x21
            0x02, 0x21, 0x01,
            // module: "my:test/http-env@1.0.0" (22)
            0x16,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x68, 0x74, 0x74, 0x70, 0x2D, 0x65, 0x6E, 0x76,
            0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "handle" (6)
            0x06,
            0x68, 0x61, 0x6E, 0x64, 0x6C, 0x65,
            0x00, 0x00,
            // Function section: 2 funcs of type 1
            0x03, 0x03, 0x02, 0x01, 0x01,
            // Export section: 2 exports
            // size = count(1) + (1+14+1+1) + (1+12+1+1) = 33 = 0x21
            0x07, 0x21, 0x02,
            0x0E,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x77, 0x69, 0x74,
            0x68, 0x5F, 0x6F, 0x70, 0x74, 0x73,
            0x00, 0x01,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6E, 0x6F, 0x5F,
            0x6F, 0x70, 0x74, 0x73,
            0x00, 0x02,
            // Code section: 2 bodies
            // body0 = locals(1) + 3*i32.const(2) + call(2) + end(1) = 10
            // body1 = same = 10
            // size = 1 + 11 + 11 = 23 = 0x17
            0x0A, 0x17, 0x02,
            0x0A, 0x00, 0x41, 0x1E, 0x41, 0x01, 0x41, 0x1F, 0x10, 0x00, 0x0B,
            0x0A, 0x00, 0x41, 0x1E, 0x41, 0x00, 0x41, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tCtor (func (param i32) (result i32)))   ;; [constructor]bag(seedHandle) → bagHandle
        //   (type $tInspect (func (param i32) (result i32))) ;; [method]bag.inspect(bagHandle) → u32
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/res-env@1.0.0" "[constructor]bag"
        //     (func $imp_ctor (type $tCtor)))
        //   (import "my:test/res-env@1.0.0" "[method]bag.inspect"
        //     (func $imp_inspect (type $tInspect)))
        //   (func (export "call_ctor_inspect") (result i32)
        //     i32.const 50          ;; seed widget handle
        //     call $imp_ctor        ;; → bagHandle
        //     call $imp_inspect))   ;; → u32 (the seed's value)
        //
        // Constructor takes the seed handle as wasm i32 (single
        // slot), returns the new bag's handle. The instance method
        // immediately consumes that handle to read the seed's
        // value back through the chain.
        private static byte[] BuildConstructorWithOwnArgFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types (both share (i32) → i32 shape)
            // We can deduplicate but keep separate for clarity:
            // type 0: (i32) → i32 (5 bytes)
            // type 1: () → i32 (4 bytes)
            // size = 1 + 5 + 4 = 10 = 0x0A
            0x01, 0x0A, 0x02,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 2 imports
            // Import 0: "my:test/res-env@1.0.0" "[constructor]bag" : type 0
            //   = 1 + 21 + 1 + 16 + 2 = 41 bytes
            // Import 1: "my:test/res-env@1.0.0" "[method]bag.inspect" : type 0
            //   = 1 + 21 + 1 + 19 + 2 = 44 bytes
            // size = count(1) + 41 + 44 = 86 = 0x56
            0x02, 0x56, 0x02,
            // Import 0:
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x10,
            0x5B, 0x63, 0x6F, 0x6E, 0x73, 0x74, 0x72, 0x75,
            0x63, 0x74, 0x6F, 0x72, 0x5D, 0x62, 0x61, 0x67,
            0x00, 0x00,
            // Import 1:
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x62, 0x61, 0x67, 0x2E, 0x69, 0x6E, 0x73, 0x70,
            0x65, 0x63, 0x74,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_ctor_inspect" (17) → func 2
            0x07, 0x15, 0x01,
            0x11,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x74, 0x6F,
            0x72, 0x5F, 0x69, 0x6E, 0x73, 0x70, 0x65, 0x63,
            0x74,
            0x00, 0x02,
            // Code: locals=0, i32.const 50, call 0, call 1, end
            // body = locals(1) + i32const(2) + call(2) + call(2) + end(1) = 8 bytes
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x32, 0x10, 0x00, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $t (func (param i32 i32 i32)))   ;; [method]logger.write(this, ptr, len)
        //   (type $tEntry (func))
        //   (import "my:test/res-env@1.0.0" "[method]logger.write"
        //     (func $imp (type $t)))
        //   (memory 1)
        //   (data (i32.const 0) "world")
        //   (func (export "call_log")
        //     i32.const 33   ;; logger handle
        //     i32.const 0    ;; ptr
        //     i32.const 5    ;; len
        //     call $imp))
        //
        // Direct-linked emit pops 3 i32s, looks up logger via
        // Resources.GetResource(typeof(ILogger), 33), pushes the
        // resolved ILogger as `this`, lifts the inner string from
        // memory via StringMarshal.LiftUtf8, then callvirts
        // ILogger.Write.
        private static byte[] BuildResourceMethodWithStringArgFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32 i32) → void (6 bytes)
            // type 1: () → void (3 bytes)
            // size = count(1) + type0(6) + type1(3) = 10 = 0x0A
            0x01, 0x0A, 0x02,
            0x60, 0x03, 0x7F, 0x7F, 0x7F, 0x00,
            0x60, 0x00, 0x00,
            // Import section
            // size = 1 + 1 + 21 + 1 + 20 + 2 = 46 = 0x2E
            0x02, 0x2E, 0x01,
            // module: "my:test/res-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "[method]logger.write" (20)
            0x14,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x6C, 0x6F, 0x67, 0x67, 0x65, 0x72, 0x2E, 0x77,
            0x72, 0x69, 0x74, 0x65,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Memory: 1 page
            0x05, 0x03, 0x01, 0x00, 0x01,
            // Export: "call_log" (8) → func 1
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6C, 0x6F, 0x67,
            0x00, 0x01,
            // Code: locals=0, i32.const 33, i32.const 0, i32.const 5, call 0, end
            // body size = locals(1)+i32const(2)*3+call(2)+end(1) = 10
            0x0A, 0x0C, 0x01, 0x0A,
            0x00, 0x41, 0x21, 0x41, 0x00, 0x41, 0x05, 0x10, 0x00, 0x0B,
            // Data: "world" at offset 0
            0x0B, 0x0B, 0x01,
            0x00, 0x41, 0x00, 0x0B, 0x05,
            0x77, 0x6F, 0x72, 0x6C, 0x64,
        };

        // (module
        //   (type $t (func (param i32 i32) (result i32)))
        //   (type $tEntry (func (result i32)))
        //   (import "my:test/res-env@1.0.0" "[method]sink.absorb"
        //     (func $imp (type $t)))
        //   (func (export "call_absorb") (result i32)
        //     i32.const 11   ;; sink handle (`this`)
        //     i32.const 22   ;; widget handle (the own<R> param)
        //     call $imp))
        //
        // [method]sink.absorb takes 2 wasm i32 slots: the leading
        // sink handle (the implicit `this`) and the widget handle
        // (the own<R> arg). Direct-linked emit pops both as i32,
        // looks up sink via Resources.GetResource(typeof(ISink)),
        // pushes that as `this`, looks up widget via
        // Resources.GetResource(typeof(IWidget)), pushes that as
        // the typed IWidget arg, then callvirts ISink.Absorb.
        private static byte[] BuildResourceMethodWithOwnArgFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            // type 0: (i32 i32) → i32 (6 bytes)
            // type 1: () → i32 (4 bytes)
            0x01, 0x0B, 0x02,
            0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x60, 0x00, 0x01, 0x7F,
            // Import section: 1 import
            // size = 1 + 1 + 21 + 1 + 19 + 2 = 45 = 0x2D
            0x02, 0x2D, 0x01,
            // module: "my:test/res-env@1.0.0" (21)
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "[method]sink.absorb" (19)
            0x13,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x73, 0x69, 0x6E, 0x6B, 0x2E, 0x61, 0x62, 0x73,
            0x6F, 0x72, 0x62,
            0x00, 0x00,
            // Function section: 1 func of type 1
            0x03, 0x02, 0x01, 0x01,
            // Export: "call_absorb" (11) → func 1
            0x07, 0x0F, 0x01,
            0x0B,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x62, 0x73,
            0x6F, 0x72, 0x62,
            0x00, 0x01,
            // Code: locals=0, i32.const 11, i32.const 22, call 0, end
            0x0A, 0x0A, 0x01, 0x08,
            0x00, 0x41, 0x0B, 0x41, 0x16, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $tFree (func (result i32)))
        //   (type $tMethod (func (param i32) (result i32)))
        //   (import "my:test/res-env@1.0.0" "banner" (func $imp1 (type $tFree)))
        //   (import "my:test/res-env@1.0.0" "[method]counter.tick"
        //           (func $imp2 (type $tMethod)))
        //   (func (export "call_banner") (result i32) call $imp1)
        //   (func (export "call_tick") (result i32)
        //     i32.const 1                       ;; resource handle
        //     call $imp2))
        //
        // Resource-method import: $imp2 takes the i32 handle as
        // its first wasm param, but the typed C# ICounter.Tick is
        // an instance method with no explicit args (handle resolves
        // to `this` via the resources lookup). Direct-linked emit
        // pops the handle, looks up the instance via
        // ctx.Resources.GetResource(typeof(ICounter), handle), and
        // invokes Tick on the resolved instance.
        private static byte[] BuildResourceMethodFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 2 types
            //   type 0: () → i32
            //   type 1: (i32) → i32
            0x01, 0x0A, 0x02,
            0x60, 0x00, 0x01, 0x7F,
            0x60, 0x01, 0x7F, 0x01, 0x7F,
            // Import section: 2 imports
            0x02, 0x4D, 0x02,
            // Import 0: "my:test/res-env@1.0.0" "banner" : type 0
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x06,
            0x62, 0x61, 0x6E, 0x6E, 0x65, 0x72,
            0x00, 0x00,
            // Import 1: "my:test/res-env@1.0.0" "[method]counter.tick" : type 1
            0x15,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x72, 0x65, 0x73, 0x2D, 0x65, 0x6E, 0x76, 0x40,
            0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x14,
            0x5B, 0x6D, 0x65, 0x74, 0x68, 0x6F, 0x64, 0x5D,
            0x63, 0x6F, 0x75, 0x6E, 0x74, 0x65, 0x72, 0x2E,
            0x74, 0x69, 0x63, 0x6B,
            0x00, 0x01,
            // Function section: 2 funcs, both type 0 (() → i32)
            0x03, 0x03, 0x02, 0x00, 0x00,
            // Export section: 2 exports
            0x07, 0x1B, 0x02,
            // "call_banner" → func 2
            0x0B,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x62, 0x61, 0x6E,
            0x6E, 0x65, 0x72,
            0x00, 0x02,
            // "call_tick" → func 3
            0x09,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x74, 0x69, 0x63, 0x6B,
            0x00, 0x03,
            // Code section: 2 bodies
            // size = count(1) + body0(5) + body1(7) = 13 = 0x0D
            0x0A, 0x0D, 0x02,
            // body 0: locals=0, call 0, end (4-byte body)
            0x04, 0x00, 0x10, 0x00, 0x0B,
            // body 1: locals=0, i32.const 1, call 1, end (6-byte body)
            0x06, 0x00, 0x41, 0x01, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $t1 (func (result i64)))
        //   (import "my:test/env@1.0.0" "get-value" (func $imp1 (type $t1)))
        //   (import "external" "stub" (func $imp2 (type $t1)))
        //   (func (export "call_resolved") (result i64) call $imp1)
        //   (func (export "call_fallback") (result i64) call $imp2))
        //
        // The "my:test/env@1.0.0"."get-value" import is matched by
        // the resolver and lowers to direct-linked IL. The
        // "external"."stub" import is NOT in the resolver's host
        // package, so it falls back to the legacy
        // ImportDelegates[] dispatch. This exercises the
        // per-funcIdx binding map's "sparse subset" handling.
        private static byte[] BuildMixedFallbackFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — () → i64
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7E,
            // Import section: 2 imports
            0x02, 0x2F, 0x02,
            // Import 0: "my:test/env@1.0.0" "get-value"
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x09,
            0x67, 0x65, 0x74, 0x2D, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x00,
            // Import 1: "external" "stub"
            0x08,
            0x65, 0x78, 0x74, 0x65, 0x72, 0x6E, 0x61, 0x6C,
            0x04,
            0x73, 0x74, 0x75, 0x62,
            0x00, 0x00,
            // Function section: 2 local funcs, both type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // Export section: 2 exports
            0x07, 0x21, 0x02,
            // "call_resolved" → func 2
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x72, 0x65, 0x73,
            0x6F, 0x6C, 0x76, 0x65, 0x64,
            0x00, 0x02,
            // "call_fallback" → func 3
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x66, 0x61, 0x6C,
            0x6C, 0x62, 0x61, 0x63, 0x6B,
            0x00, 0x03,
            // Code section: 2 bodies, each = call N; end
            // size = count(1) + body0(5) + body1(5) = 11 = 0x0B
            0x0A, 0x0B, 0x02,
            0x04, 0x00, 0x10, 0x00, 0x0B,
            0x04, 0x00, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $t (func (param i32 i32) (result i32)))
        //   (import "my:test/env@1.0.0" "combine" (func $imp (type $t)))
        //   (func (export "call_combine") (param i32 i32) (result i32)
        //     local.get 0; local.get 1; call $imp))
        private static byte[] BuildMultiParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32 i32) → i32
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            // Import section
            0x02, 0x1D, 0x01,
            // module: "my:test/env@1.0.0" (17)
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "combine" (7)
            0x07,
            0x63, 0x6F, 0x6D, 0x62, 0x69, 0x6E, 0x65,
            // desc: func, typeidx 0
            0x00, 0x00,
            // Function section: 1 local — type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_combine" → func 1
            0x07, 0x10, 0x01,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x62, 0x69, 0x6E, 0x65,
            0x00, 0x01,
            // Code: local.get 0; local.get 1; call 0; end
            0x0A, 0x0A, 0x01, 0x08, 0x00, 0x20, 0x00, 0x20, 0x01, 0x10, 0x00, 0x0B,
        };

        // ============== Test ====================================

        [Fact]
        public void DirectLinkedImport_BypassesDelegateTable()
        {
            const ulong Sentinel = 0xDEADBEEF12345678UL;

            // Reset the global init registries so this test is
            // hermetic — other tests in the project rely on them too.
            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            // Transpile-time runtime needs the import bound for
            // InstantiateModule to succeed. The stub throws if
            // anyone actually invokes it — proving direct-linked
            // bypass requires this delegate to *not* be called.
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<long>>(
                ("my:test/env@1.0.0", "get-value"),
                () => throw new InvalidOperationException(
                    "stub host fn should not be invoked when "
                    + "direct-linked dispatch is in effect"));

            using var ms = new MemoryStream(
                BuildDirectLinkedFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            // Build the resolver from THIS test assembly (so it
            // sees IEnv + TestBundle) and pass it on the options.
            // Explicit bundleType skips the WasiPreview2Bundle
            // auto-discovery path.
            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));

            // Sanity: resolver matched the import.
            Assert.True(resolver.TryResolve("my:test/env@1.0.0",
                "get-value", out var binding));
            Assert.Equal(typeof(IEnv), binding.InterfaceType);
            Assert.Equal(nameof(IEnv.GetValue), binding.Method.Name);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.DirectLinked", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // The resolver-import-bindings map should now reflect
            // the one matched import.
            Assert.NotNull(options.ResolverImportBindings);
            Assert.Single(options.ResolverImportBindings!);

            // Build the IImports proxy. Its handler throws — proves
            // direct linking when the test still passes.
            var importsInterface = result.ImportsInterface!;
            var importsProxy = ImportDispatcher.Create(
                importsInterface,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_get_value"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub should not be invoked "
                            + "for direct-linked import"),
                });

            // Construct the generated module class with
            // (importsProxy, bundle). The ctor signature is built
            // by ModuleClassGenerator.EmitConstructor.
            var bundle = new TestBundle(new FakeEnv(Sentinel));
            var moduleType = result.ModuleClass!;
            var instance = Activator.CreateInstance(moduleType,
                new object[] { importsProxy, bundle })!;

            // Find IExports.call_get, invoke it.
            var exportsInterface = result.ExportsInterface!;
            var callGet = exportsInterface.GetMethod(
                InterfaceGenerator.SanitizeName("call_get"))!;
            object? raw = callGet.Invoke(instance, Array.Empty<object>());

            // The export returns wasm i64 → CLR long. The bundle
            // returns ulong (Sentinel); CIL stack form is identical
            // (64-bit int), so casting to ulong recovers the value.
            Assert.IsType<long>(raw);
            Assert.Equal(Sentinel, unchecked((ulong)(long)raw));
        }

        [Fact]
        public void DirectLinkedImport_MultiParam_PassesNarrowConvs()
        {
            // Wasm i32+i32 → i32 import maps to a CLR
            // (uint, byte) → int interface method. Exercises:
            //   - 2-arg spill / re-push order
            //   - wasm i32 arg → CLR uint     (no conv)
            //   - wasm i32 arg → CLR byte     (conv.u1)
            //   - CLR int return → wasm i32   (no conv)

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/env@1.0.0", "combine"),
                (a, b) => throw new InvalidOperationException(
                    "stub host fn must not be called"));

            using var ms = new MemoryStream(
                BuildMultiParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.DirectLinkedMulti", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_combine"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub must not be invoked"),
                });

            var bundle = new TestBundle(new FakeEnv(0));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Compute a*1000 + b for a known (a, b) pair. Picking
            // values whose b > 127 to exercise the conv.u1 path
            // (signed-vs-unsigned narrow). 200u → byte 200.
            uint a = 12345u;
            byte b = 200;
            int expected = unchecked((int)(a * 1000u + b));

            var callCombine = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_combine"))!;
            object? raw = callCombine.Invoke(instance,
                new object?[] { unchecked((int)a), (int)b });

            Assert.IsType<int>(raw);
            Assert.Equal(expected, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_MixedResolvedAndFallback_BothPathsWork()
        {
            // Two imports in one module. The first is in the
            // resolver's host package and lowers to direct-linked
            // IL; the second is NOT and falls back to the legacy
            // ImportDelegates[] dispatch. The IImports stub for the
            // resolved one throws if called (proves bypass); the
            // stub for the unresolved one returns a known value
            // (proves the legacy path still works alongside the new).
            const ulong DirectLinkedValue = 0xAAAA_BBBB_CCCC_DDDDUL;
            const long FallbackValue = unchecked((long)0x1111_2222_3333_4444UL);

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // Resolved import: stub throws (must not be invoked).
            runtime.BindHostFunction<Func<long>>(
                ("my:test/env@1.0.0", "get-value"),
                () => throw new InvalidOperationException(
                    "direct-linked stub must not be invoked"));
            // Unresolved import: real handler — the legacy
            // delegate dispatch will route through it.
            runtime.BindHostFunction<Func<long>>(
                ("external", "stub"), () => FallbackValue);

            using var ms = new MemoryStream(
                BuildMixedFallbackFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));
            // Sanity: resolver matched ONLY the my:test entry, not
            // the external one.
            Assert.True(resolver.TryResolve(
                "my:test/env@1.0.0", "get-value", out _));
            Assert.False(resolver.TryResolve(
                "external", "stub", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.MixedFallback", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // Per-funcIdx binding map should hold exactly one
            // entry — the resolved import's slot.
            Assert.Single(options.ResolverImportBindings!);

            // IImports proxy: the resolved entry throws if invoked,
            // the fallback entry returns FallbackValue. Both paths
            // get exercised by separate exports.
            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_get_value"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for direct-linked "
                            + "import must not be invoked"),
                    ["external_stub"] = _ => FallbackValue,
                });

            var bundle = new TestBundle(new FakeEnv(DirectLinkedValue));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Direct-linked path: should hit the bundle.
            var callResolved = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_resolved"))!;
            object? rDirect = callResolved.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<long>(rDirect);
            Assert.Equal(DirectLinkedValue,
                unchecked((ulong)(long)rDirect));

            // Fallback path: should hit the IImports stub.
            var callFallback = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_fallback"))!;
            object? rFallback = callFallback.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<long>(rFallback);
            Assert.Equal(FallbackValue, (long)rFallback);
        }

        [Fact]
        public void DirectLinkedImport_ResourceMethod_LookupAndDispatch()
        {
            // Two imports in one module:
            //  - free function "banner" → IResEnv.Banner()
            //  - resource method "[method]counter.tick" with handle 1
            //    → ICounter.Tick() on the instance bound to handle 1.
            // Both resolve directly; the IImports stubs throw if
            // invoked. The resources lookup goes through
            // ctx.Resources.GetResource(typeof(ICounter), 1).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int>>(
                ("my:test/res-env@1.0.0", "banner"),
                () => throw new InvalidOperationException(
                    "stub for banner must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]counter.tick"),
                _ => throw new InvalidOperationException(
                    "stub for counter.tick must not be invoked"));

            using var ms = new MemoryStream(
                BuildResourceMethodFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResBundle),
                resourcesType: typeof(TestResources));

            // Sanity-check both bindings landed.
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "banner", out var banBinding));
            Assert.False(banBinding.IsResourceMethod);
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[method]counter.tick",
                out var tickBinding));
            Assert.True(tickBinding.IsResourceMethod);
            Assert.Equal(typeof(ICounter), tickBinding.InterfaceType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResMethod", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // Both imports should be in the per-funcIdx map.
            Assert.Equal(2, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_res_env_1_0_0_banner"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for direct-linked banner "
                            + "must not be invoked"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]counter.tick")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for direct-linked tick "
                            + "must not be invoked"),
                });

            var fakeCounter = new FakeCounter(start: 41u);
            var resources = new TestResources();
            resources.Register(typeof(ICounter), 1, fakeCounter);
            var bundle = new ResBundle(new FakeResEnv());

            // Ctor signature now: (IImports, object hostBundle,
            // object resources). HasResourceBindings adds the third.
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            // Free function: should hit FakeResEnv.Banner = 0xCAFE.
            var callBanner = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_banner"))!;
            object? rBanner = callBanner.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rBanner);
            Assert.Equal(unchecked((int)0xCAFEu), (int)rBanner);

            // Resource method: handle 1 → fakeCounter; first .Tick()
            // returns 42 (start was 41).
            var callTick = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_tick"))!;
            object? rTick1 = callTick.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rTick1);
            Assert.Equal(42, (int)rTick1);

            // Second call increments again — proves we're hitting
            // the same instance, not a fresh one each time.
            object? rTick2 = callTick.Invoke(instance,
                Array.Empty<object>());
            Assert.Equal(43, (int)rTick2);
        }

        [Fact]
        public void DirectLinkedImport_StringParam_LiftsFromMemory()
        {
            // Wasm has memory + a "hello" data segment at offset 0.
            // The exported call_print invokes the imported print
            // with (ptr=0, len=5). Direct-linked emit reads the
            // bytes from ctx.Memories[0].Data, lifts via
            // StringMarshal.LiftUtf8, and passes the resulting
            // C# string to IPrinter.Print.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int>>(
                ("my:test/str-env@1.0.0", "print"),
                (_, _) => throw new InvalidOperationException(
                    "stub for print must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(StringBundle));

            Assert.True(resolver.TryResolve(
                "my:test/str-env@1.0.0", "print", out var binding));
            Assert.Equal(typeof(IPrinter), binding.InterfaceType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StringParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_str_env_1_0_0_print"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for print must not "
                            + "be invoked"),
                });

            var capturing = new CapturingPrinter();
            var bundle = new StringBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPrint = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_print"))!;
            callPrint.Invoke(instance, Array.Empty<object>());

            // The bytes "hello" lifted from wasm memory, passed
            // through the typed I.Print(string) callvirt, and
            // captured by the test impl.
            Assert.Equal("hello", capturing.Captured);
        }

        [Fact]
        public void DirectLinkedImport_ByteArrayParam_LiftsViaListMarshal()
        {
            // Same wasm wire shape as the string test (i32 ptr +
            // i32 len, "hello" data segment) but the typed C#
            // method is IBytePrinter.PrintBytes(byte[]). Direct-
            // linked emit lifts via ListMarshal.LiftPrim<byte>.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int>>(
                ("my:test/byte-env@1.0.0", "print-bytes"),
                (_, _) => throw new InvalidOperationException(
                    "stub for print-bytes must not be invoked"));

            using var ms = new MemoryStream(
                BuildByteArrayParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ByteBundle));

            Assert.True(resolver.TryResolve(
                "my:test/byte-env@1.0.0", "print-bytes",
                out var binding));
            Assert.Equal(typeof(IBytePrinter), binding.InterfaceType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ByteParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_byte_env_1_0_0_print_bytes"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for print-bytes must "
                            + "not be invoked"),
                });

            var capturing = new CapturingBytePrinter();
            var bundle = new ByteBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPrint = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_print"))!;
            callPrint.Invoke(instance, Array.Empty<object>());

            // The bytes "hello" lifted from wasm memory and passed
            // as a fresh CLR byte[] through the typed
            // I.PrintBytes(byte[]) callvirt.
            Assert.NotNull(capturing.Captured);
            Assert.Equal(new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F },
                capturing.Captured);
        }

        [Fact]
        public void DirectLinkedImport_StaticResourceMethod_NoHandle()
        {
            // [static]widget.default-value: zero-arg static method
            // on the IWidget interface. No leading handle, no `this`.
            // Emit issues `call IWidget::DefaultValue` directly.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int>>(
                ("my:test/res-env@1.0.0", "[static]widget.default-value"),
                () => throw new InvalidOperationException(
                    "stub for static must not be invoked"));

            using var ms = new MemoryStream(
                BuildStaticResourceFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[static]widget.default-value",
                out var binding));
            Assert.Equal(HostPackageResolver.ResourceMethodKind.Static,
                binding.ResourceKind);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StaticRes", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[static]widget.default-value")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for static must not be invoked"),
                });

            // Constructor needs both the bundle and resources args
            // (HasResolverBindings + HasResourceBindings); pass
            // dummies even though static dispatch ignores them.
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy,
                    new WidgetBundle(new FakeWidget(0)),
                    new TestResources() })!;

            var callDef = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_def"))!;
            object? raw = callDef.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(7, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_ConstructorThenInstance_AllocatesAndResolves()
        {
            // [constructor]widget allocates a fresh IWidget instance
            // and returns its handle as the wasm i32 result.
            // [method]widget.read then resolves that handle and
            // calls the typed instance method on it. Together they
            // exercise the AllocateResource + GetResource convention
            // round-trip.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int>>(
                ("my:test/res-env@1.0.0", "[constructor]widget"),
                () => throw new InvalidOperationException(
                    "stub for constructor must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]widget.read"),
                _ => throw new InvalidOperationException(
                    "stub for read must not be invoked"));

            using var ms = new MemoryStream(
                BuildConstructorAndInstanceFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[constructor]widget",
                out var ctorBinding));
            Assert.Equal(HostPackageResolver.ResourceMethodKind.Constructor,
                ctorBinding.ResourceKind);
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[method]widget.read",
                out var readBinding));
            Assert.Equal(HostPackageResolver.ResourceMethodKind.Instance,
                readBinding.ResourceKind);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.CtorRes", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Equal(2, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[constructor]widget")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for ctor must not be invoked"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]widget.read")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for read must not be invoked"),
                });

            var resources = new TestResources();
            var bundle = new WidgetBundle(new FakeWidget(0));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callCreateRead = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_create_read"))!;
            object? raw = callCreateRead.Invoke(instance,
                Array.Empty<object>());

            // FakeWidget(99u).Read() = 99 — proves both the
            // constructor's Allocate path and the subsequent
            // Get-by-handle path round-trip cleanly.
            Assert.IsType<int>(raw);
            Assert.Equal(99, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_IntArrayParam_LiftsViaListMarshal()
        {
            // Generalization of the byte[] path to int[] (list<u32>).
            // Wasm wire is still (i32 ptr, i32 len) but len is the
            // ELEMENT count and the bytes occupy 4*len in memory.
            // Direct-linked emit looks up
            // ListMarshal.LiftPrim<int>(byte[], int, int) via the
            // per-T cache and lifts a fresh int[] from memory.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int>>(
                ("my:test/int-env@1.0.0", "print-ints"),
                (_, _) => throw new InvalidOperationException(
                    "stub for print-ints must not be invoked"));

            using var ms = new MemoryStream(
                BuildIntArrayParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(IntBundle));

            Assert.True(resolver.TryResolve(
                "my:test/int-env@1.0.0", "print-ints", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.IntParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_int_env_1_0_0_print_ints"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for print-ints must "
                            + "not be invoked"),
                });

            var capturing = new CapturingIntPrinter();
            var bundle = new IntBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPrint = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_print_ints"))!;
            callPrint.Invoke(instance, Array.Empty<object>());

            Assert.NotNull(capturing.Captured);
            Assert.Equal(new[] { 10, 20, 30, 40 },
                capturing.Captured);
        }

        [Fact]
        public void DirectLinkedImport_OptionParam_BothBranches()
        {
            // option<u32> wire form: (i32 disc, i32 value).
            // Two exports exercise both branches of the disc:
            //   call_some: passes (disc=1, value=42); host should
            //              receive Option<uint>.Some(42)
            //   call_none: passes (disc=0, value=0); host should
            //              receive Option<uint>.None
            // Direct-linked emit branches on the disc local via
            // brfalse + Option<T>::Some(value) / get_None.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int>>(
                ("my:test/opt-env@1.0.0", "take-opt"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-opt must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(OptBundle));

            Assert.True(resolver.TryResolve(
                "my:test/opt-env@1.0.0", "take-opt", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_opt_env_1_0_0_take_opt"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-opt must "
                            + "not be invoked"),
                });

            var capturing = new CapturingOptTaker();
            var bundle = new OptBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // call_some path: disc=1, value=42 → Option.Some(42).
            var callSome = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_some"))!;
            callSome.Invoke(instance, Array.Empty<object>());
            Assert.True(capturing.Last.HasValue);
            Assert.Equal(42u, capturing.Last.Value);

            // call_none path: disc=0 → Option.None.
            var callNone = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_none"))!;
            callNone.Invoke(instance, Array.Empty<object>());
            Assert.False(capturing.Last.HasValue);
        }

        [Fact]
        public void DirectLinkedImport_OptionStringParam_BothBranches()
        {
            // option<string> wire form: (i32 disc, i32 ptr, i32 len)
            // = 3 slots. Some branch lifts the inner string from
            // memory via LiftUtf8 (recursing into EmitLiftForType),
            // wraps in Option<string>::Some. None branch loads
            // Option<string>::None directly.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int, int>>(
                ("my:test/optstr-env@1.0.0", "take-optstr"),
                (_, _, _) => throw new InvalidOperationException(
                    "stub for take-optstr must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionStringParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(OptStrBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optstr-env@1.0.0", "take-optstr", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptStrParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optstr_env_1_0_0_take_optstr"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-optstr must "
                            + "not be invoked"),
                });

            var capturing = new CapturingOptStrTaker();
            var bundle = new OptStrBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // call_some_str: disc=1, lifts "hello" → Some("hello").
            var callSome = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_some_str"))!;
            callSome.Invoke(instance, Array.Empty<object>());
            Assert.True(capturing.Last.HasValue);
            Assert.Equal("hello", capturing.Last.Value);

            // call_none_str: disc=0 → None (ptr/len ignored).
            var callNone = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_none_str"))!;
            callNone.Invoke(instance, Array.Empty<object>());
            Assert.False(capturing.Last.HasValue);
        }

        [Fact]
        public void DirectLinkedImport_OwnResourceParam_HandleResolves()
        {
            // own<widget> param: wasm wire is one i32 handle, but
            // the typed C# IOwnTaker.TakeWidget takes an IWidget
            // (the resolved instance). Direct-linked emit detects
            // IWidget as a resource interface from the resolver
            // and lifts via Resources.GetResource(typeof(IWidget),
            // handle) → cast.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/own-env@1.0.0", "take-widget"),
                _ => throw new InvalidOperationException(
                    "stub for take-widget must not be invoked"));

            using var ms = new MemoryStream(
                BuildOwnParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(OwnBundle),
                resourcesType: typeof(TestResources));

            // Sanity: the resolver picked IWidget up as a resource
            // interface (it was declared with WitSource Item="widget").
            Assert.True(resolver.IsResourceInterface(typeof(IWidget)));
            Assert.True(resolver.TryResolve(
                "my:test/own-env@1.0.0", "take-widget",
                out var binding));
            Assert.False(binding.IsResourceMethod);   // free fn

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OwnParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_own_env_1_0_0_take_widget"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-widget must "
                            + "not be invoked"),
                });

            // Pre-register a FakeWidget at handle 7 — the wasm
            // body passes that exact handle to the import.
            var resources = new TestResources();
            resources.Register(typeof(IWidget), 7, new FakeWidget(123u));
            var bundle = new OwnBundle(new WidgetReader());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callTake = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_take"))!;
            object? raw = callTake.Invoke(instance,
                Array.Empty<object>());

            // The wasm passes handle 7 → resolver resolves to
            // FakeWidget(123u) → WidgetReader.TakeWidget(w) calls
            // w.Read() = 123. The result rides back through wasm i32.
            Assert.IsType<int>(raw);
            Assert.Equal(123, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_OptionOwnResource_RecursiveLift()
        {
            // option<own<R>> exercises the composition of Option<T>
            // and own<R> in the recursive EmitLiftForType. Some
            // branch must thread resourcesType through the inner-T
            // lift so the resource handle resolves; None branch
            // skips the handle entirely.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/own-env@1.0.0", "take-opt-widget"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-opt-widget must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionOwnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(OwnBundle),
                resourcesType: typeof(TestResources));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptOwn", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_own_env_1_0_0_take_opt_widget"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-opt-widget must "
                            + "not be invoked"),
                });

            var resources = new TestResources();
            resources.Register(typeof(IWidget), 7,
                new FakeWidget(456u));
            var bundle = new OwnBundle(new WidgetReader());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            // Some branch: handle 7 → FakeWidget(456) → 456.
            var callSome = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_take_some"))!;
            object? rSome = callSome.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rSome);
            Assert.Equal(456, (int)rSome);

            // None branch: WidgetReader.TakeOptWidget returns 0
            // when opt.HasValue is false — proves the IL took the
            // None side and never resolved a handle.
            var callNone = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_take_none"))!;
            object? rNone = callNone.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rNone);
            Assert.Equal(0, (int)rNone);
        }

        [Fact]
        public void DirectLinkedImport_ResultParam_BothSides()
        {
            // result<u32, u32> wire form: (i32 disc, i32 payload).
            // disc=0 → Ok branch (calls Result::FromOk), disc=1 →
            // Err branch (calls Result::FromErr). Same recursive
            // EmitLiftForType machinery as Option, just with a
            // 2-case discriminant routing to two construction
            // helpers.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/res-env@1.0.0", "take-result"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-result must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResultBundle));

            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "take-result", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResultParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_res_env_1_0_0_take_result"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-result must "
                            + "not be invoked"),
                });

            var bundle = new ResultBundle(new ResultProbe());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // call_ok: disc=0, payload=0x12 → Ok(0x12).
            // ResultProbe encodes as 0xA000_0000 | 0x12.
            var callOk = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_ok"))!;
            object? rOk = callOk.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rOk);
            Assert.Equal(unchecked((int)(0xA000_0000u | 0x12u)),
                (int)rOk);

            // call_err: disc=1, payload=0x34 → Err(0x34).
            // ResultProbe encodes as 0xE000_0000 | 0x34.
            var callErr = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_err"))!;
            object? rErr = callErr.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rErr);
            Assert.Equal(unchecked((int)(0xE000_0000u | 0x34u)),
                (int)rErr);
        }

        [Fact]
        public void DirectLinkedImport_TupleParam_LiftsAndConstructs()
        {
            // tuple<u32, u32> wire form: 2 i32 slots concatenated.
            // Direct-linked emit lifts each element via recursive
            // EmitLiftForType then calls ValueTuple<uint, uint>'s
            // 2-arg ctor. TupleAdder yields (Item1<<8)|Item2 so the
            // test asserts both elements made it through in order.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/tup-env@1.0.0", "take-tup"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-tup must not be invoked"));

            using var ms = new MemoryStream(
                BuildTupleParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(TupleBundle));

            Assert.True(resolver.TryResolve(
                "my:test/tup-env@1.0.0", "take-tup", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.TupParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_tup_env_1_0_0_take_tup"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-tup must not "
                            + "be invoked"),
                });

            var bundle = new TupleBundle(new TupleAdder());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Wasm passes (5, 7); TupleAdder returns (5<<8)|7 = 0x507.
            var callTup = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_tup"))!;
            object? raw = callTup.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(0x507, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_RecordParam_LiftsViaSetters()
        {
            // record point { x: u32, y: u32 } → CLR Point class
            // with X/Y auto-properties. Wasm wire is 2 i32 slots
            // concatenated. Direct-linked emit creates the Point
            // via parameterless ctor, then sets X (cursor 0) and
            // Y (cursor 1) via property setters before pushing
            // the instance for the typed callvirt.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/rec-env@1.0.0", "take-point"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-point must not be invoked"));

            using var ms = new MemoryStream(
                BuildRecordParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PointBundle));

            Assert.True(resolver.TryResolve(
                "my:test/rec-env@1.0.0", "take-point", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.RecParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_rec_env_1_0_0_take_point"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-point must "
                            + "not be invoked"),
                });

            var bundle = new PointBundle(new PointHasher());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Wasm passes (X=0x12, Y=0x34); PointHasher returns
            // (0x12<<8)|0x34 = 0x1234.
            var callPoint = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_point"))!;
            object? raw = callPoint.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(0x1234, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_EnumAndFlagsParam_PassThroughTypedSlots()
        {
            // enum + flags as separate i32 wire params. Direct-linked
            // emit treats both as their underlying integer for slot
            // count and conversion: Color (byte) gets conv.u1; Perms
            // (uint) shares i32 stack form so no conv. Both reach
            // the typed callvirt as the typed enum values.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/enum-env@1.0.0", "take-ef"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-ef must not be invoked"));

            using var ms = new MemoryStream(
                BuildEnumFlagsParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(EnumBundle));

            Assert.True(resolver.TryResolve(
                "my:test/enum-env@1.0.0", "take-ef", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.EnumParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_enum_env_1_0_0_take_ef"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-ef must not "
                            + "be invoked"),
                });

            var bundle = new EnumBundle(new EnumProbe());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Wasm passes (Color.Green=1, Perms.Read|Exec=5);
            // EnumProbe returns ((byte)1 << 16) | (uint)5 = 0x10005.
            var callEf = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_ef"))!;
            object? raw = callEf.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(0x10005, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_EnumReturn_PassesThroughPrimitivePath()
        {
            // The typed I.Pick() returns Color : byte. The CLR enum
            // value sits on the eval stack in its underlying byte/u8
            // form (which CIL widens to i32), so the existing
            // primitive-return path handles it without special-case
            // emit logic. Test asserts the returned wasm i32 equals
            // (int)Color.Green = 1.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int>>(
                ("my:test/enumret-env@1.0.0", "pick"),
                () => throw new InvalidOperationException(
                    "stub for pick must not be invoked"));

            using var ms = new MemoryStream(
                BuildEnumReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(EnumRetBundle));

            Assert.True(resolver.TryResolve(
                "my:test/enumret-env@1.0.0", "pick", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.EnumRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_enumret_env_1_0_0_pick"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for pick must not be invoked"),
                });

            var bundle = new EnumRetBundle(new GreenPicker());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPick = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_pick"))!;
            object? raw = callPick.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal((int)Color.Green, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_ResourceMethodWithOwnArg_BothLookups()
        {
            // [method]sink.absorb takes the implicit sink handle
            // (`this`) AND a widget handle (own<R> arg). Both
            // resolve through Resources.GetResource. Test plants
            // FakeSink at handle 11 and FakeWidget(789) at handle
            // 22; wasm passes both. Asserts the export's i32 result
            // == 789 — proves both resolutions hit the right
            // instances and the typed callvirt fired correctly.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/res-env@1.0.0", "[method]sink.absorb"),
                (_, _) => throw new InvalidOperationException(
                    "stub for absorb must not be invoked"));

            using var ms = new MemoryStream(
                BuildResourceMethodWithOwnArgFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            // Sanity: both ISink and IWidget recognized as
            // resource interfaces.
            Assert.True(resolver.IsResourceInterface(typeof(ISink)));
            Assert.True(resolver.IsResourceInterface(typeof(IWidget)));
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[method]sink.absorb",
                out var binding));
            Assert.True(binding.IsResourceMethod);
            Assert.Equal(typeof(ISink), binding.InterfaceType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.SinkAbsorb", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]sink.absorb")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for absorb must not "
                            + "be invoked"),
                });

            // Pre-register both instances at their wasm-passed
            // handles. The IL pops `sinkHandle, widgetHandle` and
            // routes them to GetResource(typeof(ISink), 11) +
            // GetResource(typeof(IWidget), 22) respectively.
            var resources = new TestResources();
            resources.Register(typeof(ISink), 11, new FakeSink());
            resources.Register(typeof(IWidget), 22, new FakeWidget(789u));
            // WidgetBundle is unused here (no free fns), but the
            // ctor still requires a non-null bundle arg.
            var bundle = new WidgetBundle(new FakeWidget(0u));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callAbsorb = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_absorb"))!;
            object? raw = callAbsorb.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(789, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_ResourceMethodWithStringArg_LiftsBoth()
        {
            // [method]logger.write(string msg) — wire form is
            // (i32 thisHandle, i32 ptr, i32 len). Direct-linked
            // emit pops all 3, looks up logger via Resources for
            // `this`, lifts the string from memory via LiftUtf8,
            // then callvirts ILogger.Write. Same shape as
            // wasi:io/streams.write — the canonical WASI write
            // pattern works end-to-end.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int, int>>(
                ("my:test/res-env@1.0.0", "[method]logger.write"),
                (_, _, _) => throw new InvalidOperationException(
                    "stub for logger.write must not be invoked"));

            using var ms = new MemoryStream(
                BuildResourceMethodWithStringArgFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.IsResourceInterface(typeof(ILogger)));
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[method]logger.write",
                out var binding));
            Assert.Equal(typeof(ILogger), binding.InterfaceType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.LoggerWrite", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]logger.write")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for logger.write must "
                            + "not be invoked"),
                });

            var capturing = new CapturingLogger();
            var resources = new TestResources();
            resources.Register(typeof(ILogger), 33, capturing);
            var bundle = new WidgetBundle(new FakeWidget(0u));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callLog = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_log"))!;
            callLog.Invoke(instance, Array.Empty<object>());

            // Logger received "world" from wasm memory through
            // the typed callvirt — proves the resource-method +
            // aggregate-param composition works.
            Assert.Equal("world", capturing.Captured);
        }

        [Fact]
        public void DirectLinkedImport_ConstructorWithOwnArg_Composes()
        {
            // [constructor]bag(seed: own<widget>) — same shape as
            // wasi:http/types.outgoing-request(headers: own<fields>).
            // Wasm calls the constructor with a widget handle; the
            // factory mints a Bag wrapping the resolved widget.
            // Direct-linked emit: lift seed handle via Resources,
            // call IBag::Create(IWidget) static factory, allocate
            // a handle for the new bag instance, return as i32.
            // Then [method]bag.inspect resolves the bag handle and
            // calls Inspect() which returns the seed's value.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[constructor]bag"),
                _ => throw new InvalidOperationException(
                    "stub for [constructor]bag must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]bag.inspect"),
                _ => throw new InvalidOperationException(
                    "stub for [method]bag.inspect must not be invoked"));

            using var ms = new MemoryStream(
                BuildConstructorWithOwnArgFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[constructor]bag",
                out var ctorBinding));
            Assert.Equal(HostPackageResolver.ResourceMethodKind.Constructor,
                ctorBinding.ResourceKind);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.BagCtor", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Equal(2, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[constructor]bag")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for ctor must not be invoked"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]bag.inspect")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for inspect must not be invoked"),
                });

            // Pre-register the seed widget at handle 50 — wasm
            // passes that exact handle into the constructor.
            var resources = new TestResources();
            resources.Register(typeof(IWidget), 50, new FakeWidget(2024u));
            var bundle = new WidgetBundle(new FakeWidget(0u));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callCtorInspect = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_ctor_inspect"))!;
            object? raw = callCtorInspect.Invoke(instance,
                Array.Empty<object>());

            // FakeBag(FakeWidget(2024)).Inspect() = 2024 — proves
            // the constructor lifted the widget arg, the factory
            // produced a real instance, the IL allocated a handle,
            // and the inspect method resolved that handle back.
            Assert.IsType<int>(raw);
            Assert.Equal(2024, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_HttpHandleStyle_OwnPlusOptionOwn()
        {
            // Same shape as wasi:http/outgoing-handler.handle —
            // own<R> + option<own<R>> in one call. Tests both
            // resolution paths (request always-Some, opts in Some
            // and None branches) and the canonical 1+2-slot wire
            // layout that real WASI HTTP guests will hit.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int, int>>(
                ("my:test/http-env@1.0.0", "handle"),
                (_, _, _) => throw new InvalidOperationException(
                    "stub for handle must not be invoked"));

            using var ms = new MemoryStream(
                BuildHttpHandleFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(HttpBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/http-env@1.0.0", "handle", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.HttpHandle", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_http_env_1_0_0_handle"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for handle must not be invoked"),
                });

            // Pre-register widgets at the handles the wasm passes:
            //   handle 30 → request widget (FakeWidget(50))
            //   handle 31 → opts widget   (any non-null impl)
            var resources = new TestResources();
            resources.Register(typeof(IWidget), 30, new FakeWidget(50u));
            resources.Register(typeof(IWidget), 31, new FakeWidget(99u));
            var bundle = new HttpBundle(new HttpProbe());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            // Some branch: returns (50<<1)|1 = 101.
            var callWith = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_with_opts"))!;
            object? rWith = callWith.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rWith);
            Assert.Equal(101, (int)rWith);

            // None branch: returns (50<<1)|0 = 100.
            var callNo = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_no_opts"))!;
            object? rNo = callNo.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rNo);
            Assert.Equal(100, (int)rNo);
        }

        [Fact]
        public void DirectLinkedImport_StringListParam_LiftsViaListMarshal()
        {
            // list<string> wire form: (i32 listPtr, i32 count). The
            // memory layout at listPtr is `count` (ptr, len) i32
            // pairs; each pair points at the bytes of one element.
            // Direct-linked emit invokes
            // ListMarshal.LiftStringList(memory, listPtr, count) to
            // walk the structure and lift each element via UTF-8.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int, int>>(
                ("my:test/strs-env@1.0.0", "take-strs"),
                (_, _) => throw new InvalidOperationException(
                    "stub for take-strs must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringListParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(StringsBundle));

            Assert.True(resolver.TryResolve(
                "my:test/strs-env@1.0.0", "take-strs", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StrsParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_strs_env_1_0_0_take_strs"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-strs must "
                            + "not be invoked"),
                });

            var capturing = new CapturingStrings();
            var bundle = new StringsBundle(capturing);
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPrint = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_print_strs"))!;
            callPrint.Invoke(instance, Array.Empty<object>());

            // Memory laid out so listPtr=0 references 2 (ptr, len)
            // pairs pointing at "hi" and "hello"; the lifted array
            // matches in the same order.
            Assert.NotNull(capturing.Captured);
            Assert.Equal(new[] { "hi", "hello" },
                capturing.Captured);
        }

        [Fact]
        public void DirectLinkedImport_VariantParam_AllCasesConstruct()
        {
            // variant signal { tick, beep(u32), name(string) }
            // Wire form: (i32 disc, joined-flat over max payload).
            // Max payload is string (2 slots: ptr + len), so wire =
            // 3 i32 slots. Direct-linked emit switches on disc and
            // newobjs the matching SignalXxx case class — for empty
            // cases via parameterless ctor; for payload cases by
            // recursively lifting the payload type and passing it
            // to the case ctor.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int, int>>(
                ("my:test/var-env@1.0.0", "take-signal"),
                (_, _, _) => throw new InvalidOperationException(
                    "stub for take-signal must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(SignalBundle));

            Assert.True(resolver.TryResolve(
                "my:test/var-env@1.0.0", "take-signal", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.VariantParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_var_env_1_0_0_take_signal"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-signal must "
                            + "not be invoked"),
                });

            var bundle = new SignalBundle(new SignalProbe());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Tick (case 0, no payload): SignalProbe returns 0xA000_0000.
            var callTick = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_tick"))!;
            object? rTick = callTick.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rTick);
            Assert.Equal(unchecked((int)0xA000_0000u), (int)rTick);

            // Beep(7) (case 1, u32 payload): 0xB000_0000 | 7.
            var callBeep = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_beep"))!;
            object? rBeep = callBeep.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rBeep);
            Assert.Equal(unchecked((int)(0xB000_0000u | 7u)),
                (int)rBeep);

            // Name("hi") (case 2, string payload): 0xC000_0000 | 2.
            var callName = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_name"))!;
            object? rName = callName.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(rName);
            Assert.Equal(unchecked((int)(0xC000_0000u | 2u)),
                (int)rName);
        }

        [Fact]
        public void DirectLinkedImport_AggregateReturn_RecordOfPrimitives()
        {
            // wasi:clocks/wall-clock.now()-style aggregate return.
            // Wasm sig: (i32 retArea) → (). Host writes
            // record { u64 seconds, u32 nanoseconds } at retArea
            // (12 bytes). The export's i64.load at retArea+0 reads
            // the seconds field back, providing a probe value the
            // test can assert against.

            const ulong SecondsVal = 0x1122334455667788UL;
            const uint NanosVal    = 0x09999999u;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/agg-env@1.0.0", "now"),
                _ => throw new InvalidOperationException(
                    "stub for now must not be invoked"));

            using var ms = new MemoryStream(
                BuildAggregateReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ClockBundle));

            Assert.True(resolver.TryResolve(
                "my:test/agg-env@1.0.0", "now", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.AggReturn", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // Aggregate-return shape was recognized and
            // direct-linked.
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_agg_env_1_0_0_now"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for now must not be invoked"),
                });

            var dt = new Dt
            {
                Seconds = SecondsVal,
                Nanoseconds = NanosVal,
            };
            var bundle = new ClockBundle(new FixedClock2(dt));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callNowSeconds = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_now_seconds"))!;
            object? raw = callNowSeconds.Invoke(instance,
                Array.Empty<object>());

            // The wasm i64 result IS the u64 seconds field that
            // the host wrote into memory at retArea+0 via
            // PrimitiveStore.StoreU64. Round-trip success proves
            // the aggregate-return retArea write fired correctly.
            Assert.IsType<long>(raw);
            Assert.Equal(SecondsVal, unchecked((ulong)(long)raw));
        }

        [Fact]
        public void DirectLinkedImport_AggregateReturn_ValueTuple()
        {
            // tuple<u32, u32> retArea write. Same machinery as the
            // record-return test but the CLR side is ValueTuple,
            // so the IL reads Item1/Item2 fields (not properties)
            // before the StoreXxx calls. Wasm reads both elements
            // back via i32.load and adds them.

            const uint A = 0x12, B = 0x21;  // single-byte LEB128 friendly

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/tup-ret-env@1.0.0", "make-pair"),
                _ => throw new InvalidOperationException(
                    "stub for make-pair must not be invoked"));

            using var ms = new MemoryStream(
                BuildTupleReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PairBundle));

            Assert.True(resolver.TryResolve(
                "my:test/tup-ret-env@1.0.0", "make-pair", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.TupReturn", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_tup_ret_env_1_0_0_make_pair"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for make-pair must not "
                            + "be invoked"),
                });

            var bundle = new PairBundle(new FixedPair((A, B)));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callPairSum = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_pair_sum"))!;
            object? raw = callPairSum.Invoke(instance,
                Array.Empty<object>());

            // Wasm reads both Item1 + Item2 from the retArea and
            // adds them — A + B == sum.
            Assert.IsType<int>(raw);
            Assert.Equal(unchecked((int)(A + B)), (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_OwnReturn_FactoryMintsHandle()
        {
            // make-widget() → own<widget>: free function returning
            // a resource interface. Wasm wire is 1 i32 (the
            // handle). Direct-linked emit calls the typed factory,
            // allocates a handle for the returned instance via
            // Resources.AllocateResource, and returns the handle.
            // The wasm body chains a [method]widget.read on that
            // handle to prove the handle is wired correctly into
            // the resources table.

            const uint Seed = 0x35;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int>>(
                ("my:test/own-ret-env@1.0.0", "make-widget"),
                () => throw new InvalidOperationException(
                    "stub for make-widget must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]widget.read"),
                _ => throw new InvalidOperationException(
                    "stub for read must not be invoked"));

            using var ms = new MemoryStream(
                BuildOwnReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(FactoryBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/own-ret-env@1.0.0", "make-widget", out _));
            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0", "[method]widget.read", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OwnReturn", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Equal(2, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/own-ret-env@1.0.0_make-widget")] = _ =>
                        throw new InvalidOperationException("make IImports stub"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]widget.read")] = _ =>
                        throw new InvalidOperationException("read IImports stub"),
                });

            var resources = new TestResources();
            var bundle = new FactoryBundle(new WidgetMaker(Seed));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callMakeRead = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_make_read"))!;
            object? raw = callMakeRead.Invoke(instance,
                Array.Empty<object>());

            // WidgetMaker.MakeWidget() returns FakeWidget(0x35).
            // The IL allocates a handle for it; the next call's
            // resource lookup resolves the handle back; FakeWidget
            // .Read() returns 0x35.
            Assert.IsType<int>(raw);
            Assert.Equal((int)Seed, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_OptionPrimitiveReturn_BothBranches()
        {
            // Option<u32> retArea: u8 disc @0 + u32 value @4. Two
            // exports probe each layout slot independently:
            //   call_maybe_disc:  reads byte 0 → expected disc
            //   call_maybe_value: reads i32 @ offset 4 → expected value
            // Both run twice — once with Some(0x12), once with None.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optret-env@1.0.0", "maybe"),
                _ => throw new InvalidOperationException(
                    "stub for maybe must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optret-env@1.0.0", "maybe", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptRetParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optret_env_1_0_0_maybe"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for maybe must not be invoked"),
                });

            // Test #1: Option.Some(0x12) — disc=1, value=0x12.
            {
                var bundle = new MaybeBundle(new FixedMaybe(
                    Option<uint>.Some(0x12u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_disc"))!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_value"))!;

                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
                Assert.Equal(0x12, (int)callValue.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Test #2: Option.None — disc=0, value=0.
            {
                var bundle = new MaybeBundle(new FixedMaybe(
                    Option<uint>.None));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultPrimitiveReturn_BothBranches()
        {
            // Result<u32, u32> retArea: u8 disc @0 + u32 value @4
            // (joined-flat at max alignment of Ok/Err = 4). Same
            // wire shape as Option<u32> but with two value-bearing
            // branches: disc=0 → Ok(value), disc=1 → Err(value).
            // Two exports probe each retArea slot; run twice with
            // Ok and Err results.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/resret-env@1.0.0", "compute"),
                _ => throw new InvalidOperationException(
                    "stub for compute must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResultRetBundle));

            Assert.True(resolver.TryResolve(
                "my:test/resret-env@1.0.0", "compute", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResRetParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_resret_env_1_0_0_compute"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for compute must not be invoked"),
                });

            // Test #1: Ok(0x21) — disc=0, value=0x21.
            {
                var bundle = new ResultRetBundle(new FixedResult(
                    Result<uint, uint>.FromOk(0x21u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_value"))!;

                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
                Assert.Equal(0x21, (int)callValue.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Test #2: Err(0x33) — disc=1, value=0x33.
            {
                var bundle = new ResultRetBundle(new FixedResult(
                    Result<uint, uint>.FromErr(0x33u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_value"))!;

                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
                Assert.Equal(0x33, (int)callValue.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_OptionOwnReturn_HandleAllocation()
        {
            // Option<own<R>> retArea: u8 disc + i32 handle. The
            // Some path mints a handle for the inner instance via
            // Resources.AllocateResource; the wasm body reads the
            // handle back and calls [method]widget.read on it to
            // probe round-trip success. The None path writes
            // disc=0 and the handle slot stays as default memory.

            const uint Seed = 0x33;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optown-env@1.0.0", "maybe-widget"),
                _ => throw new InvalidOperationException(
                    "stub for maybe-widget must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]widget.read"),
                _ => throw new InvalidOperationException(
                    "stub for read must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionOwnReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeWidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/optown-env@1.0.0", "maybe-widget", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptOwnRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Equal(2, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/optown-env@1.0.0_maybe-widget")] = _ =>
                        throw new InvalidOperationException("maybe IImports stub"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]widget.read")] = _ =>
                        throw new InvalidOperationException("read IImports stub"),
                });

            // Test #1: Some(FakeWidget(0x33)) — disc=1, handle
            // refers to FakeWidget(0x33), Read() returns 0x33.
            {
                var resources = new TestResources();
                var bundle = new MaybeWidgetBundle(
                    new FixedMaybeWidget(
                        Option<IWidget>.Some(new FakeWidget(Seed))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                // Re-instantiate so the resources table starts
                // fresh (each call to maybe_disc invoked the host
                // factory once already, allocating handle 1).
                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;
                var callRead = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_read_handle"))!;
                Assert.Equal((int)Seed,
                    (int)callRead.Invoke(fresh,
                        Array.Empty<object>())!);
            }

            // Test #2: None — disc=0; we don't probe the handle
            // (would resolve to garbage at unset slot 0).
            {
                var resources = new TestResources();
                var bundle = new MaybeWidgetBundle(
                    new FixedMaybeWidget(Option<IWidget>.None));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_maybe_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultOwnReturn_OkAllocatesHandle()
        {
            // Result<own<R>, primitive> retArea: u8 disc + i32
            // value @4. Ok side allocates a handle from the
            // resource instance; Err side stores the primitive.
            // Same shape as wasi:sockets/tcp-create-socket
            // -> result<own<tcp-socket>, error-code>.

            const uint Seed = 0x29;
            const uint ErrCode = 0x1F;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/socket-env@1.0.0", "create"),
                _ => throw new InvalidOperationException(
                    "stub for create must not be invoked"));
            runtime.BindHostFunction<Func<int, int>>(
                ("my:test/res-env@1.0.0", "[method]widget.read"),
                _ => throw new InvalidOperationException(
                    "stub for read must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultOwnReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(SocketBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/socket-env@1.0.0", "create", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResOwnRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/socket-env@1.0.0_create")] = _ =>
                        throw new InvalidOperationException("create IImports"),
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[method]widget.read")] = _ =>
                        throw new InvalidOperationException("read IImports"),
                });

            // Test #1: Ok(FakeWidget(0x29)) — disc=0, value=handle.
            // The follow-up read on the handle returns 0x29.
            {
                var resources = new TestResources();
                var bundle = new SocketBundle(new FixedSocket(
                    Result<IWidget, uint>.FromOk(new FakeWidget(Seed))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_create_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;
                var callRead = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_create_ok_read"))!;
                Assert.Equal((int)Seed,
                    (int)callRead.Invoke(fresh,
                        Array.Empty<object>())!);
            }

            // Test #2: Err(0x1F) — disc=1, value=0x1F (primitive
            // stored directly into the value slot, no handle alloc).
            {
                var resources = new TestResources();
                var bundle = new SocketBundle(new FixedSocket(
                    Result<IWidget, uint>.FromErr(ErrCode)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_create_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var freshForValue = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle, resources })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_create_value"))!;
                Assert.Equal((int)ErrCode,
                    (int)callValue.Invoke(freshForValue,
                        Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_StaticMultiArg_PassesAllArgs()
        {
            // [static]widget.combine-static takes 2 i32 args, no
            // `this`. Direct-linked emit: pop 2 i32s into temps,
            // re-push in order, `call` the static method (no
            // virtual dispatch, no resource lookup).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/res-env@1.0.0", "[static]widget.combine-static"),
                (_, _) => throw new InvalidOperationException(
                    "stub for combine-static must not be invoked"));

            using var ms = new MemoryStream(
                BuildStaticMultiArgFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/res-env@1.0.0",
                "[static]widget.combine-static", out var binding));
            Assert.Equal(HostPackageResolver.ResourceMethodKind.Static,
                binding.ResourceKind);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StaticMulti", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "my:test/res-env@1.0.0_[static]widget.combine-static")] = _ =>
                        throw new InvalidOperationException("stub"),
                });

            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy,
                    new WidgetBundle(new FakeWidget(0)),
                    new TestResources() })!;

            // CombineStatic(5, 7) = 5 + 7*100 = 705
            var callCombine = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_combine"))!;
            object? raw = callCombine.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(705, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_MixedTupleParam_RecurseElementLifts()
        {
            // tuple<u32, string> — Item1 is one i32 slot, Item2 is
            // (ptr, len) two i32 slots; total wasm wire = 3 slots.
            // The tuple lift loop walks elements per their flat-slot
            // count; Item2's lift recurses into the existing string-
            // lift emit.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int, int>>(
                ("my:test/mtup-env@1.0.0", "take-mtup"),
                (_, _, _) => throw new InvalidOperationException(
                    "stub for take-mtup must not be invoked"));

            using var ms = new MemoryStream(
                BuildMixedTupleParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MixedTupleBundle));

            Assert.True(resolver.TryResolve(
                "my:test/mtup-env@1.0.0", "take-mtup", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.MtupParam", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_mtup_env_1_0_0_take_mtup"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for take-mtup must not be invoked"),
                });

            var bundle = new MixedTupleBundle(new MixedTupleProbe());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Wasm passes (Item1=5, ptr=0, len=2) over "hi";
            // MixedTupleProbe yields 5*1000 + 2 = 5002.
            var callMtup = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_mtup"))!;
            object? raw = callMtup.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<int>(raw);
            Assert.Equal(5002, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_VariantReturn_AllEmptyCases()
        {
            // Variant return — wire form for all-empty-cases is
            // just u8 disc at retArea+0. IL chains isinst checks
            // against each sealed case to determine the disc.
            // Loop over Tag.TagFoo, TagBar, TagBaz: each should
            // produce disc=0/1/2 respectively.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/varret-env@1.0.0", "pick"),
                _ => throw new InvalidOperationException(
                    "stub for pick must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(TagBundle));

            Assert.True(resolver.TryResolve(
                "my:test/varret-env@1.0.0", "pick", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.VarRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_varret_env_1_0_0_pick"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for pick must not be invoked"),
                });

            var pairs = new (Tag value, int expectedDisc)[]
            {
                (new Tag.TagFoo(), 0),
                (new Tag.TagBar(), 1),
                (new Tag.TagBaz(), 2),
            };

            foreach (var (value, expectedDisc) in pairs)
            {
                var bundle = new TagBundle(new FixedTag(value));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pick_disc"))!;
                Assert.Equal(expectedDisc,
                    (int)callDisc.Invoke(instance,
                        Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_VariantReturn_PayloadCases()
        {
            // variant code { ok, count(u32), max(u32) }: u8 disc +
            // u32 payload (joined-flat). Empty case (Ok) leaves
            // payload slot at default; payload cases write the
            // u32 value at retArea+4.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/codeFactory@1.0.0", "emit"),
                _ => throw new InvalidOperationException(
                    "stub for emit must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantPayloadReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(CodeBundle));

            Assert.True(resolver.TryResolve(
                "my:test/codeFactory@1.0.0", "emit", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.VarPayloadRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_codeFactory_1_0_0_emit"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for emit must not be invoked"),
                });

            // Test #1: Ok (case 0, no payload) → disc=0.
            {
                var bundle = new CodeBundle(new FixedCode(
                    new Code.CodeOk()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_code_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Test #2: Count(0x15) (case 1, u32 payload) → disc=1, value=0x15.
            {
                var bundle = new CodeBundle(new FixedCode(
                    new Code.CodeCount(0x15u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_code_disc"))!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_code_value"))!;

                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
                Assert.Equal(0x15, (int)callValue.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Test #3: Max(0x27) (case 2, u32 payload) → disc=2, value=0x27.
            {
                var bundle = new CodeBundle(new FixedCode(
                    new Code.CodeMax(0x27u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_code_disc"))!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_code_value"))!;

                Assert.Equal(2, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
                Assert.Equal(0x27, (int)callValue.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_StringReturn_ViaCabiRealloc()
        {
            // Wasm exports `cabi_realloc` (a real bump allocator);
            // direct-linked emit detects the string return, calls
            // PrimitiveStore.StoreString which encodes UTF-8,
            // calls cabi_realloc to allocate the buffer, copies
            // bytes, and writes (ptr, len) at retArea.
            // call_greet_len reads len; call_greet_first_byte
            // reads byte at *(ptr) — both validate the lifted
            // string sat correctly in guest memory.

            const string Greeting = "hi";

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/str-ret-env@1.0.0", "greet"),
                _ => throw new InvalidOperationException(
                    "stub for greet must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(GreetBundle));

            Assert.True(resolver.TryResolve(
                "my:test/str-ret-env@1.0.0", "greet", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_str_ret_env_1_0_0_greet"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for greet must not be invoked"),
                });

            var bundle = new GreetBundle(new FixedGreeter(Greeting));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // call_greet_len reads len at retArea+4 → 2.
            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_len"))!;
            Assert.Equal(2, (int)callLen.Invoke(instance,
                Array.Empty<object>())!);

            // call_greet_first_byte reads ptr@retArea, then byte
            // at that ptr → 'h' = 0x68. Run on a fresh module
            // instance so the bump allocator starts back at 32.
            var freshInstance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_first_byte"))!;
            Assert.Equal(0x68, (int)callByte.Invoke(freshInstance,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_ByteArrayReturn_ViaCabiRealloc()
        {
            // Same retArea machinery as string return, but no
            // encode step — raw bytes flow through. Two exports
            // probe len and first byte of the returned buffer.

            byte[] expected = { 0x10, 0x20, 0x30 };

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/bytes-ret-env@1.0.0", "give"),
                _ => throw new InvalidOperationException(
                    "stub for give must not be invoked"));

            using var ms = new MemoryStream(
                BuildByteArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ByteSourceBundle));

            Assert.True(resolver.TryResolve(
                "my:test/bytes-ret-env@1.0.0", "give", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.BytesRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_bytes_ret_env_1_0_0_give"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for give must not be invoked"),
                });

            var bundle = new ByteSourceBundle(
                new FixedByteSource(expected));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // call_give_len reads count at retArea+4 → 3.
            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_len"))!;
            Assert.Equal(3, (int)callLen.Invoke(instance,
                Array.Empty<object>())!);

            // call_give_first_byte reads ptr@retArea, then byte
            // at that ptr → 0x10. Fresh module instance so the
            // bump allocator starts back at 32.
            var freshInstance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_first_byte"))!;
            Assert.Equal(0x10, (int)callByte.Invoke(freshInstance,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_OptionStringReturn_ViaCabiRealloc()
        {
            // Option<string> retArea: u8 disc @0 + (i32 ptr @4,
            // i32 len @8). Some path: cabi_realloc + memcpy + write
            // disc=1 + (ptr, len). None path: write disc=0.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optstr-ret-env@1.0.0", "name"),
                _ => throw new InvalidOperationException(
                    "stub for name must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionStringReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeNameBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optstr-ret-env@1.0.0", "name", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptStrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optstr_ret_env_1_0_0_name"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for name must not be invoked"),
                });

            // Test #1: Some("Wat") — disc=1; first byte at ptr = 'W' (0x57).
            {
                var bundle = new MaybeNameBundle(
                    new FixedMaybeName(Option<string>.Some("Wat")));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_name_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callByte = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_name_first_byte"))!;
                Assert.Equal(0x57, (int)callByte.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Test #2: None — disc=0; the ptr/len slots stay
            // default and we don't probe them.
            {
                var bundle = new MaybeNameBundle(
                    new FixedMaybeName(Option<string>.None));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_name_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultStringReturn_ViaCabiRealloc()
        {
            // Result<string, u32> retArea: u8 disc @0 + joined-flat
            // (i32 ptr | u32) at offset 4. Ok branch: cabi_realloc
            // mints a buffer, StoreString writes (ptr, len) and
            // disc=0; Err branch: StoreI32 writes the err value and
            // disc=1. Probes both branches end-to-end.
            //
            // Mirrors realistic WASI shapes like
            // wasi:filesystem/types.read-link → result<string, error-code>.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/resstr-env@1.0.0", "compute"),
                _ => throw new InvalidOperationException(
                    "stub for compute must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultStringReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResStrBundle));

            Assert.True(resolver.TryResolve(
                "my:test/resstr-env@1.0.0", "compute", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResStrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_resstr_env_1_0_0_compute"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for compute must not be invoked"),
                });

            // Test #1: Ok("Hi") — disc=0; first byte at *(ptr) = 'H' (0x48).
            {
                var bundle = new ResStrBundle(new FixedResStr(
                    Result<string, uint>.FromOk("Hi")));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callByte = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_first_byte"))!;
                Assert.Equal(0x48, (int)callByte.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Test #2: Err(0x33) — disc=1; i32 @retArea+4 = 0x33.
            {
                var bundle = new ResStrBundle(new FixedResStr(
                    Result<string, uint>.FromErr(0x33u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInt4 = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_int4"))!;
                Assert.Equal(0x33, (int)callInt4.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultByteArrayReturn_ViaCabiRealloc()
        {
            // Result<u32, list<u8>> — symmetric to ResultString but
            // the byte[] arm rides the Err side. Ok branch writes
            // u32 + disc=0; Err branch cabi_realloc-allocates the
            // byte[] and writes (ptr, count) + disc=1.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/resbyt-env@1.0.0", "compute"),
                _ => throw new InvalidOperationException(
                    "stub for compute must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultByteArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResBytBundle));

            Assert.True(resolver.TryResolve(
                "my:test/resbyt-env@1.0.0", "compute", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResBytRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_resbyt_env_1_0_0_compute"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for compute must not be invoked"),
                });

            // Test #1: Ok(0x21) — disc=0; i32 @retArea+4 = 0x21.
            {
                var bundle = new ResBytBundle(new FixedResByt(
                    Result<uint, byte[]>.FromOk(0x21u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInt4 = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_int4"))!;
                Assert.Equal(0x21, (int)callInt4.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Test #2: Err([0x37, 0x12]) — disc=1; first byte at
            // *(ptr) = 0x37.
            {
                var bundle = new ResBytBundle(new FixedResByt(
                    Result<uint, byte[]>.FromErr(new byte[] { 0x37, 0x12 })));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callByte = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_compute_first_byte"))!;
                Assert.Equal(0x37, (int)callByte.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_IntArrayReturn_ViaCabiRealloc()
        {
            // list<s32> retArea: i32 ptr @0 + i32 count @4. Host
            // returns int[]; emit calls cabi_realloc with align=4
            // and byte_count = count*4, copies the LE bytes via
            // MemoryMarshal.AsBytes, writes (ptr, count). Probes:
            //   call_give_len   → count
            //   call_give_first_int → first i32 at *(ptr+0)

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/ints-ret-env@1.0.0", "give"),
                _ => throw new InvalidOperationException(
                    "stub for give must not be invoked"));

            using var ms = new MemoryStream(
                BuildIntArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(IntSourceBundle));

            Assert.True(resolver.TryResolve(
                "my:test/ints-ret-env@1.0.0", "give", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.IntArrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_ints_ret_env_1_0_0_give"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for give must not be invoked"),
                });

            // int[] { 0x21, 0x33, 0x12 } — len=3, first int=0x21.
            var bundle = new IntSourceBundle(new FixedIntSource(
                new int[] { 0x21, 0x33, 0x12 }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_len"))!;
            Assert.Equal(3, (int)callLen.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callFirst = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_first_int"))!;
            Assert.Equal(0x21, (int)callFirst.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_LongArrayReturn_ViaCabiRealloc()
        {
            // list<s64> retArea: i32 ptr @0 + i32 count @4. Host
            // returns long[]; emit calls cabi_realloc with align=8
            // and byte_count = count*8, copies LE bytes. Probes
            // first 32 bits of first long (== full long for small
            // values on LE).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/longs-ret-env@1.0.0", "give"),
                _ => throw new InvalidOperationException(
                    "stub for give must not be invoked"));

            using var ms = new MemoryStream(
                BuildLongArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(LongSourceBundle));

            Assert.True(resolver.TryResolve(
                "my:test/longs-ret-env@1.0.0", "give", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.LongArrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_longs_ret_env_1_0_0_give"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for give must not be invoked"),
                });

            // long[] { 0x42, 0x37 } — len=2, first long low32=0x42.
            var bundle = new LongSourceBundle(new FixedLongSource(
                new long[] { 0x42, 0x37 }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_len"))!;
            Assert.Equal(2, (int)callLen.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callFirst = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_first_long_lo"))!;
            Assert.Equal(0x42, (int)callFirst.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_StringArrayReturn_ViaCabiRealloc()
        {
            // list<string> retArea: i32 ptr @0 + i32 count @4. The
            // emit calls StoreStringList which mints the outer
            // (sptr,slen) array via cabi_realloc, then per-string
            // mints UTF-8 buffers and writes (sptr, slen) into the
            // outer slots. Probes:
            //   call_give_count           → outer count
            //   call_give_first_byte      → first byte of first
            //                                string (chases outer
            //                                + inner ptrs)

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/strs-ret-env@1.0.0", "give"),
                _ => throw new InvalidOperationException(
                    "stub for give must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(StringSourceBundle));

            Assert.True(resolver.TryResolve(
                "my:test/strs-ret-env@1.0.0", "give", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StrArrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_strs_ret_env_1_0_0_give"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for give must not be invoked"),
                });

            // string[] { "Hi", "Wat" } — count=2, first="H"=0x48.
            var bundle = new StringSourceBundle(new FixedStringSource(
                new string[] { "Hi", "Wat" }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_count"))!;
            Assert.Equal(2, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_first_byte"))!;
            Assert.Equal(0x48, (int)callByte.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_VariantMixedPayloadReturn_AllCases()
        {
            // event { tap, beep(u8), tone(u32) } — joined-flat
            // payload slot reserves max(align)=4, max(size)=4. Each
            // case writes its own width starting at retArea+4.
            // The probe loads i32 at retArea+4: for u32 payloads
            // it returns the full value; for u8 payloads only the
            // low byte is meaningful (higher bytes are zero on
            // a fresh wasm memory).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/evtret-env@1.0.0", "fire"),
                _ => throw new InvalidOperationException(
                    "stub for fire must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantMixedPayloadFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(EvtBundle));

            Assert.True(resolver.TryResolve(
                "my:test/evtret-env@1.0.0", "fire", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.EvtRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_evtret_env_1_0_0_fire"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for fire must not be invoked"),
                });

            // Tap (no payload) → disc=0.
            {
                var bundle = new EvtBundle(
                    new FixedEvt(new Evt.EvtTap()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_evt_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Beep(0x37) → disc=1, low byte of value=0x37.
            {
                var bundle = new EvtBundle(
                    new FixedEvt(new Evt.EvtBeep(0x37)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_evt_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_evt_value"))!;
                Assert.Equal(0x37, (int)callValue.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Tone(0x12345678) → disc=2, value=0x12345678.
            {
                var bundle = new EvtBundle(
                    new FixedEvt(new Evt.EvtTone(0x12345678u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_evt_disc"))!;
                Assert.Equal(2, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_evt_value"))!;
                Assert.Equal(0x12345678, (int)callValue.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_VariantStringPayloadReturn_BothCases()
        {
            // variant note { silent, label(string) }: empty case +
            // string payload case. The label arm rides through
            // cabi_realloc + StoreString (same machinery as
            // Result<string, X>'s Ok arm).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/noteret-env@1.0.0", "sound"),
                _ => throw new InvalidOperationException(
                    "stub for sound must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantStringPayloadFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(NoteBundle));

            Assert.True(resolver.TryResolve(
                "my:test/noteret-env@1.0.0", "sound", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.NoteRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_noteret_env_1_0_0_sound"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for sound must not be invoked"),
                });

            // Silent (no payload) → disc=0.
            {
                var bundle = new NoteBundle(
                    new FixedNote(new Note.NoteSilent()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_note_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Label("Hi") → disc=1, first byte = 'H' (0x48).
            {
                var bundle = new NoteBundle(
                    new FixedNote(new Note.NoteLabel("Hi")));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_note_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callByte = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_note_first_byte"))!;
                Assert.Equal(0x48, (int)callByte.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_OptionTupleReturn_BothBranches()
        {
            // Option<(uint, uint)> retArea: u8 disc @0 + u32 a @4
            // + u32 b @8. Some path writes per-element; None path
            // writes disc=0 only.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/opttup-env@1.0.0", "pair"),
                _ => throw new InvalidOperationException(
                    "stub for pair must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionTupleReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybePairBundle));

            Assert.True(resolver.TryResolve(
                "my:test/opttup-env@1.0.0", "pair", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptTupRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_opttup_env_1_0_0_pair"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for pair must not be invoked"),
                });

            // Some((0x21, 0x33)) → disc=1, a=0x21, b=0x33.
            {
                var bundle = new MaybePairBundle(new FixedMaybePair(
                    Option<(uint, uint)>.Some((0x21u, 0x33u))));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pair_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callA = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pair_a"))!;
                Assert.Equal(0x21, (int)callA.Invoke(fresh,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callB = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pair_b"))!;
                Assert.Equal(0x33, (int)callB.Invoke(fresh2,
                    Array.Empty<object>())!);
            }

            // None → disc=0; tuple slots stay default.
            {
                var bundle = new MaybePairBundle(new FixedMaybePair(
                    Option<(uint, uint)>.None));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pair_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_OptionRecordReturn_BothBranches()
        {
            // Option<XYPair> retArea: same wire form as the tuple
            // case (record-of-primitives is structurally identical
            // for canon-ABI). Per-property write at retArea+4/+8.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optrec-env@1.0.0", "xy"),
                _ => throw new InvalidOperationException(
                    "stub for xy must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionRecordReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeXYBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optrec-env@1.0.0", "xy", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptRecRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optrec_env_1_0_0_xy"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for xy must not be invoked"),
                });

            // Some(XYPair { X=7, Y=21 }) → disc=1, a=7, b=21.
            {
                var bundle = new MaybeXYBundle(new FixedMaybeXY(
                    Option<XYPair>.Some(new XYPair { X = 7, Y = 21 })));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_xy_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callA = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_xy_a"))!;
                Assert.Equal(7, (int)callA.Invoke(fresh,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callB = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_xy_b"))!;
                Assert.Equal(21, (int)callB.Invoke(fresh2,
                    Array.Empty<object>())!);
            }

            // None → disc=0; record slots stay default.
            {
                var bundle = new MaybeXYBundle(new FixedMaybeXY(
                    Option<XYPair>.None));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_xy_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultTupleReturn_BothBranches()
        {
            // Result<(uint, uint), uint> retArea: u8 disc + 8-byte
            // tuple in Ok arm OR 4-byte u32 in Err arm. Joined-flat
            // valueOffset = 4, max-payload-size = 8 (the tuple).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/restup-env@1.0.0", "go"),
                _ => throw new InvalidOperationException(
                    "stub for go must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultTupleReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PairResultBundle));

            Assert.True(resolver.TryResolve(
                "my:test/restup-env@1.0.0", "go", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResTupRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_restup_env_1_0_0_go"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for go must not be invoked"),
                });

            // Ok((9, 16)) → disc=0, a=9, b=16.
            {
                var bundle = new PairResultBundle(new FixedPairResult(
                    Result<(uint, uint), uint>.FromOk((9u, 16u))));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callA = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_a"))!;
                Assert.Equal(9, (int)callA.Invoke(fresh,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callB = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_b"))!;
                Assert.Equal(16, (int)callB.Invoke(fresh2,
                    Array.Empty<object>())!);
            }

            // Err(0x37) → disc=1, a=0x37 (Err value at retArea+4).
            {
                var bundle = new PairResultBundle(new FixedPairResult(
                    Result<(uint, uint), uint>.FromErr(0x37u)));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callA = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_a"))!;
                Assert.Equal(0x37, (int)callA.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_VariantTuplePayloadReturn_BothCases()
        {
            // pos { home, point(tuple<u32,u32>) }: empty + tuple
            // payload. canon-ABI joined-flat reserves max(align)=4
            // and max(size)=8 for the slot. The point arm goes
            // through EmitInlineRecordOrTupleStore — same machinery
            // as Option<tuple>/Result<tuple,X>.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/varpos-env@1.0.0", "emit"),
                _ => throw new InvalidOperationException(
                    "stub for emit must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantTuplePayloadFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PosBundle));

            Assert.True(resolver.TryResolve(
                "my:test/varpos-env@1.0.0", "emit", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.PosRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_varpos_env_1_0_0_emit"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for emit must not be invoked"),
                });

            // Home (no payload) → disc=0.
            {
                var bundle = new PosBundle(
                    new FixedPos(new Pos.PosHome()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pos_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Point((9, 16)) → disc=1, a=9, b=16.
            {
                var bundle = new PosBundle(
                    new FixedPos(new Pos.PosPoint((9u, 16u))));

                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pos_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callA = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pos_a"))!;
                Assert.Equal(9, (int)callA.Invoke(fresh,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callB = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_pos_b"))!;
                Assert.Equal(16, (int)callB.Invoke(fresh2,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ListOfTupleReturn_ViaCabiRealloc()
        {
            // list<tuple<u32, u32>> wire form: outer (ptr, count)
            // at retArea + a packed array of (a, b) tuples at *ptr.
            // The emit allocates the outer buffer once via
            // cabi_realloc, loops to write each element's fields,
            // then writes (ptr, count) to retArea. Probes: count,
            // first element's a/b via outer-ptr chase.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/listtup-env@1.0.0", "all"),
                _ => throw new InvalidOperationException(
                    "stub for all must not be invoked"));

            using var ms = new MemoryStream(
                BuildListOfTupleReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PairListBundle));

            Assert.True(resolver.TryResolve(
                "my:test/listtup-env@1.0.0", "all", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ListTupRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_listtup_env_1_0_0_all"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for all must not be invoked"),
                });

            var bundle = new PairListBundle(new FixedPairList(
                new (uint, uint)[] { (9u, 16u), (3u, 4u) }));

            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_count"))!;
            Assert.Equal(2, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callA = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_first_a"))!;
            Assert.Equal(9, (int)callA.Invoke(fresh,
                Array.Empty<object>())!);

            var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callB = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_first_b"))!;
            Assert.Equal(16, (int)callB.Invoke(fresh2,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_OptionIntListReturn_BothBranches()
        {
            // Option<int[]>: u8 disc + (i32 ptr, i32 count) at +4.
            // Some path goes through cabi_realloc + StorePrimitiveArray<int>.
            // None path writes disc=0 only.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optintl-env@1.0.0", "list"),
                _ => throw new InvalidOperationException(
                    "stub for list must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionIntListReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeIntsBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optintl-env@1.0.0", "list", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptIntListRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optintl_env_1_0_0_list"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for list must not be invoked"),
                });

            // Some(int[] { 9, 16 }) → disc=1, first int=9.
            {
                var bundle = new MaybeIntsBundle(new FixedMaybeInts(
                    Option<int[]>.Some(new int[] { 9, 16 })));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_list_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callFirst = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_list_first_int"))!;
                Assert.Equal(9, (int)callFirst.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // None → disc=0; ptr/count slots stay default.
            {
                var bundle = new MaybeIntsBundle(new FixedMaybeInts(
                    Option<int[]>.None));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_list_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ListOfByteListReturn_ViaCabiRealloc()
        {
            // list<list<u8>> wire form: outer (ptr, count) + inner
            // (sub_ptr, sub_len) pairs at *(ptr) + raw byte buffers
            // at each *(sub_ptr). Two-level cabi_realloc machinery
            // — same as list<string> minus the UTF-8 encode step.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/listbyt-env@1.0.0", "all"),
                _ => throw new InvalidOperationException(
                    "stub for all must not be invoked"));

            using var ms = new MemoryStream(
                BuildListOfByteListReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ByteListBundle));

            Assert.True(resolver.TryResolve(
                "my:test/listbyt-env@1.0.0", "all", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ListBytRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_listbyt_env_1_0_0_all"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for all must not be invoked"),
                });

            // byte[][] { {0x37, 0x12}, {0x9, 0x16, 0x21} } — count=2,
            // first sub's first byte = 0x37.
            var bundle = new ByteListBundle(new FixedByteList(
                new byte[][]
                {
                    new byte[] { 0x37, 0x12 },
                    new byte[] { 0x09, 0x16, 0x21 },
                }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_count"))!;
            Assert.Equal(2, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_first_byte"))!;
            Assert.Equal(0x37, (int)callByte.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_ListOfIntListReturn_ViaCabiRealloc()
        {
            // list<list<s32>> = int[][]: same two-level cabi_realloc
            // shape as byte[][] but per-sub buffer is i32-aligned
            // (align=4) and elementSize=4. Generic StorePrimArrayList<T>
            // closes over int (or any T : unmanaged).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/listil-env@1.0.0", "all"),
                _ => throw new InvalidOperationException(
                    "stub for all must not be invoked"));

            using var ms = new MemoryStream(
                BuildListOfIntListReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(IntListListBundle));

            Assert.True(resolver.TryResolve(
                "my:test/listil-env@1.0.0", "all", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ListIntListRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_listil_env_1_0_0_all"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for all must not be invoked"),
                });

            // int[][] { {9, 16}, {21, 33, 7} } — count=2, first
            // sub's first int = 9.
            var bundle = new IntListListBundle(new FixedIntListList(
                new int[][]
                {
                    new int[] { 9, 16 },
                    new int[] { 21, 33, 7 },
                }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_count"))!;
            Assert.Equal(2, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callInt = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_first_int"))!;
            Assert.Equal(9, (int)callInt.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_ListOfOwnReturn_AllocatesHandles()
        {
            // list<own<widget>>: per-element AllocateResource mints
            // a handle for each instance; outer (ptr, count) +
            // packed handle buffer at *(ptr). Probes count + first
            // handle (which TestResources allocates starting at 1).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/listown-env@1.0.0", "all"),
                _ => throw new InvalidOperationException(
                    "stub for all must not be invoked"));

            using var ms = new MemoryStream(
                BuildListOfOwnReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(WidgetListBundle),
                resourcesType: typeof(TestResources));

            Assert.True(resolver.TryResolve(
                "my:test/listown-env@1.0.0", "all", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ListOwnRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_listown_env_1_0_0_all"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for all must not be invoked"),
                });

            // Two FakeWidgets — count=2, first handle = 1 (first
            // allocation in a fresh TestResources).
            var resources = new TestResources();
            var bundle = new WidgetListBundle(new FixedWidgetList(
                new IWidget[]
                {
                    new FakeWidget(0x9),
                    new FakeWidget(0x16),
                }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_count"))!;
            Assert.Equal(2, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var freshResources = new TestResources();
            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, freshResources })!;
            var callHandle = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all_first_handle"))!;
            Assert.Equal(1, (int)callHandle.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_OptionOptionReturn_AllBranches()
        {
            // Option<Option<u32>> wire form:
            //   @0: outer disc (u8)
            //   @4: inner disc (u8)
            //   @8: inner value (u32)
            // Three branches:
            //   None              → outer_disc=0
            //   Some(None)        → outer_disc=1, inner_disc=0
            //   Some(Some(0x42))  → outer_disc=1, inner_disc=1, value=0x42

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/optopt-env@1.0.0", "num"),
                _ => throw new InvalidOperationException(
                    "stub for num must not be invoked"));

            using var ms = new MemoryStream(
                BuildOptionOptionReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(MaybeMaybeNumBundle));

            Assert.True(resolver.TryResolve(
                "my:test/optopt-env@1.0.0", "num", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OptOptRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_optopt_env_1_0_0_num"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for num must not be invoked"),
                });

            // None → outer_disc=0.
            {
                var bundle = new MaybeMaybeNumBundle(
                    new FixedMaybeMaybeNum(Option<Option<uint>>.None));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_outer_disc"))!;
                Assert.Equal(0, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Some(None) → outer_disc=1, inner_disc=0.
            {
                var bundle = new MaybeMaybeNumBundle(
                    new FixedMaybeMaybeNum(
                        Option<Option<uint>>.Some(Option<uint>.None)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_outer_disc"))!;
                Assert.Equal(1, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_inner_disc"))!;
                Assert.Equal(0, (int)callInner.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Some(Some(0x42)) → outer_disc=1, inner_disc=1, value=0x42.
            {
                var bundle = new MaybeMaybeNumBundle(
                    new FixedMaybeMaybeNum(
                        Option<Option<uint>>.Some(
                            Option<uint>.Some(0x42u))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_outer_disc"))!;
                Assert.Equal(1, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh1 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_inner_disc"))!;
                Assert.Equal(1, (int)callInner.Invoke(fresh1,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_num_value"))!;
                Assert.Equal(0x42, (int)callValue.Invoke(fresh2,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_ResultOptionReturn_AllBranches()
        {
            // Result<Option<u32>, u32>: nested Option in the Ok arm.
            // Wire form:
            //   @0: outer disc (u8) — 0=Ok, 1=Err
            //   @4: Ok arm: inner Option<u32> disc (u8)
            //         Err arm: u32 (low byte at @4)
            //   @8: Ok+Some arm: inner u32 value
            // Three test branches: Ok(Some(0x42)), Ok(None), Err(0x37).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/resopt-env@1.0.0", "go"),
                _ => throw new InvalidOperationException(
                    "stub for go must not be invoked"));

            using var ms = new MemoryStream(
                BuildResultOptionReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(ResultOptionNumBundle));

            Assert.True(resolver.TryResolve(
                "my:test/resopt-env@1.0.0", "go", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.ResOptRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_resopt_env_1_0_0_go"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for go must not be invoked"),
                });

            // Ok(Some(0x42)) → outer=0, inner=1, value=0x42.
            {
                var bundle = new ResultOptionNumBundle(
                    new FixedResultOptionNum(
                        Result<Option<uint>, uint>.FromOk(
                            Option<uint>.Some(0x42u))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_outer_disc"))!;
                Assert.Equal(0, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_inner_disc"))!;
                Assert.Equal(1, (int)callInner.Invoke(fresh,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_value"))!;
                Assert.Equal(0x42, (int)callValue.Invoke(fresh2,
                    Array.Empty<object>())!);
            }

            // Ok(None) → outer=0, inner=0.
            {
                var bundle = new ResultOptionNumBundle(
                    new FixedResultOptionNum(
                        Result<Option<uint>, uint>.FromOk(
                            Option<uint>.None)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_outer_disc"))!;
                Assert.Equal(0, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_inner_disc"))!;
                Assert.Equal(0, (int)callInner.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Err(0x37) → outer=1, low byte of err u32 = 0x37.
            {
                var bundle = new ResultOptionNumBundle(
                    new FixedResultOptionNum(
                        Result<Option<uint>, uint>.FromErr(0x37u)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callOuter = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_outer_disc"))!;
                Assert.Equal(1, (int)callOuter.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_go_inner_disc"))!;
                Assert.Equal(0x37, (int)callInner.Invoke(fresh,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_StringReturn_Utf16Encoding()
        {
            // Reuses the greet/string-return wasm fixture, but
            // mutates the binding's StringEncoding to Utf16 before
            // transpile so the IL emit dispatches to
            // PrimitiveStore.StoreStringUtf16. Returns "Â" (U+00C2):
            //   UTF-8 encoding: bytes [0xC3, 0x82], len=2 bytes
            //   UTF-16 encoding: bytes [0xC2, 0x00], len=1 code unit
            // The differentiator is len AND first byte — both differ.

            const string Greeting = "Â";

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/str-ret-env@1.0.0", "greet"),
                _ => throw new InvalidOperationException(
                    "stub for greet must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(GreetBundle));

            // Mutate the binding's encoding BEFORE transpile so the
            // emitted IL dispatches to StoreStringUtf16.
            Assert.True(resolver.TryResolve(
                "my:test/str-ret-env@1.0.0", "greet", out var binding));
            binding.StringEncoding = CanonOption.Kind.StringUtf16;

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StrRetUtf16", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_str_ret_env_1_0_0_greet"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for greet must not be invoked"),
                });

            var bundle = new GreetBundle(new FixedGreeter(Greeting));

            // call_greet_len → 1 (UTF-16 code units, NOT 2 bytes)
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_len"))!;
            Assert.Equal(1, (int)callLen.Invoke(instance,
                Array.Empty<object>())!);

            // call_greet_first_byte → 0xC2 (low byte of U+00C2 LE),
            // NOT 0xC3 (UTF-8 first byte of 2-byte encoding).
            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_first_byte"))!;
            Assert.Equal(0xC2, (int)callByte.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_StringReturn_Latin1OrUtf16Encoding()
        {
            // Same fixture, but encoding = Latin1OrUtf16. WACS
            // always picks the UTF-16 branch (high-bit set tag).
            // Returns "Â": UTF-16 bytes [0xC2, 0x00], code units=1,
            // tagged_count = 1 | (1 << 31) = 0x80000001 = -2147483647
            // when read as i32. The probe reads i32 at retArea+4.

            const string Greeting = "Â";

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/str-ret-env@1.0.0", "greet"),
                _ => throw new InvalidOperationException(
                    "stub for greet must not be invoked"));

            using var ms = new MemoryStream(
                BuildStringReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(GreetBundle));

            Assert.True(resolver.TryResolve(
                "my:test/str-ret-env@1.0.0", "greet", out var binding));
            binding.StringEncoding = CanonOption.Kind.StringLatin1OrUtf16;

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.StrRetLatin1OrUtf16", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_str_ret_env_1_0_0_greet"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for greet must not be invoked"),
                });

            var bundle = new GreetBundle(new FixedGreeter(Greeting));

            // call_greet_len → high-bit-tagged: low 31 bits = 1
            // (UTF-16 code unit count), high bit set. Reads as i32:
            // 0x80000001 = -2147483647.
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callLen = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_len"))!;
            int taggedLen = (int)callLen.Invoke(instance,
                Array.Empty<object>())!;
            Assert.Equal(unchecked((int)0x80000001u), taggedLen);

            // call_greet_first_byte → 0xC2 (low byte of U+00C2 LE)
            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_greet_first_byte"))!;
            Assert.Equal(0xC2, (int)callByte.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_VariantOptionPayloadReturn_AllBranches()
        {
            // variant ping { idle, retry(option<u32>) }: nested
            // Option payload exercises the variant emit's
            // dispatch-to-EmitOptionStoreAt path for case payloads.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/varopt-env@1.0.0", "emit"),
                _ => throw new InvalidOperationException(
                    "stub for emit must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantOptionPayloadFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(PingBundle));

            Assert.True(resolver.TryResolve(
                "my:test/varopt-env@1.0.0", "emit", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.PingRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_varopt_env_1_0_0_emit"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for emit must not be invoked"),
                });

            // Idle (no payload) → outer_disc=0.
            {
                var bundle = new PingBundle(
                    new FixedPing(new Ping.PingIdle()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Retry(None) → outer_disc=1, inner_disc=0.
            {
                var bundle = new PingBundle(
                    new FixedPing(new Ping.PingRetry(
                        Option<uint>.None)));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_inner_disc"))!;
                Assert.Equal(0, (int)callInner.Invoke(fresh,
                    Array.Empty<object>())!);
            }

            // Retry(Some(0x42)) → outer_disc=1, inner_disc=1, value=0x42.
            {
                var bundle = new PingBundle(
                    new FixedPing(new Ping.PingRetry(
                        Option<uint>.Some(0x42u))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh1 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_inner_disc"))!;
                Assert.Equal(1, (int)callInner.Invoke(fresh1,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_ping_value"))!;
                Assert.Equal(0x42, (int)callValue.Invoke(fresh2,
                    Array.Empty<object>())!);
            }
        }

        [Fact]
        public void DirectLinkedImport_BoolArrayReturn_ViaCabiRealloc()
        {
            // bool[] return: same retArea wire as byte[] (i32 ptr +
            // i32 count + raw 0/1 bytes). MemoryMarshal.AsBytes<bool>
            // encodes false=0, true=1 — matches canon-ABI bool wire.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/boolarr-env@1.0.0", "give"),
                _ => throw new InvalidOperationException(
                    "stub for give must not be invoked"));

            using var ms = new MemoryStream(
                BuildBoolArrayReturnFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(BoolSourceBundle));

            Assert.True(resolver.TryResolve(
                "my:test/boolarr-env@1.0.0", "give", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.BoolArrRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_boolarr_env_1_0_0_give"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for give must not be invoked"),
                });

            // bool[] { true, false, true } → count=3, first byte=1.
            var bundle = new BoolSourceBundle(new FixedBoolSource(
                new bool[] { true, false, true }));
            var instance = Activator.CreateInstance(
                result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            var callCount = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_count"))!;
            Assert.Equal(3, (int)callCount.Invoke(instance,
                Array.Empty<object>())!);

            var fresh = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;
            var callByte = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_give_first_byte"))!;
            Assert.Equal(1, (int)callByte.Invoke(fresh,
                Array.Empty<object>())!);
        }

        [Fact]
        public void DirectLinkedImport_VariantResultPayloadReturn_AllBranches()
        {
            // variant outcome { okay, fail(result<u32, u32>) }: nested
            // Result payload exercises the variant emit's
            // dispatch-to-EmitResultStoreAt path for case payloads.

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Action<int>>(
                ("my:test/varres-env@1.0.0", "emit"),
                _ => throw new InvalidOperationException(
                    "stub for emit must not be invoked"));

            using var ms = new MemoryStream(
                BuildVariantResultPayloadFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm },
                bundleType: typeof(OutcomeBundle));

            Assert.True(resolver.TryResolve(
                "my:test/varres-env@1.0.0", "emit", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.OutcomeRet", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");
            Assert.Single(options.ResolverImportBindings!);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_varres_env_1_0_0_emit"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for emit must not be invoked"),
                });

            // Okay (no payload) → outer_disc=0.
            {
                var bundle = new OutcomeBundle(
                    new FixedOutcome(new Outcome.OutcomeOkay()));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_disc"))!;
                Assert.Equal(0, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);
            }

            // Fail(Ok(0x42)) → outer=1, inner_disc=0, value=0x42.
            {
                var bundle = new OutcomeBundle(
                    new FixedOutcome(new Outcome.OutcomeFail(
                        Result<uint, uint>.FromOk(0x42u))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh1 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_inner_disc"))!;
                Assert.Equal(0, (int)callInner.Invoke(fresh1,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_value"))!;
                Assert.Equal(0x42, (int)callValue.Invoke(fresh2,
                    Array.Empty<object>())!);
            }

            // Fail(Err(0x37)) → outer=1, inner_disc=1, value=0x37.
            {
                var bundle = new OutcomeBundle(
                    new FixedOutcome(new Outcome.OutcomeFail(
                        Result<uint, uint>.FromErr(0x37u))));
                var instance = Activator.CreateInstance(
                    result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;

                var callDisc = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_disc"))!;
                Assert.Equal(1, (int)callDisc.Invoke(instance,
                    Array.Empty<object>())!);

                var fresh1 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callInner = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_inner_disc"))!;
                Assert.Equal(1, (int)callInner.Invoke(fresh1,
                    Array.Empty<object>())!);

                var fresh2 = Activator.CreateInstance(result.ModuleClass!,
                    new object[] { importsProxy, bundle })!;
                var callValue = result.ExportsInterface!.GetMethod(
                    InterfaceGenerator.SanitizeName("call_outcome_value"))!;
                Assert.Equal(0x37, (int)callValue.Invoke(fresh2,
                    Array.Empty<object>())!);
            }
        }
    }
}
