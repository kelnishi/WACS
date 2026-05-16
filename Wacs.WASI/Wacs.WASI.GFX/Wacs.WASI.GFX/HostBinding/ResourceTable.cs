// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.GFX.HostBinding
{
    /// <summary>
    /// Per-resource-type handle table — one of these per
    /// resource type the host owns (graphics-context, surface,
    /// frame-buffer device, frame-buffer buffer, abstract-buffer,
    /// pollable). Mirrors
    /// <c>Wacs.WASI.NN.HostBinding.ResourceTable</c> — separate
    /// copy here so this package doesn't need to reference
    /// WASI.NN.
    ///
    /// <para>Handle 0 is the canonical-ABI null sentinel.
    /// Allocation starts at 1; handles are not reused after
    /// drop. <see cref="Drop"/> disposes the instance if it
    /// implements <see cref="IDisposable"/>.</para>
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

        /// <summary>
        /// Drop every entry, disposing IDisposables as it goes.
        /// Used at host teardown. Snapshots the values first so
        /// dispose-time side effects can't mutate the dict
        /// mid-enumeration.
        /// </summary>
        public void Clear()
        {
            List<object> snapshot;
            lock (_lock)
            {
                snapshot = new List<object>(_handles.Values);
                _handles.Clear();
            }
            foreach (var inst in snapshot)
            {
                if (inst is IDisposable d) d.Dispose();
            }
        }
    }
}
