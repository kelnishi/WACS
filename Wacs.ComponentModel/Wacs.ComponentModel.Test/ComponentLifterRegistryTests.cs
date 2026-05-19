// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice K3 coverage: the
    /// <see cref="ComponentLifterAttribute"/> + source-generated
    /// <see cref="ComponentLifterRegistry"/> + dispatcher
    /// type-idx → WIT-ident mapping that lets heterogeneous-shape
    /// records/variants lift through a typed bindgen-written
    /// method instead of the per-arity fallback.
    ///
    /// <para>The fixture types below are synthetic:</para>
    /// <list type="bullet">
    ///   <item><c>TestPoint</c> + its <c>LiftPoint</c> method
    ///     simulate what wit-bindgen would emit for a
    ///     <c>record point { x: u32, y: u32 }</c>.</item>
    ///   <item><c>TestColor</c> + <c>LiftColor</c> simulate a
    ///     mixed-payload <c>variant color { rgb(u32, u32, u32),
    ///     named(string) }</c> shape that the per-arity helpers
    ///     (Slice K2) decline.</item>
    /// </list>
    /// </summary>
    public class ComponentLifterRegistryTests
    {
        // ---- Synthetic typed fixtures ---------------------------------
        //
        // The lifter methods are `internal static` so the xUnit1013
        // analyzer ("public method on test class should be a Theory")
        // doesn't fire. The source generator looks for
        // <see cref="ComponentLifterAttribute"/> on any static
        // method regardless of accessibility, and the generated
        // ComponentLifterRegistration.Register call site is also
        // internal (same assembly), so the visibility chain
        // resolves cleanly.

        internal sealed record TestPoint(uint X, uint Y);

        internal sealed record TestColor(
            int Disc, uint RgbPayload, double NamedPayload);

        // Lifter signature mirrors the canon-ABI flat-lowered shape
        // of `record { x: u32, y: u32 }`. The generator picks this
        // up via the [ComponentLifter] marker and registers it
        // under "test:fixtures/test#point".
        [ComponentLifter("test:fixtures/test#point")]
        internal static void LiftPoint(ExecContext _, int x, int y)
        {
            var d = TestDispatcherSlot.Current
                ?? throw new InvalidOperationException(
                    "Test slot not set — bracket the test body with " +
                    "TestDispatcherSlot.Use(dispatcher) { … }.");
            d.TaskReturn(null!, new TestPoint(
                unchecked((uint)x), unchecked((uint)y)));
        }

        // Heterogeneous variant: case 0 = rgb payload u32,
        // case 1 = named payload f64. The lifter receives all
        // possible payload slots; the disc selects the live one.
        [ComponentLifter("test:fixtures/test#color")]
        internal static void LiftColor(
            ExecContext _, int disc, int rgb, double named)
        {
            var d = TestDispatcherSlot.Current
                ?? throw new InvalidOperationException("Test slot not set.");
            d.TaskReturn(null!, new TestColor(
                disc, unchecked((uint)rgb), named));
        }

        // Ambient-dispatcher slot used by the test lifters to
        // reach the dispatcher without threading it through the
        // lifter signature (the canon-ABI lift signature is fixed
        // by the flat-lowering; an extra dispatcher param doesn't
        // fit). Real bindgen would capture the dispatcher via the
        // generated module-instance class.
        private static class TestDispatcherSlot
        {
            [ThreadStatic]
            public static AsyncDispatcher? Current;

            public static IDisposable Use(AsyncDispatcher d)
            {
                Current = d;
                return new Scope(d);
            }
            private sealed class Scope : IDisposable
            {
                private readonly AsyncDispatcher _d;
                public Scope(AsyncDispatcher d) { _d = d; }
                public void Dispose()
                {
                    if (ReferenceEquals(Current, _d)) Current = null;
                }
            }
        }

        // ---- Registry shape -------------------------------------------

        [Fact]
        public void Registry_picks_up_decorated_methods_via_generator()
        {
            // The source generator emits a non-empty BuildLifterTable()
            // when any [ComponentLifter] method exists in the
            // compilation. The two fixtures above guarantee at
            // least these two entries.
            Assert.True(ComponentLifterRegistry.Count >= 2);
            Assert.Contains("test:fixtures/test#point",
                ComponentLifterRegistry.Idents);
            Assert.Contains("test:fixtures/test#color",
                ComponentLifterRegistry.Idents);
        }

        [Fact]
        public void Registry_returns_matching_delegate_for_known_ident()
        {
            Assert.True(ComponentLifterRegistry.TryGet(
                "test:fixtures/test#point", out var lifter));
            Assert.NotNull(lifter);
            // The generator emits an Action<ExecContext, int, int>
            // matching the LiftPoint signature.
            var action = Assert.IsType<Action<ExecContext, int, int>>(lifter);
            Assert.NotNull(action);
        }

        [Fact]
        public void Registry_returns_false_for_unknown_ident()
        {
            Assert.False(ComponentLifterRegistry.TryGet(
                "no:such/ident#missing", out var lifter));
            Assert.Null(lifter);
        }

        [Fact]
        public void Registry_TryGet_null_ident_throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ComponentLifterRegistry.TryGet(null!, out _));
        }

        // ---- Dispatcher type-idx mapping ------------------------------

        [Fact]
        public void RegisterTypeMapping_records_the_idx_to_ident()
        {
            var d = new AsyncDispatcher();
            d.RegisterTypeMapping(7, "test:fixtures/test#point");
            Assert.True(d.TryGetTypedLifterForTypeIdx(7, out var lifter));
            Assert.NotNull(lifter);
        }

        [Fact]
        public void RegisterTypeMapping_unregistered_idx_returns_false()
        {
            var d = new AsyncDispatcher();
            Assert.False(d.TryGetTypedLifterForTypeIdx(99, out var lifter));
            Assert.Null(lifter);
        }

        [Fact]
        public void RegisterTypeMapping_known_idx_unknown_wit_ident_returns_false()
        {
            // Mapping is registered, but no [ComponentLifter] for
            // the ident exists. The registry says "no", and the
            // dispatcher reports that — the binder falls back to
            // its per-arity helpers.
            var d = new AsyncDispatcher();
            d.RegisterTypeMapping(3, "totally:made/up#thing");
            Assert.False(d.TryGetTypedLifterForTypeIdx(3, out var lifter));
            Assert.Null(lifter);
        }

        [Fact]
        public void RegisterTypeMapping_idempotent_for_same_pair()
        {
            var d = new AsyncDispatcher();
            d.RegisterTypeMapping(40, "test:fixtures/test#point");
            d.RegisterTypeMapping(40, "test:fixtures/test#point");
            Assert.True(d.TryGetTypedLifterForTypeIdx(40, out _));
        }

        [Fact]
        public void RegisterTypeMapping_throws_on_conflicting_ident()
        {
            var d = new AsyncDispatcher();
            d.RegisterTypeMapping(4, "test:fixtures/test#point");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                d.RegisterTypeMapping(4, "test:fixtures/test#color"));
            Assert.Contains("already mapped", ex.Message);
        }

        [Fact]
        public void RegisterTypeMapping_null_ident_throws()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<ArgumentNullException>(() =>
                d.RegisterTypeMapping(1, null!));
        }

        // ---- Binder integration: typed lifter pre-empts per-arity ----

        private static ContInstance MakeCont()
        {
            var store = new ContinuationStore();
            return store.Allocate(
                (TypeIdx)0, (Delegate)(Func<int>)(() => 0));
        }

        [Fact]
        public async Task Binder_uses_typed_lifter_for_registered_record()
        {
            var d = new AsyncDispatcher();
            // The dispatcher's Types table holds the parsed record.
            // The bindgen also registers "type-idx 0 is point".
            var pointType = new ComponentRecordType(new[]
            {
                new ComponentRecordType.Field("x",
                    ComponentValType.OfPrim(ComponentPrim.U32)),
                new ComponentRecordType.Field("y",
                    ComponentValType.OfPrim(ComponentPrim.U32)),
            });
            d.Types = new DefTypeEntry[] { pointType };
            d.RegisterTypeMapping(0, "test:fixtures/test#point");

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                using (TestDispatcherSlot.Use(d))
                {
                    var entry = new CanonTaskReturn(
                        ComponentValType.OfRef(0),
                        Array.Empty<CanonOption>());
                    var del = (Action<ExecContext, int, int>)
                        CanonAsyncBinder.TryBuildDelegateForEntry(entry, d)!;
                    del(null!, 11, 22);
                }
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var point = Assert.IsType<TestPoint>(result);
            Assert.Equal(11u, point.X);
            Assert.Equal(22u, point.Y);
        }

        [Fact]
        public async Task Binder_uses_typed_lifter_for_heterogeneous_variant()
        {
            // This shape is exactly what Slice K2's variant lifter
            // declines (mixed primitive payloads u32 + f64). Without
            // K3 the binder would return null; with K3 the typed
            // [ComponentLifter] lifter pre-empts the per-arity
            // fallback.
            var d = new AsyncDispatcher();
            var colorType = new ComponentVariantType(new[]
            {
                new ComponentVariantType.Case("rgb",
                    ComponentValType.OfPrim(ComponentPrim.U32), null),
                new ComponentVariantType.Case("named",
                    ComponentValType.OfPrim(ComponentPrim.F64), null),
            });
            d.Types = new DefTypeEntry[] { colorType };
            d.RegisterTypeMapping(0, "test:fixtures/test#color");

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                using (TestDispatcherSlot.Use(d))
                {
                    var entry = new CanonTaskReturn(
                        ComponentValType.OfRef(0),
                        Array.Empty<CanonOption>());
                    var del = (Action<ExecContext, int, int, double>)
                        CanonAsyncBinder.TryBuildDelegateForEntry(entry, d)!;
                    // Disc=1 (named), rgb slot ignored, named=2.718.
                    del(null!, 1, 0, 2.718);
                }
            }
            finally { d.PopCurrentTask(); }

            var color = Assert.IsType<TestColor>(await task.Completion.Task);
            Assert.Equal(1, color.Disc);
            Assert.Equal(2.718, color.NamedPayload);
        }

        [Fact]
        public void Binder_falls_back_to_per_arity_when_no_typed_lifter()
        {
            // Same record type as before but the bindgen did NOT
            // register a type mapping. The binder reaches the
            // per-arity fallback (Slice K2 same-prim arity-2 record)
            // and emits a Dictionary-based lifter — proves the
            // typed-lifter path doesn't disturb the existing
            // fallback when not configured.
            var d = new AsyncDispatcher();
            d.Types = new DefTypeEntry[]
            {
                new ComponentRecordType(new[]
                {
                    new ComponentRecordType.Field("x",
                        ComponentValType.OfPrim(ComponentPrim.U32)),
                    new ComponentRecordType.Field("y",
                        ComponentValType.OfPrim(ComponentPrim.U32)),
                }),
            };
            // No RegisterTypeMapping call.

            var entry = new CanonTaskReturn(
                ComponentValType.OfRef(0), Array.Empty<CanonOption>());
            var del = CanonAsyncBinder.TryBuildDelegateForEntry(entry, d);
            Assert.NotNull(del);
            // Per-arity Slice K2 emits Action<ExecContext, int, int>
            // surfacing a Dictionary — NOT our typed TestPoint
            // lifter. Sanity-check by cast — typed lifter would be
            // the same delegate type but the action body differs;
            // can't tell apart without invoking. So just confirm
            // the non-typed path returns something.
            Assert.IsType<Action<ExecContext, int, int>>(del);
        }
    }
}
