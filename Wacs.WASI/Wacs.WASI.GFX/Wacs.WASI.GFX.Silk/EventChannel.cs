// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Per-event-type queue plus a list of pollables waiting
    /// for "the next event of this type". <see cref="Push"/>
    /// enqueues + signals all current waiters; new
    /// <see cref="Subscribe"/>rs after that point start fresh.
    /// <see cref="TryDequeue"/> drains; queue empties without
    /// touching the already-signaled pollables (they stay
    /// "ready" until dropped, per wasi:io/poll semantics).
    ///
    /// <para>Concurrency model: <see cref="Push"/> runs on the
    /// SDL event-pump thread (the main thread per
    /// <c>SilkGfxBackend.RunMainLoop</c>'s contract);
    /// <see cref="Subscribe"/> + <see cref="TryDequeue"/> run on
    /// the wasm guest worker thread.
    /// <see cref="ConcurrentQueue{T}"/> handles enqueue/dequeue;
    /// the waiter list is lock-guarded.</para>
    /// </summary>
    internal sealed class EventChannel<T> where T : struct
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly object _waitersLock = new object();
        private List<ManualResetPollable> _waiters = new List<ManualResetPollable>();

        public T? TryDequeue()
        {
            return _queue.TryDequeue(out var ev) ? ev : (T?)null;
        }

        public Pollable Subscribe()
        {
            var p = new ManualResetPollable();
            // Fast path: events already pending → fire
            // immediately, skip the waiter list. Matches the
            // "ready iff condition met" semantic.
            if (!_queue.IsEmpty) { p.Signal(); return p; }
            lock (_waitersLock)
            {
                if (!_queue.IsEmpty) { p.Signal(); return p; }
                _waiters.Add(p);
            }
            return p;
        }

        public void Push(T ev)
        {
            _queue.Enqueue(ev);
            List<ManualResetPollable> toSignal;
            lock (_waitersLock)
            {
                toSignal = _waiters;
                _waiters = new List<ManualResetPollable>();
            }
            foreach (var p in toSignal) p.Signal();
        }
    }
}
