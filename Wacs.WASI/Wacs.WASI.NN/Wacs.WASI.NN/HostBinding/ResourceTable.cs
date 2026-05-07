// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.NN.HostBinding
{
    /// <summary>
    /// Per-resource-type handle table for both the WIT resource
    /// universe (tensor / graph / graph-execution-context /
    /// error) and the WITX legacy ABI's i32 handle returns.
    /// Mirrors <c>Wacs.WASI.Preview2.HostBinding.ResourceTable</c>
    /// — separate copy here so this package doesn't need to
    /// reference Preview2.
    ///
    /// <para>Handle 0 is reserved as the null sentinel (canonical
    /// ABI). Allocation starts at 1; handles are never reused
    /// in this v0. <see cref="Drop"/> disposes the instance if
    /// it implements <see cref="IDisposable"/> and removes the
    /// entry; re-dropping returns false rather than throwing,
    /// matching canonical-ABI semantics.</para>
    ///
    /// <para>Tables are scoped to one <see cref="WasiNNHost"/>
    /// instance — handles do NOT cross between hosts. The same
    /// table backs both ABIs because the host's identity, not
    /// the ABI's, is what defines a resource lifetime.</para>
    /// </summary>
    internal sealed class ResourceTable
    {
        private readonly Dictionary<int, object> _handles = new();
        private int _nextHandle = 1;
        private readonly object _lock = new();

        public int Allocate(object instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            lock (_lock)
            {
                var h = _nextHandle++;
                _handles[h] = instance;
                return h;
            }
        }

        public object Get(int handle)
        {
            if (handle == 0)
                throw new InvalidOperationException(
                    "Handle 0 is reserved as the null sentinel.");
            lock (_lock)
            {
                if (!_handles.TryGetValue(handle, out var inst))
                    throw new InvalidOperationException(
                        $"Resource handle {handle} is not registered — "
                        + "guest may have dropped it or never owned it.");
                return inst;
            }
        }

        public bool TryGet<T>(int handle, out T? instance) where T : class
        {
            if (handle == 0) { instance = null; return false; }
            lock (_lock)
            {
                if (_handles.TryGetValue(handle, out var inst) && inst is T t)
                {
                    instance = t;
                    return true;
                }
            }
            instance = null;
            return false;
        }

        public bool Drop(int handle)
        {
            object? inst;
            lock (_lock)
            {
                if (!_handles.TryGetValue(handle, out inst))
                    return false;
                _handles.Remove(handle);
            }
            if (inst is IDisposable d) d.Dispose();
            return true;
        }

        public int Count
        {
            get { lock (_lock) return _handles.Count; }
        }
    }
}
