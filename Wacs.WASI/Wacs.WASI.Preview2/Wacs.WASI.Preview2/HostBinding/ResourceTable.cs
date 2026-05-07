// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Per-resource-type handle table used by the host side of
    /// WASI resource imports. Maps i32 handles (the wire form
    /// of <c>own&lt;T&gt;</c> / <c>borrow&lt;T&gt;</c>) to
    /// live C# instances.
    ///
    /// <para>Per the canonical ABI, handle 0 is reserved
    /// (treated as null on the wire), so allocation starts at 1.
    /// Handles are not recycled in this v0 — a long-running
    /// component could exhaust int.MaxValue, but that's
    /// 2 billion allocations, fine for any realistic
    /// workload.</para>
    ///
    /// <para><see cref="Drop"/> calls
    /// <see cref="IDisposable.Dispose"/> on the held instance
    /// when applicable, then removes the entry. Re-dropping
    /// returns false rather than throwing, matching the
    /// canonical ABI's "drop on already-dropped is allowed"
    /// semantics.</para>
    /// </summary>
    public sealed class ResourceTable
    {
        private readonly Dictionary<int, object> _handles = new();
        private int _nextHandle = 1;

        /// <summary>Insert <paramref name="instance"/> into the
        /// table and return a fresh handle. Caller hands the
        /// handle to the wasm side as the wire form of
        /// <c>own&lt;T&gt;</c>.</summary>
        public int Allocate(object instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            var h = _nextHandle++;
            _handles[h] = instance;
            return h;
        }

        /// <summary>Look up the instance for a given handle.
        /// Throws if the handle isn't registered — the wasm
        /// side passing an invalid handle is a guest bug.</summary>
        public object Get(int handle)
        {
            if (handle == 0)
                throw new InvalidOperationException(
                    "Handle 0 is reserved as the null sentinel.");
            if (!_handles.TryGetValue(handle, out var inst))
                throw new InvalidOperationException(
                    "Resource handle " + handle + " is not "
                    + "registered — guest may have dropped it "
                    + "or never owned it.");
            return inst;
        }

        /// <summary>Remove a handle. Disposes the instance if
        /// it implements <see cref="IDisposable"/>. Returns
        /// true if the handle was registered.</summary>
        public bool Drop(int handle)
        {
            if (!_handles.TryGetValue(handle, out var inst))
                return false;
            _handles.Remove(handle);
            if (inst is IDisposable d) d.Dispose();
            return true;
        }

        /// <summary>Number of currently-live handles. Useful
        /// for tests that want to verify drop semantics.</summary>
        public int Count => _handles.Count;
    }
}
