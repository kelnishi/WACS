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
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Slim contract the WACS transpiler's <c>ThinContext</c>
    /// satisfies so the <see cref="StackSwitchingHelpers"/>
    /// runtime helpers can access the host context without
    /// taking a hard reference on <c>Wacs.Transpiler.Lib</c>'s
    /// concrete type — and without runtime reflection (AOT
    /// requirement).
    ///
    /// <para>Mixed mode populates <see cref="ExecContext"/>;
    /// standalone leaves it null and the helpers fall back to
    /// <see cref="Continuations"/> + <see cref="Tags"/> +
    /// <see cref="FuncTable"/>.</para>
    /// </summary>
    public interface IContinuationContext
    {
        ExecContext? ExecContext { get; }
        ContinuationStore Continuations { get; }
        TagInstance[] Tags { get; }
        Delegate[] FuncTable { get; }
    }

    /// <summary>
    /// IGcRef variant that exposes a delegate target — the
    /// standalone funcref encoding from the transpiler. Helpers
    /// downcast Value.GcRef to this interface to extract the
    /// invoke target without reflection.
    /// </summary>
    public interface IDelegateRef : IGcRef
    {
        Delegate Target { get; }
    }
}
