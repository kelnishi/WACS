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
    }
}
