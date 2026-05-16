// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Routes SDL calls to the main thread. macOS's AppKit
    /// requires window + event-pump operations on the main
    /// thread; Linux/Windows tolerate worker-thread SDL but
    /// we marshal uniformly so portable code is the default.
    ///
    /// <para>Threading contract:
    /// <list type="bullet">
    /// <item><see cref="ClaimMainThread"/> captures the current
    /// thread as "the main thread" — called from
    /// <c>SilkGfxBackend.RunMainLoop</c> at entry.</item>
    /// <item><see cref="DrainPending"/> runs all queued work
    /// on the calling thread — called by RunMainLoop each
    /// pump tick.</item>
    /// <item><see cref="Invoke{T}"/> and <see cref="Invoke"/>
    /// dispatch from any thread: if the caller IS the main
    /// thread (or main thread hasn't been claimed yet), runs
    /// inline; otherwise enqueues + blocks waiting for
    /// completion.</item>
    /// </list></para>
    ///
    /// <para>"Main thread hasn't been claimed yet" lets tests
    /// and headless usage exercise SDL without spinning up
    /// the loop — degradation, not silent breakage. On macOS
    /// this still fails when the test thread isn't the
    /// process main thread; documented but tolerated.</para>
    /// </summary>
    internal sealed class MainThreadDispatcher
    {
        private readonly BlockingCollection<Action> _queue
            = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        private int _mainThreadId = -1;

        public void ClaimMainThread()
        {
            // Volatile-ish write; subsequent reads in Invoke are
            // racy but acceptable: worst case is a single round
            // of unnecessary marshaling before the new TID is
            // observed.
            Volatile.Write(ref _mainThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        public void DrainPending()
        {
            while (_queue.TryTake(out var work))
            {
                try { work(); }
                catch
                {
                    // Swallow — Invoke's TCS contract captures the
                    // exception for the calling thread; uncaught
                    // here means a fire-and-forget tail with no
                    // owner. Don't propagate into the event loop.
                }
            }
        }

        public T Invoke<T>(Func<T> fn)
        {
            int tid = Volatile.Read(ref _mainThreadId);
            if (tid < 0 || tid == Thread.CurrentThread.ManagedThreadId)
                return fn();
            T result = default!;
            Exception? caught = null;
            using var done = new ManualResetEventSlim(false);
            _queue.Add(() =>
            {
                try { result = fn(); }
                catch (Exception e) { caught = e; }
                finally { done.Set(); }
            });
            done.Wait();
            if (caught != null) throw new WasiGfxRuntimeException(caught);
            return result;
        }

        public void Invoke(Action fn)
        {
            int tid = Volatile.Read(ref _mainThreadId);
            if (tid < 0 || tid == Thread.CurrentThread.ManagedThreadId)
            {
                fn();
                return;
            }
            Exception? caught = null;
            using var done = new ManualResetEventSlim(false);
            _queue.Add(() =>
            {
                try { fn(); }
                catch (Exception e) { caught = e; }
                finally { done.Set(); }
            });
            done.Wait();
            if (caught != null) throw new WasiGfxRuntimeException(caught);
        }
    }

    /// <summary>
    /// Wraps an exception that was thrown on the main thread
    /// and re-thrown on the caller's thread. Preserves the
    /// original via <see cref="Exception.InnerException"/>.
    /// </summary>
    internal sealed class WasiGfxRuntimeException : Exception
    {
        public WasiGfxRuntimeException(Exception inner)
            : base("Main-thread SDL operation threw: " + inner.Message, inner) { }
    }
}
