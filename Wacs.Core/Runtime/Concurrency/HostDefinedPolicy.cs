// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Concurrency policy that actually blocks on <c>atomic.wait</c> and
    /// wakes waiters on <c>atomic.notify</c>. Maintains a wait queue keyed
    /// on <c>(MemoryInstance, address)</c> pairs, with waiters registered
    /// via <see cref="ManualResetEventSlim"/>.
    /// <para>
    /// Thread-safety: the queue itself is a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/>; per-slot waiter lists
    /// are guarded by a small lock, so concurrent waits and notifies on the
    /// same address are safe.
    /// </para>
    /// </summary>
    public sealed class HostDefinedPolicy : IConcurrencyPolicy
    {
        public ConcurrencyPolicyMode Mode => ConcurrencyPolicyMode.HostDefined;

        private readonly ConcurrentDictionary<WaitKey, WaitSlot> _slots =
            new ConcurrentDictionary<WaitKey, WaitSlot>();

        private readonly struct WaitKey : System.IEquatable<WaitKey>
        {
            public readonly MemoryInstance Mem;
            public readonly long Addr;

            public WaitKey(MemoryInstance mem, long addr) { Mem = mem; Addr = addr; }

            public bool Equals(WaitKey other) =>
                ReferenceEquals(Mem, other.Mem) && Addr == other.Addr;

            public override bool Equals(object? obj) => obj is WaitKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Mem);
                    return (h * 397) ^ Addr.GetHashCode();
                }
            }
        }

        private sealed class Waiter
        {
            public readonly ManualResetEventSlim Event = new ManualResetEventSlim(false);
            public bool Notified;
        }

        private sealed class WaitSlot
        {
            public readonly object Sync = new object();
            public readonly List<Waiter> Waiters = new List<Waiter>();
        }

        public int Wait32(MemoryInstance mem, long addr, int expected, long timeoutNs)
        {
            // Re-check value under the slot's sync so that a notifier who
            // holds the lock can't race past us between the equality
            // check and our enqueue.
            var key = new WaitKey(mem, addr);
            var slot = _slots.GetOrAdd(key, _ => new WaitSlot());
            var waiter = new Waiter();

            int current;
            lock (slot.Sync)
            {
                current = mem.AtomicLoadInt32((int)addr);
                if (current != expected) return 2;
                slot.Waiters.Add(waiter);
            }

            return WaitAndDequeue(slot, waiter, timeoutNs);
        }

        public int Wait64(MemoryInstance mem, long addr, long expected, long timeoutNs)
        {
            var key = new WaitKey(mem, addr);
            var slot = _slots.GetOrAdd(key, _ => new WaitSlot());
            var waiter = new Waiter();

            long current;
            lock (slot.Sync)
            {
                current = mem.AtomicLoadInt64((int)addr);
                if (current != expected) return 2;
                slot.Waiters.Add(waiter);
            }

            return WaitAndDequeue(slot, waiter, timeoutNs);
        }

        public int Notify(MemoryInstance mem, long addr, int maxWaiters)
        {
            var key = new WaitKey(mem, addr);
            if (!_slots.TryGetValue(key, out var slot)) return 0;

            int woken = 0;
            lock (slot.Sync)
            {
                int take = maxWaiters < 0 || maxWaiters > slot.Waiters.Count
                    ? slot.Waiters.Count : maxWaiters;
                for (int i = 0; i < take; i++)
                {
                    var w = slot.Waiters[i];
                    w.Notified = true;
                    w.Event.Set();
                    woken++;
                }
                if (take > 0) slot.Waiters.RemoveRange(0, take);
            }
            return woken;
        }

        private static int WaitAndDequeue(WaitSlot slot, Waiter waiter, long timeoutNs)
        {
            int timeoutMs;
            if (timeoutNs < 0) timeoutMs = Timeout.Infinite;
            else
            {
                long ms = timeoutNs / 1_000_000;
                if (ms > int.MaxValue) ms = int.MaxValue;
                timeoutMs = (int)ms;
            }

            bool signaled = waiter.Event.Wait(timeoutMs);

            if (signaled && waiter.Notified) return 0; // ok

            // Timed out OR spurious — remove ourselves from the queue if
            // we weren't already dequeued by a concurrent notifier.
            lock (slot.Sync)
            {
                if (!waiter.Notified) slot.Waiters.Remove(waiter);
            }
            return 1; // timed-out
        }
    }
}
