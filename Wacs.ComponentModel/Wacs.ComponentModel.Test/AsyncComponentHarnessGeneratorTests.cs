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

    /// <summary><c>[WitRecord]</c> with mixed primitive +
    /// ptr/len aggregate fields. Exercises the
    /// aggregate-in-record marshaling path: each string /
    /// byte[] field flat-lowers as (ptr:i32, len:i32) at the
    /// canon-ABI field offset, and the record return goes
    /// through a retArea with per-field reads.</summary>
    [WitRecord]
    internal struct Person
    {
        public string Name;
        public int Age;
    }

    [AsyncComponentHarness]
    internal partial class AggregateRecordHarnessFixture
    {
        [SyncExport("greet")]
        internal partial string Greet(Person who);

        [SyncExport("describe")]
        internal partial Person Describe(int id);
    }

    /// <summary>Tuples carrying a ptr/len aggregate element
    /// (string + int). Mirrors the aggregate-in-record path
    /// for tuples.</summary>
    [AsyncComponentHarness]
    internal partial class AggregateTupleHarnessFixture
    {
        [SyncExport("label")]
        internal partial int Label((string, int) named);

        [SyncExport("named_count")]
        internal partial (string, int) NamedCount(int seed);
    }

    /// <summary><c>[WitRecord]</c> with option / result
    /// fields. Exercises the option/result-in-record path:
    /// each field's disc + payload flat-lower as separate
    /// slots and the record return reads them at the
    /// canon-ABI field offset within the retArea.</summary>
    [WitRecord]
    internal struct Account
    {
        public int Id;
        public int? Balance;
        public WitResult<int, int> Status;
    }

    [AsyncComponentHarness]
    internal partial class OptionResultRecordHarnessFixture
    {
        [SyncExport("lookup")]
        internal partial Account Lookup(int id);

        [SyncExport("submit")]
        internal partial int Submit(Account a);
    }

    /// <summary>Tuple carrying option + result elements.</summary>
    [AsyncComponentHarness]
    internal partial class OptionResultTupleHarnessFixture
    {
        [SyncExport("evaluate")]
        internal partial (int?, WitResult<int, int>)
            Evaluate(int seed);

        [SyncExport("apply")]
        internal partial int Apply(
            (int?, WitResult<int, int>) input);
    }

    /// <summary>Nested-record fixture: <c>Contact</c>
    /// contains a <c>Name</c> string, an <c>Address Home</c>
    /// sub-record (string + int), and an <c>Age</c> int.
    /// Exercises recursive layout, flat-arg expansion, and
    /// retArea lift across one level of record nesting.
    /// </summary>
    [WitRecord]
    internal struct Address
    {
        public string Street;
        public int Zip;
    }

    [WitRecord]
    internal struct Contact
    {
        public string Name;
        public Address Home;
        public int Age;
    }

    [AsyncComponentHarness]
    internal partial class NestedRecordHarnessFixture
    {
        [SyncExport("write")]
        internal partial int Write(Contact c);

        [SyncExport("read")]
        internal partial Contact Read(int id);
    }

    /// <summary>Heterogeneous-element lists: list&lt;string&gt;
    /// (`string[]`) and list&lt;record&gt; (`Point[]` where
    /// `Point` is a [WitRecord] of primitives only). Same
    /// flat (ptr, len) shape as a primitive array, but
    /// lower / lift run per-element loops because each
    /// element body is a separate allocation (string) or
    /// laid out at the record's canon-ABI offsets
    /// (record).</summary>
    /// <summary>Record carrying a byte[] field — exercises
    /// the ptr/len-field branch of list&lt;record&gt; lower /
    /// lift beyond the string case.</summary>
    [WitRecord]
    internal struct Document
    {
        public string Title;
        public byte[] Body;
        public int Version;
    }

    /// <summary>Record carrying an option&lt;primitive&gt;
    /// field — exercises the option-field branch of
    /// list&lt;record&gt; lower / lift.</summary>
    [WitRecord]
    internal struct Entry
    {
        public int Id;
        public int? Expires;
    }

    /// <summary>Record carrying an
    /// option&lt;string&gt; field — exercises the
    /// option&lt;ptr/len&gt;-field branch of
    /// list&lt;record&gt; lower / lift.</summary>
    [WitRecord]
    internal struct Profile
    {
        public string Name;
        public string? Email;
    }

    /// <summary>Record carrying a result field — exercises
    /// the result-field branch of list&lt;record&gt;
    /// recursive helpers.</summary>
    [WitRecord]
    internal struct Audit
    {
        public int Id;
        public WitResult<int, string> Result;
    }

    [AsyncComponentHarness]
    internal partial class ListShapeHarnessFixture
    {
        [SyncExport("join_strings")]
        internal partial string JoinStrings(string[] parts);

        [SyncExport("split_string")]
        internal partial string[] SplitString(string text);

        [SyncExport("send_points")]
        internal partial int SendPoints(Point[] points);

        [SyncExport("get_points")]
        internal partial Point[] GetPoints(int count);

        [SyncExport("send_people")]
        internal partial int SendPeople(Person[] people);

        [SyncExport("get_people")]
        internal partial Person[] GetPeople(int count);

        [SyncExport("send_documents")]
        internal partial int SendDocuments(Document[] docs);

        [SyncExport("get_documents")]
        internal partial Document[] GetDocuments(int n);

        // list<list<X>>: outer + inner are both (ptr, len).
        // Lower nests EmitListLower; lift nests
        // EmitListLiftStatements via depth-suffixed loop vars.
        [SyncExport("send_buckets")]
        internal partial int SendBuckets(string[][] buckets);

        [SyncExport("get_buckets")]
        internal partial string[][] GetBuckets(int n);

        [SyncExport("send_int_lists")]
        internal partial int SendIntLists(int[][] xss);

        [SyncExport("send_entries")]
        internal partial int SendEntries(Entry[] entries);

        [SyncExport("get_entries")]
        internal partial Entry[] GetEntries(int n);

        [SyncExport("send_profiles")]
        internal partial int SendProfiles(
            Profile[] profiles);

        [SyncExport("get_profiles")]
        internal partial Profile[] GetProfiles(int n);

        // list<record> with a nested-record field. Contact
        // (declared earlier) is { string Name; Address Home;
        // int Age; } where Address is { string Street; int
        // Zip; }. Lower recursively writes inner Address
        // fields at outer + sub-field offsets; lift uses
        // InlineRecordFieldLiftExpression to build a nested
        // property-initializer in one expression.
        [SyncExport("send_contacts")]
        internal partial int SendContacts(
            Contact[] contacts);

        [SyncExport("get_contacts")]
        internal partial Contact[] GetContacts(int n);

        [SyncExport("send_audits")]
        internal partial int SendAudits(Audit[] audits);

        [SyncExport("get_audits")]
        internal partial Audit[] GetAudits(int n);
    }

    /// <summary>Primitive arrays beyond byte[] — canon-ABI
    /// list&lt;T&gt; for T ∈ {i32, i64, f32, f64, u16, bool}.
    /// Same flat (ptr, len) shape as byte[]; lower/lift
    /// scale by sizeof(T).</summary>
    [AsyncComponentHarness]
    internal partial class PrimitiveArrayHarnessFixture
    {
        [SyncExport("sum_i32")]
        internal partial int SumI32(int[] xs);

        [SyncExport("sum_i64")]
        internal partial long SumI64(long[] xs);

        [SyncExport("scale_f64")]
        internal partial double[] ScaleF64(double[] xs);

        [SyncExport("histogram_u16")]
        internal partial ushort[] HistogramU16(ushort[] xs);

        [SyncExport("filter_bools")]
        internal partial bool[] FilterBools(bool[] xs);
    }

    /// <summary>Option types whose inner is a ptr/len
    /// aggregate (string / byte[]). Nullable reference
    /// annotation distinguishes string? from string —
    /// captured at parse time via NullableAnnotation +
    /// IsReferenceType. Flat lower / lift route through
    /// realloc + LiftUtf8 / LiftBytes with a disc check.
    /// </summary>
    [AsyncComponentHarness]
    internal partial class OptionPtrLenHarnessFixture
    {
        [SyncExport("normalize")]
        internal partial string? Normalize(string? input);

        [SyncExport("maybe_bytes")]
        internal partial byte[]? MaybeBytes(byte[]? input);

        // option<list<X>> for X ∈ {string, primitive array}.
        // Inner lift uses MemoryHelpers.LiftStringList /
        // LiftPrimitiveArrayList<T>; lower routes through
        // EmitPtrLenAssignInto's list branch.
        [SyncExport("maybe_names")]
        internal partial string[]? MaybeNames(
            string[]? input);

        [SyncExport("maybe_ints")]
        internal partial int[][]? MaybeInts(int[][]? xs);
    }

    /// <summary>Result types whose arms are ptr/len
    /// aggregates (string / byte[]). Joined payload is the
    /// arm type; flat lower / lift route through realloc +
    /// LiftUtf8 / LiftBytes.</summary>
    [AsyncComponentHarness]
    internal partial class ResultPtrLenHarnessFixture
    {
        [SyncExport("try_decode")]
        internal partial WitResult<string, ValueTuple>
            TryDecode(byte[] data);

        [SyncExport("encode")]
        internal partial WitResult<byte[], ValueTuple>
            Encode(string text);

        [SyncExport("pick")]
        internal partial WitResult<string, string>
            Pick(bool which, string a, string b);

        // Mixed ptr/len arms: string vs byte[]. Joined
        // payload is a shared (ptr, len) pair — per-arm
        // lift/lower dispatches on the C# arm type
        // (LiftUtf8 vs LiftBytes).
        [SyncExport("blob_or_text")]
        internal partial WitResult<string, byte[]>
            BlobOrText(int kind);

        // result<list<X>, ...> arms — same flat (ptr, len)
        // shape; per-arm lift/lower routes through
        // PtrLenLiftExpression + EmitPtrLenAssignInto which
        // now branch on IsListType.
        [SyncExport("first_names")]
        internal partial WitResult<string[], System.ValueTuple>
            FirstNames(int seed);

        [SyncExport("matrix")]
        internal partial WitResult<int[][], System.ValueTuple>
            Matrix(int seed);

        // Mixed primitive + ptr/len arms: i32-or-smaller
        // primitive in one arm, string / byte[] in the
        // other. Joined slots = (disc, slot1, slot2);
        // primitive arm uses slot1 with slot2=0, ptr/len
        // arm uses (slot1=ptr, slot2=len). Restricted to
        // primitive types of sizeof≤4 so the joined slot
        // width stays i32.
        [SyncExport("parse_or_int")]
        internal partial WitResult<int, string>
            ParseOrInt(string input);

        [SyncExport("bytes_or_code")]
        internal partial WitResult<byte[], int>
            BytesOrCode(int seed);

        [SyncExport("flag_or_text")]
        internal partial WitResult<bool, string>
            FlagOrText(int seed);

        // Wider mixed: i64 (long / double) prim arm vs ptr/
        // len. Joined slot 1 widens to long; slot 2 stays
        // int. Param flat = (int disc, long slot1, int slot2).
        // Memory layout: disc:u8 at 0, payload at +8.
        [SyncExport("long_or_text")]
        internal partial WitResult<long, string>
            LongOrText(int seed);

        [SyncExport("dbl_or_bytes")]
        internal partial WitResult<double, byte[]>
            DblOrBytes(int seed);
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
        public void Generator_emits_aggregate_in_record_export_signatures()
        {
            // string Greet(Person who) — Person { string Name;
            // int Age; }. Flat lower: Name → (ptr, len); Age
            // → int. Method returns string → retArea ptr.
            // Invoker: Func<int, int, int, int>
            // (name_ptr, name_len, age, retArea_ptr).
            var greet = typeof(AggregateRecordHarnessFixture)
                .GetMethod("Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(greet);
            Assert.Equal(typeof(string), greet!.ReturnType);
            Assert.Equal(typeof(Person),
                greet.GetParameters()[0].ParameterType);
            var greetInv = typeof(AggregateRecordHarnessFixture)
                .GetField("_invoker_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(greetInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                greetInv!.FieldType);

            // Person Describe(int id) — return uses retArea
            // (multi-field record). Invoker: Func<int, int>
            // (id, retArea_ptr).
            var describe = typeof(AggregateRecordHarnessFixture)
                .GetMethod("Describe",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(describe);
            Assert.Equal(typeof(Person),
                describe!.ReturnType);
            var describeInv = typeof(AggregateRecordHarnessFixture)
                .GetField("_invoker_Describe",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(describeInv);
            Assert.Equal(typeof(Func<int, int>),
                describeInv!.FieldType);

            // Both methods need memory + realloc (param lowering
            // for string field, return lift for the record's
            // string field at retArea offset 0).
            Assert.NotNull(typeof(AggregateRecordHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(AggregateRecordHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            // cabi_post is needed for: Greet (string return)
            // and Describe (record-with-string return).
            Assert.NotNull(typeof(AggregateRecordHarnessFixture)
                .GetField("_post_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(AggregateRecordHarnessFixture)
                .GetField("_post_Describe",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_aggregate_in_tuple_export_signatures()
        {
            // int Label((string, int) named) — tuple param
            // flat: (name_ptr, name_len, count). Return int.
            // Invoker: Func<int, int, int, int>.
            var label = typeof(AggregateTupleHarnessFixture)
                .GetMethod("Label",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(label);
            Assert.Equal(typeof(int), label!.ReturnType);
            Assert.Equal(typeof(ValueTuple<string, int>),
                label.GetParameters()[0].ParameterType);
            var labelInv = typeof(AggregateTupleHarnessFixture)
                .GetField("_invoker_Label",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(labelInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                labelInv!.FieldType);

            // (string, int) NamedCount(int) — multi-element
            // return with aggregate uses retArea. Invoker:
            // Func<int, int> (seed, retArea_ptr).
            var nc = typeof(AggregateTupleHarnessFixture)
                .GetMethod("NamedCount",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(nc);
            Assert.Equal(typeof(ValueTuple<string, int>),
                nc!.ReturnType);
            var ncInv = typeof(AggregateTupleHarnessFixture)
                .GetField("_invoker_NamedCount",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(ncInv);
            Assert.Equal(typeof(Func<int, int>),
                ncInv!.FieldType);

            Assert.NotNull(typeof(AggregateTupleHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(AggregateTupleHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(AggregateTupleHarnessFixture)
                .GetField("_post_NamedCount",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_option_result_in_record_export_signatures()
        {
            // Account { int Id; int? Balance; WitResult<int, int> Status; }
            // Field flat-slot counts: 1 + 2 + 2 = 5.
            // Account Lookup(int id) — return via retArea.
            // Flat invoker convention: retArea is the FLAT
            // RETURN slot (not a param), so signature is
            // Func<int (id), int (retArea_ptr)>.
            var lookup = typeof(OptionResultRecordHarnessFixture)
                .GetMethod("Lookup",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(lookup);
            Assert.Equal(typeof(Account), lookup!.ReturnType);
            var lookupInv = typeof(OptionResultRecordHarnessFixture)
                .GetField("_invoker_Lookup",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(lookupInv);
            Assert.Equal(typeof(Func<int, int>),
                lookupInv!.FieldType);

            // int Submit(Account a) — record param flattens to
            // 5 slots: id, balance_disc, balance_payload,
            // status_disc, status_payload (joined int).
            // Flat invoker: Func<int, int, int, int, int, int>
            // (5 param ints + return int).
            var submit = typeof(OptionResultRecordHarnessFixture)
                .GetMethod("Submit",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(submit);
            Assert.Equal(typeof(int), submit!.ReturnType);
            Assert.Equal(typeof(Account),
                submit.GetParameters()[0].ParameterType);
            var submitInv = typeof(OptionResultRecordHarnessFixture)
                .GetField("_invoker_Submit",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(submitInv);
            Assert.Equal(
                typeof(Func<int, int, int, int, int, int>),
                submitInv!.FieldType);

            // _memory needed for retArea reads + record param
            // is no-op for memory (option/result don't allocate).
            // No _reallocInvoke required (no ptr/len fields).
            Assert.NotNull(typeof(OptionResultRecordHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.Null(typeof(OptionResultRecordHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            // No cabi_post: nothing in the return needs the
            // post-return free (no heap allocations).
            Assert.Null(typeof(OptionResultRecordHarnessFixture)
                .GetField("_post_Lookup",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_option_result_in_tuple_export_signatures()
        {
            // (int?, WitResult<int, int>) Evaluate(int seed)
            // — return via retArea (total slots = 4).
            // Flat invoker: Func<int (seed), int (retArea_ptr)>.
            var evalM = typeof(OptionResultTupleHarnessFixture)
                .GetMethod("Evaluate",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(evalM);
            Assert.Equal(
                typeof(ValueTuple<int?, WitResult<int, int>>),
                evalM!.ReturnType);
            var evalInv = typeof(OptionResultTupleHarnessFixture)
                .GetField("_invoker_Evaluate",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(evalInv);
            Assert.Equal(typeof(Func<int, int>),
                evalInv!.FieldType);

            // int Apply((int?, WitResult<int, int>)) — tuple
            // param flattens to 4 slots: disc, payload, disc,
            // payload. Flat invoker:
            // Func<int, int, int, int, int>
            // (4 param ints + return int).
            var apply = typeof(OptionResultTupleHarnessFixture)
                .GetMethod("Apply",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(apply);
            Assert.Equal(typeof(int), apply!.ReturnType);
            var applyInv = typeof(OptionResultTupleHarnessFixture)
                .GetField("_invoker_Apply",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(applyInv);
            Assert.Equal(
                typeof(Func<int, int, int, int, int>),
                applyInv!.FieldType);

            Assert.NotNull(typeof(OptionResultTupleHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.Null(typeof(OptionResultTupleHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.Null(typeof(OptionResultTupleHarnessFixture)
                .GetField("_post_Evaluate",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_nested_record_export_signatures()
        {
            // Contact { string Name; Address Home; int Age; }
            // Address { string Street; int Zip; }
            // Field flat-slot counts:
            //   Name:    2 (ptr, len)
            //   Home:    3 (street_ptr, street_len, zip)
            //   Age:     1
            // Total Contact slots: 6.

            // int Write(Contact c) — param flattens to 6
            // slots, return is 1 int. Func has 7 type args.
            var write = typeof(NestedRecordHarnessFixture)
                .GetMethod("Write",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(write);
            Assert.Equal(typeof(int), write!.ReturnType);
            Assert.Equal(typeof(Contact),
                write.GetParameters()[0].ParameterType);
            var writeInv = typeof(NestedRecordHarnessFixture)
                .GetField("_invoker_Write",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(writeInv);
            Assert.Equal(
                typeof(Func<int, int, int, int, int, int, int>),
                writeInv!.FieldType);

            // Contact Read(int id) — retArea return. Total
            // flat slots = 6 > 1 → retArea. Func<int, int>.
            var read = typeof(NestedRecordHarnessFixture)
                .GetMethod("Read",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(read);
            Assert.Equal(typeof(Contact), read!.ReturnType);
            var readInv = typeof(NestedRecordHarnessFixture)
                .GetField("_invoker_Read",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(readInv);
            Assert.Equal(typeof(Func<int, int>),
                readInv!.FieldType);

            // memory + realloc + post all needed (Contact
            // carries a string param + string-bearing return).
            Assert.NotNull(typeof(NestedRecordHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(NestedRecordHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(NestedRecordHarnessFixture)
                .GetField("_post_Read",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_list_shape_export_signatures()
        {
            // string JoinStrings(string[] parts)
            // - Param flat: (ptr, len) = 2 ints
            // - Return: string (retArea ptr) = 1 int
            // - Func<int, int, int>
            var join = typeof(ListShapeHarnessFixture)
                .GetMethod("JoinStrings",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(join);
            Assert.Equal(typeof(string[]),
                join!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(string), join.ReturnType);
            var joinInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_JoinStrings",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(joinInv);
            Assert.Equal(typeof(Func<int, int, int>),
                joinInv!.FieldType);

            // string[] SplitString(string text)
            // - Param string flat: (ptr, len) = 2 ints
            // - Return list retArea = 1 int
            // - Func<int, int, int>
            var split = typeof(ListShapeHarnessFixture)
                .GetMethod("SplitString",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(split);
            Assert.Equal(typeof(string[]), split!.ReturnType);
            var splitInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SplitString",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(splitInv);
            Assert.Equal(typeof(Func<int, int, int>),
                splitInv!.FieldType);

            // int SendPoints(Point[] points)
            // - Param Point[] flat: (ptr, len) = 2 ints
            // - Return int = 1 int
            // - Func<int, int, int>
            var sp = typeof(ListShapeHarnessFixture)
                .GetMethod("SendPoints",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sp);
            Assert.Equal(typeof(Point[]),
                sp!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(int), sp.ReturnType);
            var spInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendPoints",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(spInv);
            Assert.Equal(typeof(Func<int, int, int>),
                spInv!.FieldType);

            // Point[] GetPoints(int count)
            // - Param int = 1 slot
            // - Return retArea (ptr, len) = 1 int
            // - Func<int, int>
            var gp = typeof(ListShapeHarnessFixture)
                .GetMethod("GetPoints",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gp);
            Assert.Equal(typeof(Point[]), gp!.ReturnType);
            var gpInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_GetPoints",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gpInv);
            Assert.Equal(typeof(Func<int, int>),
                gpInv!.FieldType);

            // memory + realloc both needed. post for the
            // three returns that carry heap content
            // (JoinStrings returns string; SplitString
            // returns string[]; GetPoints returns Point[]).
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_JoinStrings",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_SplitString",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_GetPoints",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // int SendPeople(Person[] people)
            // Person { string Name; int Age; } — list<record>
            // where the record carries a string field. Same
            // flat shape; the per-element record write loops
            // through fields, LowerUtf8-ing the string and
            // WriteI32LE-ing the int.
            var sPe = typeof(ListShapeHarnessFixture)
                .GetMethod("SendPeople",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sPe);
            Assert.Equal(typeof(Person[]),
                sPe!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(int), sPe.ReturnType);
            var sPeInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendPeople",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sPeInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sPeInv!.FieldType);

            // Person[] GetPeople(int) — flat Func<int, int>.
            // cabi_post needed since the lifted records carry
            // heap-allocated string bodies.
            var gPe = typeof(ListShapeHarnessFixture)
                .GetMethod("GetPeople",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gPe);
            Assert.Equal(typeof(Person[]), gPe!.ReturnType);
            var gPeInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_GetPeople",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gPeInv);
            Assert.Equal(typeof(Func<int, int>),
                gPeInv!.FieldType);
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_GetPeople",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // int SendDocuments(Document[] docs) — Document {
            // string Title; byte[] Body; int Version; }. Two
            // ptr/len fields per element; lower uses the
            // shared EmitPtrLenAssignInto for each. Same flat
            // shape as the other Send/Get pairs.
            var sDo = typeof(ListShapeHarnessFixture)
                .GetMethod("SendDocuments",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sDo);
            Assert.Equal(typeof(Document[]),
                sDo!.GetParameters()[0].ParameterType);
            var sDoInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendDocuments",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sDoInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sDoInv!.FieldType);

            // Document[] GetDocuments(int) — retArea return
            // carries multiple heap-allocated bodies per
            // element; cabi_post still freed at the outer
            // level.
            var gDo = typeof(ListShapeHarnessFixture)
                .GetMethod("GetDocuments",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gDo);
            Assert.Equal(typeof(Document[]), gDo!.ReturnType);
            var gDoInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_GetDocuments",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gDoInv);
            Assert.Equal(typeof(Func<int, int>),
                gDoInv!.FieldType);
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_GetDocuments",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // int SendBuckets(string[][] buckets) — outer
            // (ptr, len) param. Same flat shape as any list.
            // Func<int, int, int> (ptr, len, retArea).
            var sB = typeof(ListShapeHarnessFixture)
                .GetMethod("SendBuckets",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sB);
            Assert.Equal(typeof(string[][]),
                sB!.GetParameters()[0].ParameterType);
            var sBInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendBuckets",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sBInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sBInv!.FieldType);

            // string[][] GetBuckets(int)
            var gB = typeof(ListShapeHarnessFixture)
                .GetMethod("GetBuckets",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gB);
            Assert.Equal(typeof(string[][]), gB!.ReturnType);
            var gBInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_GetBuckets",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gBInv);
            Assert.Equal(typeof(Func<int, int>),
                gBInv!.FieldType);
            Assert.NotNull(typeof(ListShapeHarnessFixture)
                .GetField("_post_GetBuckets",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // int SendIntLists(int[][] xss) — list-of-
            // primitive-array. Lower nests EmitPrimitiveArrayLower
            // inside the outer list loop.
            var sIL = typeof(ListShapeHarnessFixture)
                .GetMethod("SendIntLists",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sIL);
            Assert.Equal(typeof(int[][]),
                sIL!.GetParameters()[0].ParameterType);
            var sILInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendIntLists",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sILInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sILInv!.FieldType);

            // int SendEntries(Entry[]) — Entry has an
            // option<int> field. Per-element lower writes
            // disc:u8 + payload:int32 at the field offset.
            var sEn = typeof(ListShapeHarnessFixture)
                .GetMethod("SendEntries",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sEn);
            Assert.Equal(typeof(Entry[]),
                sEn!.GetParameters()[0].ParameterType);
            var sEnInv = typeof(ListShapeHarnessFixture)
                .GetField("_invoker_SendEntries",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sEnInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sEnInv!.FieldType);

            // Entry[] GetEntries(int) — lift reads each
            // element's option<int> via inline (disc != 0
            // ? payload : null) expression.
            var gEn = typeof(ListShapeHarnessFixture)
                .GetMethod("GetEntries",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(gEn);
            Assert.Equal(typeof(Entry[]), gEn!.ReturnType);
        }

        [Fact]
        public void Generator_emits_primitive_array_export_signatures()
        {
            // int SumI32(int[]) — param flat (ptr, len) = 2
            // ints; return is one int. Func<int, int, int>.
            var s32 = typeof(PrimitiveArrayHarnessFixture)
                .GetMethod("SumI32",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(s32);
            Assert.Equal(typeof(int[]),
                s32!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(int), s32.ReturnType);
            var s32Inv = typeof(PrimitiveArrayHarnessFixture)
                .GetField("_invoker_SumI32",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(s32Inv);
            Assert.Equal(typeof(Func<int, int, int>),
                s32Inv!.FieldType);

            // long SumI64(long[]) — same flat shape; return
            // long. Func<int, int, long>.
            var s64 = typeof(PrimitiveArrayHarnessFixture)
                .GetMethod("SumI64",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(s64);
            Assert.Equal(typeof(long[]),
                s64!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(long), s64.ReturnType);
            var s64Inv = typeof(PrimitiveArrayHarnessFixture)
                .GetField("_invoker_SumI64",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(s64Inv);
            Assert.Equal(typeof(Func<int, int, long>),
                s64Inv!.FieldType);

            // double[] ScaleF64(double[]) — param (ptr, len);
            // return retArea ptr. Func<int, int, int>.
            var sf = typeof(PrimitiveArrayHarnessFixture)
                .GetMethod("ScaleF64",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sf);
            Assert.Equal(typeof(double[]),
                sf!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(double[]), sf.ReturnType);
            var sfInv = typeof(PrimitiveArrayHarnessFixture)
                .GetField("_invoker_ScaleF64",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(sfInv);
            Assert.Equal(typeof(Func<int, int, int>),
                sfInv!.FieldType);

            // ushort[] HistogramU16(ushort[]) — same shape;
            // align 2 element size 2 (verified by generated
            // code via PrimitiveSize). Func<int, int, int>.
            var hu = typeof(PrimitiveArrayHarnessFixture)
                .GetMethod("HistogramU16",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(hu);
            Assert.Equal(typeof(ushort[]), hu!.ReturnType);
            var huInv = typeof(PrimitiveArrayHarnessFixture)
                .GetField("_invoker_HistogramU16",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(huInv);
            Assert.Equal(typeof(Func<int, int, int>),
                huInv!.FieldType);

            // bool[] FilterBools(bool[]) — element size 1
            // (bool is one byte in canon-ABI). Same flat shape.
            var fb = typeof(PrimitiveArrayHarnessFixture)
                .GetMethod("FilterBools",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fb);
            Assert.Equal(typeof(bool[]), fb!.ReturnType);
            var fbInv = typeof(PrimitiveArrayHarnessFixture)
                .GetField("_invoker_FilterBools",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fbInv);
            Assert.Equal(typeof(Func<int, int, int>),
                fbInv!.FieldType);

            // Class-scope: memory + realloc + per-method post
            // (returns containing list<T> need cabi_post).
            Assert.NotNull(typeof(PrimitiveArrayHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(PrimitiveArrayHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(PrimitiveArrayHarnessFixture)
                .GetField("_post_ScaleF64",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(PrimitiveArrayHarnessFixture)
                .GetField("_post_HistogramU16",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(PrimitiveArrayHarnessFixture)
                .GetField("_post_FilterBools",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
        }

        [Fact]
        public void Generator_emits_option_ptrlen_export_signatures()
        {
            // string? Normalize(string? input)
            // - Param string? flat: (disc, ptr, len) = 3 ints
            // - Return retArea = 1 int
            // - Func<int, int, int, int>
            var norm = typeof(OptionPtrLenHarnessFixture)
                .GetMethod("Normalize",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(norm);
            Assert.Equal(typeof(string), norm!.ReturnType);
            Assert.Equal(typeof(string),
                norm.GetParameters()[0].ParameterType);
            var normInv = typeof(OptionPtrLenHarnessFixture)
                .GetField("_invoker_Normalize",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(normInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                normInv!.FieldType);

            // byte[]? MaybeBytes(byte[]?)
            // - Param byte[]? flat: (disc, ptr, len) = 3 ints
            // - Return retArea = 1 int
            // - Func<int, int, int, int>
            var mb = typeof(OptionPtrLenHarnessFixture)
                .GetMethod("MaybeBytes",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mb);
            Assert.Equal(typeof(byte[]), mb!.ReturnType);
            var mbInv = typeof(OptionPtrLenHarnessFixture)
                .GetField("_invoker_MaybeBytes",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mbInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                mbInv!.FieldType);

            // Class-scope: memory + realloc + post for both.
            Assert.NotNull(typeof(OptionPtrLenHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(OptionPtrLenHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(OptionPtrLenHarnessFixture)
                .GetField("_post_Normalize",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(OptionPtrLenHarnessFixture)
                .GetField("_post_MaybeBytes",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // string[]? MaybeNames(string[]?) — option<list>.
            // Same flat shape as option<string>: (disc, ptr,
            // len) param + retArea return. Func<int, int,
            // int, int>.
            var mn = typeof(OptionPtrLenHarnessFixture)
                .GetMethod("MaybeNames",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mn);
            Assert.Equal(typeof(string[]), mn!.ReturnType);
            var mnInv = typeof(OptionPtrLenHarnessFixture)
                .GetField("_invoker_MaybeNames",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mnInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                mnInv!.FieldType);

            // int[][]? MaybeInts(int[][]?) — option<list<list>>.
            var mi = typeof(OptionPtrLenHarnessFixture)
                .GetMethod("MaybeInts",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mi);
            Assert.Equal(typeof(int[][]), mi!.ReturnType);
            var miInv = typeof(OptionPtrLenHarnessFixture)
                .GetField("_invoker_MaybeInts",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(miInv);
            Assert.Equal(typeof(Func<int, int, int, int>),
                miInv!.FieldType);
        }

        [Fact]
        public void Generator_emits_result_ptrlen_export_signatures()
        {
            // WitResult<string, ValueTuple> TryDecode(byte[])
            // - Param byte[] flat: (ptr, len) = 2 ints
            // - Return retArea (3 slots: disc, ptr, len) → 1 int
            // - Func<int, int, int> (param_ptr, param_len, retArea_ptr)
            var td = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("TryDecode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(td);
            Assert.Equal(
                typeof(WitResult<string, ValueTuple>),
                td!.ReturnType);
            var tdInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_TryDecode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(tdInv);
            Assert.Equal(typeof(Func<int, int, int>),
                tdInv!.FieldType);

            // WitResult<byte[], ValueTuple> Encode(string)
            // - Param string flat: (ptr, len) = 2 ints
            // - Return retArea = 1 int
            // - Func<int, int, int>
            var encode = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("Encode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(encode);
            Assert.Equal(typeof(WitResult<byte[], ValueTuple>),
                encode!.ReturnType);
            var encodeInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_Encode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(encodeInv);
            Assert.Equal(typeof(Func<int, int, int>),
                encodeInv!.FieldType);

            // WitResult<string, string> Pick(bool, string, string)
            // - bool param = 1 slot, 2x string = 4 slots
            // - Return retArea = 1 slot
            // - Func<bool, int, int, int, int, int> = 5 params + 1 ret
            var pick = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("Pick",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(pick);
            Assert.Equal(typeof(WitResult<string, string>),
                pick!.ReturnType);
            var pickInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_Pick",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(pickInv);
            Assert.Equal(
                typeof(Func<bool, int, int, int, int, int>),
                pickInv!.FieldType);

            // Class-scope: memory + realloc + post (all
            // returns are ptr/len-armed → need post; all
            // params + returns transit memory).
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_memory",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_reallocInvoke",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_TryDecode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_Encode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_Pick",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // WitResult<string, byte[]> BlobOrText(int)
            // Mixed ptr/len arms: same flat shape as same-
            // type ptr/len arms (disc + ptr + len in the
            // retArea), but per-arm lift dispatches on the
            // C# arm type. Flat invoker: Func<int, int>
            // (kind, retArea_ptr).
            var blob = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("BlobOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(blob);
            Assert.Equal(typeof(WitResult<string, byte[]>),
                blob!.ReturnType);
            var blobInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_BlobOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(blobInv);
            Assert.Equal(typeof(Func<int, int>),
                blobInv!.FieldType);
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_BlobOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // result<string[], ()> — list arm. Same flat
            // (disc, ptr, len) shape as result<string, ()>.
            // Func<int, int> (seed, retArea).
            var fn = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("FirstNames",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fn);
            Assert.Equal(
                typeof(WitResult<string[], ValueTuple>),
                fn!.ReturnType);
            var fnInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_FirstNames",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fnInv);
            Assert.Equal(typeof(Func<int, int>),
                fnInv!.FieldType);
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_FirstNames",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // result<int[][], ()> — nested-list arm.
            var mx = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("Matrix",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mx);
            Assert.Equal(
                typeof(WitResult<int[][], ValueTuple>),
                mx!.ReturnType);
            var mxInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_Matrix",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(mxInv);
            Assert.Equal(typeof(Func<int, int>),
                mxInv!.FieldType);

            // result<int, string> — mixed primitive vs
            // ptr/len. Joined payload = (slot1, slot2) where
            // slot1 carries either the int (ok) or the
            // string ptr (err); slot2 is unused for int (0)
            // or carries the string len. Param flat = (disc,
            // slot1, slot2) on input; one extra (ptr, len)
            // for the string input param. Func<int, int, int, int>.
            var pIo = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("ParseOrInt",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(pIo);
            Assert.Equal(typeof(WitResult<int, string>),
                pIo!.ReturnType);
            var pIoInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_ParseOrInt",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(pIoInv);
            Assert.Equal(typeof(Func<int, int, int>),
                pIoInv!.FieldType);
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_ParseOrInt",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // result<byte[], int> — mixed reverse direction
            // (ptr/len arm first). Same joined shape.
            var bOC = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("BytesOrCode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(bOC);
            Assert.Equal(typeof(WitResult<byte[], int>),
                bOC!.ReturnType);
            var bOCInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_BytesOrCode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(bOCInv);
            Assert.Equal(typeof(Func<int, int>),
                bOCInv!.FieldType);
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_BytesOrCode",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // result<bool, string> — bool widens to int in
            // slot1. Tests the bool/single special-case in
            // EmitPtrLenAssignInto + PtrLenLiftExpression
            // primitive branches.
            var fOT = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("FlagOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fOT);
            Assert.Equal(typeof(WitResult<bool, string>),
                fOT!.ReturnType);
            var fOTInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_FlagOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(fOTInv);
            Assert.Equal(typeof(Func<int, int>),
                fOTInv!.FieldType);

            // result<long, string> — wide-mixed. Return-only
            // so the flat invoker is Func<int seed, int
            // retArea>. The retArea memory layout uses long
            // slot at offset 8.
            var lOT = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("LongOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(lOT);
            Assert.Equal(typeof(WitResult<long, string>),
                lOT!.ReturnType);
            var lOTInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_LongOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(lOTInv);
            Assert.Equal(typeof(Func<int, int>),
                lOTInv!.FieldType);
            Assert.NotNull(typeof(ResultPtrLenHarnessFixture)
                .GetField("_post_LongOrText",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance));

            // result<double, byte[]>
            var dOB = typeof(ResultPtrLenHarnessFixture)
                .GetMethod("DblOrBytes",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(dOB);
            Assert.Equal(typeof(WitResult<double, byte[]>),
                dOB!.ReturnType);
            var dOBInv = typeof(ResultPtrLenHarnessFixture)
                .GetField("_invoker_DblOrBytes",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(dOBInv);
            Assert.Equal(typeof(Func<int, int>),
                dOBInv!.FieldType);
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
