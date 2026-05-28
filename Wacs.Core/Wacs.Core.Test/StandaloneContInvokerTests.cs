// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Wacs.Core.Compilation;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Tests the standalone-mode dispatch contract for
    /// <c>Resume</c> / <c>Switch</c> helpers. Mocks the
    /// <see cref="IContinuationContext"/> with
    /// <c>ExecContext == null</c> and hand-rolls a
    /// <see cref="StandaloneContInvoker"/> standing in for the
    /// transpiler-generated per-signature invokers. End-to-end
    /// transpiler emission of the invokers is a separate slice.
    /// </summary>
    public class StandaloneContInvokerTests
    {
        private sealed class FakeCtx : IContinuationContext
        {
            public ExecContext? ExecContext => null;
            public ContinuationStore Continuations { get; } = new ContinuationStore();
            public TagInstance[] Tags => Array.Empty<TagInstance>();
            public Delegate[] FuncTable => Array.Empty<Delegate>();
        }

        // Per-signature invoker for () → i32 — what the
        // transpiler would emit for a continuation with that
        // signature.
        private sealed class VoidReturnsI32Invoker : StandaloneContInvoker
        {
            public override Value[] Invoke(
                IContinuationContext hctx, ContInstance cont, Value[] args)
            {
                var del = (Func<IContinuationContext, int>)cont.StandaloneDelegate!;
                int r = del(hctx);
                return new[] { new Value(r) };
            }
        }

        [Fact]
        public void Resume_standalone_with_invoker_returns_typed_result()
        {
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 99;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);

            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);
            var results = StackSwitchingHelpers.Resume(
                ctx, typeIdx: 0,
                args: Array.Empty<Value>(),
                contRef: contRef,
                handlerTagIdxs: Array.Empty<int>(),
                standaloneInvoker: new VoidReturnsI32Invoker());

            Assert.Single(results);
            Assert.Equal(99, results[0].Data.Int32);
            Assert.Equal(ContState.Completed, cont.State);
        }

        [Fact]
        public void Resume_standalone_without_invoker_throws_clear_NotSupported()
        {
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 0;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);

            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);
            var ex = Assert.Throws<NotSupportedException>(() =>
                StackSwitchingHelpers.Resume(
                    ctx, typeIdx: 0,
                    args: Array.Empty<Value>(),
                    contRef: contRef,
                    handlerTagIdxs: Array.Empty<int>(),
                    standaloneInvoker: null));
            Assert.Contains("StandaloneContInvoker", ex.Message);
        }

        [Fact]
        public void Resume_standalone_propagates_invoker_exception()
        {
            // If the inner function (via the invoker) throws,
            // the cont is marked Completed and the exception
            // propagates — the helper does not swallow.
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ =>
                throw new InvalidOperationException("kaboom");
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);
            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                StackSwitchingHelpers.Resume(
                    ctx, typeIdx: 0,
                    args: Array.Empty<Value>(),
                    contRef: contRef,
                    handlerTagIdxs: Array.Empty<int>(),
                    standaloneInvoker: new VoidReturnsI32Invoker()));
            Assert.Equal("kaboom", thrown.Message);
            Assert.Equal(ContState.Completed, cont.State);
        }

        [Fact]
        public void Resume_standalone_rejects_non_Fresh_cont()
        {
            // First resume completes the cont; the second must
            // trap because the cont is no longer Fresh.
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 0;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);
            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);
            var invoker = new VoidReturnsI32Invoker();

            StackSwitchingHelpers.Resume(
                ctx, 0, Array.Empty<Value>(), contRef,
                Array.Empty<int>(), invoker);

            Assert.Throws<TrapException>(() =>
                StackSwitchingHelpers.Resume(
                    ctx, 0, Array.Empty<Value>(), contRef,
                    Array.Empty<int>(), invoker));
        }

        [Fact]
        public void ResumeThrow_standalone_with_invoker_raises_WasmException_carrying_payload()
        {
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 42;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);
            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);

            var payload = new[] { new Value(7), new Value(11) };
            var ex = Assert.Throws<WasmException>(() =>
                StackSwitchingHelpers.ResumeThrow(
                    ctx, typeIdx: 0, tagIdxValue: 3,
                    excArgs: payload,
                    contRef: contRef,
                    handlerTagIdxs: Array.Empty<int>(),
                    standaloneInvoker: new VoidReturnsI32Invoker()));

            Assert.Equal(3, (int)ex.Exn.Tag.Value);
            Assert.Equal(2, ex.Exn.Fields.Count);
            Assert.Equal(ContState.Completed, cont.State);
        }

        [Fact]
        public void ResumeThrow_standalone_without_invoker_throws_clear_NotSupported()
        {
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 0;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);
            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);

            var ex = Assert.Throws<NotSupportedException>(() =>
                StackSwitchingHelpers.ResumeThrow(
                    ctx, typeIdx: 0, tagIdxValue: 0,
                    excArgs: Array.Empty<Value>(),
                    contRef: contRef,
                    handlerTagIdxs: Array.Empty<int>(),
                    standaloneInvoker: null));
            Assert.Contains("StandaloneContInvoker", ex.Message);
        }

        // Phase 1 acceptance criterion #2: a single-tag producer/
        // consumer co-routine running many switches without leaking
        // continuations. Our continuations are one-shot (state ends
        // Completed after first resume), so the cycle here is
        // allocate + resume + drop — repeated. The assertion is on
        // CLR heap stability across iterations: if
        // ContinuationStore retained a strong reference, the heap
        // would grow ~64–80 bytes per iteration (ContInstance +
        // List slot), producing megabytes of growth across the
        // run. The test asserts growth stays well under that.
        [Fact]
        public void Resume_one_million_iterations_does_not_leak_continuations()
        {
            // Gated to keep the unit suite snappy: this test is
            // intentionally heavy. Opt in with the same env var
            // used for AOT acceptance to keep the gate consistent.
            if (Environment.GetEnvironmentVariable("WACS_LONG_TESTS") != "1")
                return;

            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 1;
            var invoker = new VoidReturnsI32Invoker();

            // Warm-up to settle JIT/allocator behavior before the
            // baseline measurement.
            for (int i = 0; i < 1000; i++)
            {
                var c = ctx.Continuations.Allocate((TypeIdx)0, body);
                var r = new Value(ValType.ContRefNN, (IGcRef)c);
                StackSwitchingHelpers.Resume(
                    ctx, 0, Array.Empty<Value>(), r,
                    Array.Empty<int>(), invoker);
            }

            long before = GC.GetTotalMemory(forceFullCollection: true);

            for (int i = 0; i < 1_000_000; i++)
            {
                var c = ctx.Continuations.Allocate((TypeIdx)0, body);
                var r = new Value(ValType.ContRefNN, (IGcRef)c);
                StackSwitchingHelpers.Resume(
                    ctx, 0, Array.Empty<Value>(), r,
                    Array.Empty<int>(), invoker);
            }

            long after = GC.GetTotalMemory(forceFullCollection: true);
            long delta = after - before;

            // Bound: 1 MB across 1M iterations. A leaking impl
            // would grow >50 MB.
            Assert.True(delta < 1_000_000,
                $"Heap grew {delta} bytes across 1M resume cycles — " +
                $"ContinuationStore is leaking. Expected < 1MB growth.");
        }

        [Fact]
        public void ResumeThrow_standalone_rejects_non_Fresh_cont()
        {
            var ctx = new FakeCtx();
            Func<IContinuationContext, int> body = _ => 0;
            var cont = ctx.Continuations.Allocate((TypeIdx)0, body);
            var contRef = new Value(ValType.ContRefNN, (IGcRef)cont);
            var invoker = new VoidReturnsI32Invoker();

            // First call completes the cont via the body's normal
            // return, then surfaces WasmException.
            Assert.Throws<WasmException>(() =>
                StackSwitchingHelpers.ResumeThrow(
                    ctx, 0, 0, Array.Empty<Value>(), contRef,
                    Array.Empty<int>(), invoker));

            // Second call traps: cont is Completed.
            Assert.Throws<TrapException>(() =>
                StackSwitchingHelpers.ResumeThrow(
                    ctx, 0, 0, Array.Empty<Value>(), contRef,
                    Array.Empty<int>(), invoker));
        }
    }
}
