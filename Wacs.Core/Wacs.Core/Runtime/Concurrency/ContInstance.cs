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

using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// State of a <see cref="ContInstance"/> in the host's view.
    /// <list type="bullet">
    ///   <item><c>Fresh</c> — <c>cont.new</c> allocated this
    ///     instance; its underlying function has not been invoked.</item>
    ///   <item><c>Running</c> — entered via <c>resume</c> /
    ///     <c>resume_throw</c> / <c>switch</c> and currently
    ///     executing on the interpreter loop.</item>
    ///   <item><c>Suspended</c> — paused at a <c>suspend $tag</c>
    ///     site whose handler captured this continuation for
    ///     potential re-resumption.</item>
    ///   <item><c>Completed</c> — the underlying function returned
    ///     normally; the instance is no longer resumable.</item>
    /// </list>
    /// </summary>
    public enum ContState
    {
        Fresh,
        Running,
        Suspended,
        Completed,
    }

    /// <summary>
    /// Runtime representation of a WebAssembly Stack-Switching
    /// continuation. Backs <c>ValType.ContRef</c> /
    /// <c>ContRefNN</c> values: a <c>cont.new $ct</c> allocates one
    /// of these from a function reference, <c>cont.bind</c>
    /// partially applies args, <c>resume</c> / <c>resume_throw</c>
    /// / <c>switch</c> invoke it, and <c>suspend</c> reaches up the
    /// call stack to a handler frame and may package the rest of
    /// the running computation as a fresh <see cref="ContInstance"/>
    /// passed to that handler.
    ///
    /// <para>Values for the bound prefix are stored in
    /// <see cref="BoundArgs"/>; the caller of <c>resume</c> pushes
    /// the remaining parameters on the opstack, the dispatcher
    /// re-marshals the union onto the inner function's frame.</para>
    /// </summary>
    public sealed class ContInstance : IGcRef
    {
        /// <summary>The continuation type index that named this
        /// instance at <c>cont.new</c> time — preserved so
        /// <c>resume</c> / <c>cont.bind</c> can re-validate against
        /// the typing context.</summary>
        public readonly TypeIdx ContTypeIndex;

        /// <summary>The interpreter-side function address. In
        /// mixed mode this is the FuncAddr in the runtime's
        /// Store; in standalone mode this is unused (zero) and
        /// <see cref="StandaloneDelegate"/> carries the function
        /// reference instead.</summary>
        public readonly FuncAddr Function;

        /// <summary>Standalone-mode function reference: the CLR
        /// delegate from the transpiled module's
        /// <c>ThinContext.FuncTable</c>. Null in mixed mode where
        /// invocation routes through <see cref="Function"/>.</summary>
        public readonly System.Delegate? StandaloneDelegate;

        /// <summary>Prefix arguments pre-supplied by
        /// <c>cont.bind</c>. Empty for a fresh <c>cont.new</c>
        /// instance; populated lazily as <c>cont.bind</c> wraps a
        /// continuation in a shorter-arity outer signature.</summary>
        public readonly List<Value> BoundArgs;

        public ContState State { get; internal set; }

        public ContIdx Index { get; }

        public ContInstance(long idx, TypeIdx contTypeIndex, FuncAddr function)
        {
            Index = new ContIdx(idx);
            ContTypeIndex = contTypeIndex;
            Function = function;
            StandaloneDelegate = null;
            BoundArgs = new List<Value>();
            State = ContState.Fresh;
        }

        public ContInstance(long idx, TypeIdx contTypeIndex, System.Delegate standaloneDelegate)
        {
            Index = new ContIdx(idx);
            ContTypeIndex = contTypeIndex;
            Function = default;
            StandaloneDelegate = standaloneDelegate;
            BoundArgs = new List<Value>();
            State = ContState.Fresh;
        }

        public RefIdx StoreIndex => Index;
    }
}
