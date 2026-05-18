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

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// AOT-safe dispatch contract for invoking a transpiled
    /// continuation function in standalone mode. The transpiler
    /// generates one concrete subclass per unique continuation
    /// signature it encounters in a module — each implementation
    /// has the typed cast (<c>cont.StandaloneDelegate</c> →
    /// <c>Func&lt;ThinContext, T1, …, TR&gt;</c> or
    /// <c>Action&lt;…&gt;</c>), the typed argument unbox from
    /// <see cref="Value"/>, the call, and the result wrap back
    /// to <see cref="Value"/>.
    ///
    /// <para>Why an abstract class instead of an interface: the
    /// transpiler stores one instance per signature on a static
    /// field of the generated Functions class. Virtual dispatch
    /// is the AOT-safe equivalent of <c>delegate.DynamicInvoke</c>
    /// — no reflection, no boxing of the delegate type.</para>
    ///
    /// <para><see cref="StackSwitchingHelpers.Resume"/>,
    /// <see cref="StackSwitchingHelpers.ResumeThrow"/>, and
    /// <see cref="StackSwitchingHelpers.Switch"/> accept an
    /// optional invoker as their last parameter. Mixed-mode
    /// callers (<c>ExecContext != null</c>) ignore it.
    /// Standalone callers without an invoker get a
    /// <see cref="System.NotSupportedException"/> with a clear
    /// message; standalone callers with an invoker get the
    /// typed dispatch.</para>
    /// </summary>
    public abstract class StandaloneContInvoker
    {
        /// <summary>
        /// Invoke the continuation's underlying function with
        /// the supplied args. Implementations cast
        /// <c>cont.StandaloneDelegate</c> to their signature's
        /// strongly-typed <c>Func</c> / <c>Action</c>, unpack
        /// <paramref name="args"/> per the signature's param
        /// types, call the delegate, and wrap the typed result
        /// back into a <see cref="Value"/> array.
        ///
        /// <para>A <c>SuspensionException</c> thrown by the inner
        /// function propagates naturally — implementations need
        /// no try/catch.</para>
        /// </summary>
        public abstract Value[] Invoke(
            IContinuationContext hctx, ContInstance cont, Value[] args);
    }
}
