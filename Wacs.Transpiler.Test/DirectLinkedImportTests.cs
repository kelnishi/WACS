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
    }
}
