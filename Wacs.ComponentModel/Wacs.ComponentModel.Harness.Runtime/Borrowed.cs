// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Harness.Runtime
{
    /// <summary>
    /// Lifetime-distinct view of a resource: a <c>borrow&lt;R&gt;</c>
    /// reference the host received from (or wants to pass to) wasm.
    /// Wraps the wasm-side rep directly without participating in the
    /// host handle table — borrows have no host-side lifetime to
    /// release because the canonical-ABI guarantees the lender keeps
    /// the underlying resource alive across the call.
    ///
    /// <para>The <em>type</em> distinction from <c>R</c> (which is
    /// always own-typed) is the v2 safety surface: code that took a
    /// borrow can't accidentally <c>Dispose()</c> it because the
    /// struct doesn't implement <see cref="System.IDisposable"/>.
    /// Call-scope invalidation (refusing to use a borrow after the
    /// call returns) is deferred — it needs an ExecContext hook the
    /// runtime doesn't expose yet.</para>
    ///
    /// <para>Constructed by the harness emitter when lifting a
    /// borrow return or accepting a borrow parameter; not intended
    /// for direct construction in user code (but the ctor is public
    /// so AOT-emitted IL can <c>newobj</c> it).</para>
    /// </summary>
    public readonly struct Borrowed<T> where T : class
    {
        // Public readonly field (not auto-property) so emit IL can
        // ldfld the value directly off a struct on the stack —
        // avoids the ldloca dance a get_Rep call would require.
        public readonly int Rep;

        public Borrowed(int rep) { Rep = rep; }
    }
}
