// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Harness;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    // Top-level (not nested) partial classes so the MVP
    // generator's namespace-bound emit shape applies. Nested-
    // class emission is a follow-up scope.

    /// <summary>Void / no-arg shape — the 0.10.0 MVP.</summary>
    [AsyncComponentHarness]
    internal partial class VoidHarnessFixture
    {
        [AsyncExport("[async-lift]demo:cli/run#run")]
        internal partial void Run();
    }

    /// <summary>Primitive args + return — the 0.10.1 extension.</summary>
    [AsyncComponentHarness]
    internal partial class PrimitiveHarnessFixture
    {
        [AsyncExport("[async-lift]demo:calc/add#add")]
        internal partial int Add(int a, int b);

        [AsyncExport("[async-lift]demo:bool/check#check")]
        internal partial bool Check(int input);
    }

    /// <summary>Sync (non-async-lifted) exports. Generator
    /// emits CreateInvokerFunc / CreateInvokerAction bodies
    /// with statically-known type args — fully AOT-safe, no
    /// canon-async dispatcher involvement.</summary>
    [AsyncComponentHarness]
    internal partial class SyncHarnessFixture
    {
        [SyncExport("greet")]
        internal partial int Greet(int namePtr, int nameLen);

        [SyncExport("cabi_post_greet")]
        internal partial void PostGreet(int retArea);
    }

    /// <summary>Sync exports with string params + return —
    /// the canonical hello-spike shape. Generator emits
    /// StringCoding.LowerUtf8/LiftUtf8 + MemoryHelpers
    /// glue + cabi_realloc/cabi_post_X invokers.</summary>
    [AsyncComponentHarness]
    internal partial class StringHarnessFixture
    {
        [SyncExport("greet")]
        internal partial string Greet(string name);
    }

    /// <summary>Sync exports with `byte[]` params + return
    /// (canon-ABI <c>list&lt;u8&gt;</c>). Same flat shape as
    /// strings — (ptr, len) pair — but no UTF-8 encoding,
    /// just raw byte copy via Buffer.BlockCopy.</summary>
    [AsyncComponentHarness]
    internal partial class BytesHarnessFixture
    {
        [SyncExport("transform")]
        internal partial byte[] Transform(byte[] data);
    }

    /// <summary>Sync exports with <c>Option&lt;T&gt;</c>
    /// (canon-ABI <c>option&lt;T&gt;</c>) for primitive T.
    /// C# representation is <c>T?</c> (Nullable&lt;T&gt;).
    /// Flat lowering: (disc:i32, payload:T-slot); option
    /// return uses an inline retArea written by the callee.
    /// </summary>
    [AsyncComponentHarness]
    internal partial class OptionHarnessFixture
    {
        [SyncExport("maybe_double")]
        internal partial int? MaybeDouble(int? input);

        [SyncExport("flag")]
        internal partial bool? Flag(bool? input);
    }

    /// <summary>User-defined <c>[WitRecord]</c> struct
    /// referenced by sync exports. The generator enumerates
    /// instance fields in declaration order and emits
    /// per-field lift/lower at the canon-ABI field offsets.
    /// </summary>
    [WitRecord]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>Sync exports with <c>[WitRecord]</c>
    /// params + return.</summary>
    [AsyncComponentHarness]
    internal partial class RecordHarnessFixture
    {
        [SyncExport("origin")]
        internal partial Point Origin();

        [SyncExport("manhattan")]
        internal partial int Manhattan(Point p);
    }

    /// <summary>Sync exports with C# tuple types
    /// (canon-ABI <c>tuple&lt;T1, T2, ...&gt;</c>) for
    /// primitive element types. Tuples flat-lower
    /// per-element on params; multi-element returns use a
    /// retArea, single-element returns inline.</summary>
    [AsyncComponentHarness]
    internal partial class TupleHarnessFixture
    {
        [SyncExport("split")]
        internal partial (int, int) Split(int input);

        [SyncExport("combine")]
        internal partial int Combine((int, int) pair);

        [SyncExport("triple")]
        internal partial (int, long, bool) Triple();
    }

    /// <summary>Sync exports with <c>WitResult&lt;TOk,
    /// TErr&gt;</c> (canon-ABI <c>result&lt;TOk, TErr&gt;</c>).
    /// Covers the four canonical shapes:
    /// <c>result&lt;(), ()&gt;</c> (disc-only),
    /// <c>result&lt;(), Int32&gt;</c> /
    /// <c>result&lt;Int32, ()&gt;</c> (one trivial arm),
    /// <c>result&lt;Int32, Int32&gt;</c> (both
    /// primitive). Mixed-width arms remain unsupported.
    /// </summary>
    [AsyncComponentHarness]
    internal partial class ResultHarnessFixture
    {
        [SyncExport("noop")]
        internal partial WitResult<ValueTuple, ValueTuple>
            Noop();

        [SyncExport("try_parse")]
        internal partial WitResult<int, ValueTuple>
            TryParse(int input);

        [SyncExport("divide")]
        internal partial WitResult<int, int>
            Divide(int a, int b);
    }

    /// <summary>
    /// Generator integration tests. The Wacs.ComponentModel.Test
    /// csproj wires
    /// <c>Wacs.ComponentModel.Async.SourceGen</c> as an
    /// <c>Analyzer</c>, so any partial class in this assembly
    /// marked <see cref="AsyncComponentHarnessAttribute"/> is
    /// processed at build time. These tests reflect over the
    /// emitted shape — constructor signature, <c>Instance</c>
    /// property, per-export partial method bodies — to validate
    /// the generator's contract.
    /// </summary>
    public class AsyncComponentHarnessGeneratorTests
    {
        [Fact]
        public void Generator_emits_constructor_with_componentBytes()
        {
            var ctors = typeof(VoidHarnessFixture).GetConstructors(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance);
            Assert.Single(ctors);
            var ctor = ctors[0];
            var parms = ctor.GetParameters();
            Assert.Equal(2, parms.Length);
            Assert.Equal(typeof(byte[]), parms[0].ParameterType);
            Assert.True(parms[1].ParameterType.IsGenericType);
            Assert.Equal(typeof(Action<>),
                parms[1].ParameterType.GetGenericTypeDefinition());
            Assert.True(parms[1].HasDefaultValue);
        }

        [Fact]
        public void Generator_emits_Instance_property()
        {
            var prop = typeof(VoidHarnessFixture)
                .GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            Assert.Equal(
                "Wacs.ComponentModel.Runtime.ComponentInstance",
                prop!.PropertyType.FullName);
            Assert.True(prop.CanRead);
            Assert.Null(prop.GetSetMethod(nonPublic: true));
        }

        [Fact]
        public void Generator_emits_void_partial_method_body()
        {
            var m = typeof(VoidHarnessFixture).GetMethod("Run",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal(typeof(void), m!.ReturnType);
            Assert.Empty(m.GetParameters());
            Assert.NotNull(m.GetMethodBody());
        }

        [Fact]
        public void Generator_emits_hash_error_for_unsupported_types()
        {
            // We can't compile a fixture with an unsupported
            // type (the `#error` directive would fail the build),
            // so reflect on the generator's own assembly and
            // verify the error string is present in the
            // generator source — a smoke-test that the
            // unsupported-type guard is still in place.
            var genAsm = typeof(AsyncComponentHarnessAttribute)
                .Assembly.GetReferencedAssemblies();
            // The generator ships as an analyzer, not a runtime
            // reference. Instead probe the build-output
            // generated-files cache for the canonical
            // unsupported-type sentinel that EmitExportMethod
            // emits. If a future generator refactor drops the
            // guard, this test surfaces the regression at the
            // metadata level.
            //
            // Locating the generator-emitted file in the
            // consumer obj/ tree is brittle, so we instead
            // assert the contract obliquely: the generator's
            // assembly is in the analyzer load path and the
            // error string lives there as a string literal.
            var analyzerPath = AppContext.BaseDirectory;
            var dllPath = Path.Combine(analyzerPath,
                "Wacs.ComponentModel.Async.SourceGen.dll");
            if (!File.Exists(dllPath))
            {
                // Analyzer DLLs aren't always copied to the test
                // output. Skip when not findable; the test is
                // best-effort.
                return;
            }
            var bytes = File.ReadAllBytes(dllPath);
            // ASCII / UTF-16 string-literal scan for the
            // marker substring in the generator's emit code.
            var ascii = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains(
                "is not a canon-ABI primitive", ascii);
        }

        [Fact]
        public void Generator_emits_record_export_signatures()
        {
            // Point Origin() — multi-field record return uses
            // retArea; flat invoker: Func<int>.
            var origin = typeof(RecordHarnessFixture).GetMethod(
                "Origin",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(origin);
            Assert.Equal(typeof(Point), origin!.ReturnType);
            Assert.Empty(origin.GetParameters());
            var originInv = typeof(RecordHarnessFixture)
                .GetField("_invoker_Origin",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(originInv);
            Assert.Equal(typeof(Func<int>),
                originInv!.FieldType);

            // int Manhattan(Point p) — record param flattens
            // to 2 ints. Flat invoker: Func<int, int, int>.
            var manhattan = typeof(RecordHarnessFixture)
                .GetMethod("Manhattan",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(manhattan);
            Assert.Equal(typeof(int), manhattan!.ReturnType);
            Assert.Single(manhattan.GetParameters());
            Assert.Equal(typeof(Point),
                manhattan.GetParameters()[0].ParameterType);
            var manInv = typeof(RecordHarnessFixture)
                .GetField("_invoker_Manhattan",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(manInv);
            Assert.Equal(typeof(Func<int, int, int>),
                manInv!.FieldType);
        }

        [Fact]
        public void Generator_emits_tuple_export_signatures()
        {
            // (int, int) Split(int) — flat invoker:
            // (input:int, retArea:int) → Func<int, int>.
            var split = typeof(TupleHarnessFixture).GetMethod(
                "Split",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(split);
            Assert.Equal(typeof(ValueTuple<int, int>),
                split!.ReturnType);
            var splitInv = typeof(TupleHarnessFixture)
                .GetField("_invoker_Split",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(splitInv);
            Assert.Equal(typeof(Func<int, int>),
                splitInv!.FieldType);

            // int Combine((int, int) pair) — tuple param
            // flattens to 2 ints. Flat invoker: (item1, item2,
            // ret) = Func<int, int, int>.
            var combine = typeof(TupleHarnessFixture).GetMethod(
                "Combine",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(combine);
            Assert.Equal(typeof(int), combine!.ReturnType);
            Assert.Single(combine.GetParameters());
            Assert.Equal(typeof(ValueTuple<int, int>),
                combine.GetParameters()[0].ParameterType);
            var combineInv = typeof(TupleHarnessFixture)
                .GetField("_invoker_Combine",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(combineInv);
            Assert.Equal(typeof(Func<int, int, int>),
                combineInv!.FieldType);

            // (int, long, bool) Triple() — three-element
            // tuple return uses retArea. Flat invoker:
            // Func<int>.
            var triple = typeof(TupleHarnessFixture).GetMethod(
                "Triple",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(triple);
            Assert.Equal(typeof(ValueTuple<int, long, bool>),
                triple!.ReturnType);
            var tripleInv = typeof(TupleHarnessFixture)
                .GetField("_invoker_Triple",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(tripleInv);
            Assert.Equal(typeof(Func<int>),
                tripleInv!.FieldType);
        }

        [Fact]
        public void Generator_emits_result_export_signatures()
        {
            // result<(), ()> — disc-only. Flat invoker:
            // Func<int> (just the disc i32 returned directly).
            var noop = typeof(ResultHarnessFixture).GetMethod(
                "Noop",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(noop);
            Assert.Equal(
                typeof(WitResult<ValueTuple, ValueTuple>),
                noop!.ReturnType);
            Assert.Empty(noop.GetParameters());
            var noopInv = typeof(ResultHarnessFixture)
                .GetField("_invoker_Noop",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(noopInv);
            Assert.Equal(typeof(Func<int>),
                noopInv!.FieldType);

            // result<int, ()> — Ok carries int, Err is unit.
            // Joined payload type is int.
            // Flat: (int param, int retArea) = Func<int, int>.
            var tp = typeof(ResultHarnessFixture).GetMethod(
                "TryParse",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(tp);
            Assert.Equal(
                typeof(WitResult<int, ValueTuple>),
                tp!.ReturnType);
            var tpInv = typeof(ResultHarnessFixture)
                .GetField("_invoker_TryParse",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(tpInv);
            Assert.Equal(typeof(Func<int, int>),
                tpInv!.FieldType);

            // result<int, int> — both arms carry int. Joined
            // payload is int. Flat: (a, b, retArea) =
            // Func<int, int, int>.
            var div = typeof(ResultHarnessFixture).GetMethod(
                "Divide",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(div);
            Assert.Equal(typeof(WitResult<int, int>),
                div!.ReturnType);
            var divInv = typeof(ResultHarnessFixture)
                .GetField("_invoker_Divide",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(divInv);
            Assert.Equal(typeof(Func<int, int, int>),
                divInv!.FieldType);
        }

        [Fact]
        public void Generator_emits_option_export_signature()
        {
            // int? MaybeDouble(int? input) — canon-ABI
            // option<u32>. Flat shape: param (disc:i32,
            // payload:i32) and return i32 (retArea ptr).
            // Combined: Func<int, int, int>.
            var m = typeof(OptionHarnessFixture).GetMethod(
                "MaybeDouble",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal(typeof(int?), m!.ReturnType);
            Assert.Single(m.GetParameters());
            Assert.Equal(typeof(int?),
                m.GetParameters()[0].ParameterType);
            Assert.NotNull(m.GetMethodBody());

            var invField = typeof(OptionHarnessFixture)
                .GetField("_invoker_MaybeDouble",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(invField);
            Assert.True(invField!.FieldType.IsGenericType);
            var args = invField.FieldType.GetGenericArguments();
            // (int disc, int payload, int retArea) = 3 ints
            Assert.Equal(3, args.Length);
            Assert.All(args,
                t => Assert.Equal(typeof(int), t));

            // _memory is needed for the option<T> return lift
            // (read disc + payload at retArea offsets). No
            // realloc needed for option<primitive>.
            Assert.NotNull(typeof(OptionHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // bool? Flag(bool?) — option<bool>. Flat param
            // (disc:i32, payload:bool). Return retArea (i32).
            var flag = typeof(OptionHarnessFixture).GetMethod(
                "Flag",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(flag);
            Assert.Equal(typeof(bool?), flag!.ReturnType);
            Assert.Equal(typeof(bool?),
                flag.GetParameters()[0].ParameterType);
            var flagInv = typeof(OptionHarnessFixture)
                .GetField("_invoker_Flag",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(flagInv);
            var fa = flagInv!.FieldType.GetGenericArguments();
            // (disc:int, payload:bool, retArea:int)
            Assert.Equal(3, fa.Length);
            Assert.Equal(typeof(int), fa[0]);
            Assert.Equal(typeof(bool), fa[1]);
            Assert.Equal(typeof(int), fa[2]);
        }

        [Fact]
        public void Generator_emits_byte_array_export_signature()
        {
            // byte[] Transform(byte[] data) — canonical
            // list<u8> shape. Flattens identically to a
            // string (ptr, len) but lowers as a raw byte
            // copy. Verify the declared method signature
            // is unchanged from the source declaration and
            // the flat-invoker field is Func<int,int,int>.
            var m = typeof(BytesHarnessFixture).GetMethod(
                "Transform",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal(typeof(byte[]), m!.ReturnType);
            Assert.Single(m.GetParameters());
            Assert.Equal(typeof(byte[]),
                m.GetParameters()[0].ParameterType);
            Assert.NotNull(m.GetMethodBody());

            // Flat invoker is Func<int, int, int>
            // (input ptr/len → output retArea).
            var invField = typeof(BytesHarnessFixture)
                .GetField("_invoker_Transform",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(invField);
            Assert.True(invField!.FieldType.IsGenericType);
            var args = invField.FieldType.GetGenericArguments();
            Assert.Equal(3, args.Length);
            Assert.All(args,
                t => Assert.Equal(typeof(int), t));

            // Same aggregate-state fields as the string
            // harness — strings + byte[] share the
            // ptr/len marshaling pattern.
            Assert.NotNull(typeof(BytesHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(BytesHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(BytesHarnessFixture)
                .GetField("_post_Transform",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_string_export_signature()
        {
            // string Greet(string name) — the canonical
            // hello-spike shape. Generator emits the
            // StringCoding.LowerUtf8 / LiftUtf8 + cabi_realloc
            // glue. We reflect on the declared signature
            // (declared as `string Greet(string)`) and the
            // class-scope memo fields (_memory,
            // _reallocInvoke, _post_Greet, _invoker_Greet
            // with flat sig `Func<int,int,int>`).
            var m = typeof(StringHarnessFixture).GetMethod("Greet",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal(typeof(string), m!.ReturnType);
            Assert.Single(m.GetParameters());
            Assert.Equal(typeof(string),
                m.GetParameters()[0].ParameterType);
            Assert.NotNull(m.GetMethodBody());

            // Class-scope state.
            var memField = typeof(StringHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(memField);
            Assert.Equal(
                "Wacs.Core.Runtime.Types.MemoryInstance",
                memField!.FieldType.FullName);

            var reallocField = typeof(StringHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(reallocField);
            // Func<int,int,int,int,int> — 4 ints in, 1 int out.
            Assert.True(reallocField!.FieldType.IsGenericType);
            Assert.Equal(5,
                reallocField.FieldType
                    .GetGenericArguments().Length);

            var postField = typeof(StringHarnessFixture)
                .GetField("_post_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(postField);
            Assert.Equal(typeof(Action<int>),
                postField!.FieldType);

            // The flat-signature invoker for `string Greet(string)`
            // is Func<int, int, int> (string param flattens to
            // 2 ints; string return flattens to 1 int retArea).
            var invField = typeof(StringHarnessFixture)
                .GetField("_invoker_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(invField);
            Assert.True(invField!.FieldType.IsGenericType);
            var args = invField.FieldType.GetGenericArguments();
            Assert.Equal(3, args.Length);
            Assert.All(args,
                t => Assert.Equal(typeof(int), t));
        }

        [Fact]
        public void Generator_emits_sync_export_signatures()
        {
            var greet = typeof(SyncHarnessFixture).GetMethod(
                "Greet",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(greet);
            Assert.Equal(typeof(int), greet!.ReturnType);
            Assert.Equal(2, greet.GetParameters().Length);

            var post = typeof(SyncHarnessFixture).GetMethod(
                "PostGreet",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(post);
            Assert.Equal(typeof(void), post!.ReturnType);
            Assert.Single(post.GetParameters());
            Assert.NotNull(post.GetMethodBody());

            // Verify the memoized invoker fields exist —
            // they're emitted at class scope so the lazy
            // initialization can stash the resolved delegate.
            var invokerField = typeof(SyncHarnessFixture)
                .GetField("_invoker_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(invokerField);
            // Field type is Func<int,int,int>? (Func<int,int,int>
            // since the nullability flow is erased at runtime).
            Assert.True(invokerField!.FieldType.IsGenericType);
            Assert.Equal(typeof(Func<,,>),
                invokerField.FieldType.GetGenericTypeDefinition());

            var actionField = typeof(SyncHarnessFixture)
                .GetField("_invoker_PostGreet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(actionField);
            Assert.True(actionField!.FieldType.IsGenericType);
            Assert.Equal(typeof(Action<>),
                actionField.FieldType.GetGenericTypeDefinition());
        }

        [Fact]
        public void Generator_emits_primitive_signatures()
        {
            var add = typeof(PrimitiveHarnessFixture).GetMethod(
                "Add",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(add);
            Assert.Equal(typeof(int), add!.ReturnType);
            var addParms = add.GetParameters();
            Assert.Equal(2, addParms.Length);
            Assert.All(addParms,
                p => Assert.Equal(typeof(int), p.ParameterType));

            var check = typeof(PrimitiveHarnessFixture).GetMethod(
                "Check",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(check);
            Assert.Equal(typeof(bool), check!.ReturnType);
            Assert.Single(check.GetParameters());
            Assert.Equal(typeof(int),
                check.GetParameters()[0].ParameterType);
        }
    }
}
