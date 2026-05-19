// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.Preview3.Resources
{
    /// <summary>
    /// Typed handle table for host-side resource objects whose
    /// handles cross the canon-ABI boundary into guest code. Each
    /// WASI Preview 3 resource (fields, request, response,
    /// request-options, descriptor, tcp-socket, udp-socket) gets
    /// its own table.
    ///
    /// <para>The guest sees handles as opaque i32s; the host
    /// indirects through the table to recover the typed object
    /// for method dispatch. <c>resource.new</c>/constructor
    /// methods <see cref="Allocate(T)"/>;
    /// <c>resource.drop</c>/destructor methods
    /// <see cref="Drop(int)"/>. Method bodies look up via
    /// <see cref="Get(int)"/>.</para>
    ///
    /// <para><b>Separate from
    /// <c>Wacs.ComponentModel.Async.AsyncHandleTable&lt;T&gt;</c>:</b>
    /// the async handle table backs canon-async waitable kinds
    /// (task, stream, future, etc.) and lives in a single
    /// per-dispatcher shared namespace. Host resources are a
    /// distinct concept — each resource type has its own
    /// handle space, exactly like the WIT spec's "per-(component,
    /// resource-type)" lifetime model.</para>
    ///
    /// <para>Wire convention: handles are 4-byte positive
    /// integers, with 0 reserved as the null sentinel.
    /// Freelist recycling keeps the counter stable under
    /// allocate/drop churn.</para>
    ///
    /// <para><b>Thread-safety:</b> not thread-safe. Same
    /// constraints as <see cref="Wacs.ComponentModel.Async.AsyncHandleTable{T}"/>.</para>
    /// </summary>
    public sealed class HostResourceTable<T> where T : class
    {
        private readonly Dictionary<int, T> _entries =
            new Dictionary<int, T>();
        private readonly Stack<int> _freelist = new Stack<int>();
        private int _nextHandle = 1; // 0 reserved as null sentinel

        /// <summary>Allocate a fresh handle bound to
        /// <paramref name="value"/>. Reuses a dropped handle
        /// when one is free.</summary>
        public int Allocate(T value)
        {
            int handle = _freelist.Count > 0
                ? _freelist.Pop() : _nextHandle++;
            _entries[handle] = value;
            return handle;
        }

        /// <summary>Lookup. Returns null when the handle was
        /// never allocated or has been dropped.</summary>
        public T? Get(int handle) =>
            _entries.TryGetValue(handle, out var v) ? v : null;

        /// <summary>Release the slot back to the freelist.
        /// Returns the dropped value or null when the handle was
        /// absent.</summary>
        public T? Drop(int handle)
        {
            if (!_entries.TryGetValue(handle, out var v)) return null;
            _entries.Remove(handle);
            _freelist.Push(handle);
            return v;
        }

        /// <summary>Live entry count.</summary>
        public int Count => _entries.Count;
    }
}
