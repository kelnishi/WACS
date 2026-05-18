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

using Wacs.Core.Instructions;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// One installed entry on the active-resume-handler stack. A
    /// <c>resume $ct handler*</c> instruction pushes one of these
    /// before invoking the continuation's inner function; a
    /// <c>suspend $tag</c> raised inside that invocation walks the
    /// stack top-down looking for a matching handler.
    ///
    /// <para>The <see cref="InstallFrameDepth"/> field records the
    /// call-stack height when this handler was installed, so the
    /// dispatcher can prune stale entries on normal function
    /// return: once we have unwound past the frame that ran
    /// <c>resume</c>, its handlers are no longer in scope.</para>
    /// </summary>
    public sealed class ResumeHandlerFrame
    {
        public readonly StackSwitchHandler[] Handlers;

        /// <summary>
        /// Per-handler precomputed wasm-side branch target. Null
        /// entries (or a null array) mark the frame as
        /// transpiler-installed: the matching dispatch lives in
        /// the emitted CIL's try/catch arm, not in the interpreter,
        /// so <see cref="SuspensionDispatcher"/> unwinds to the
        /// install frame and lets the exception propagate up
        /// rather than executing an interpreter branch.
        /// </summary>
        public readonly BlockTarget?[]? HandlerTargets;
        public readonly int InstallFrameDepth;
        public readonly ContInstance Continuation;

        /// <summary>
        /// True when this frame was installed from emitted
        /// transpiler IL — the surrounding CIL owns the dispatch.
        /// </summary>
        public bool IsTranspilerInstalled => HandlerTargets is null;

        public ResumeHandlerFrame(
            StackSwitchHandler[] handlers,
            BlockTarget?[]? handlerTargets,
            int installFrameDepth,
            ContInstance cont)
        {
            Handlers = handlers;
            HandlerTargets = handlerTargets;
            InstallFrameDepth = installFrameDepth;
            Continuation = cont;
        }
    }
}
