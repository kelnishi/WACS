// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading.Tasks;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Backing storage for a Component Model <c>future&lt;T&gt;</c>
    /// handle — single-write / single-read async cell. The host
    /// surface is a CLR <see cref="Task{TResult}"/>; the dispatcher
    /// (Slice C) maps wasm <c>future.write</c> to
    /// <see cref="TrySetResult"/> and host <c>await Task</c> to
    /// the natural Task completion.
    ///
    /// <para>Backed by <see cref="TaskCompletionSource{T}"/> with
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>
    /// so the writer side never reenters wasm in-line via a
    /// continuation chain on resolution.</para>
    ///
    /// <para>Single-write semantics: <see cref="TrySetResult"/>
    /// returns <c>false</c> on the second call. The spec's
    /// <c>future.write</c> sees the same behavior — duplicate
    /// writes trap.</para>
    /// </summary>
    public sealed class FutureCell<T>
    {
        private readonly TaskCompletionSource<T> _tcs =
            new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Host await target. Completes on
        /// <see cref="TrySetResult"/>, faults on
        /// <see cref="TrySetException"/>, cancels on
        /// <see cref="TrySetCanceled"/>.</summary>
        public Task<T> Task => _tcs.Task;

        /// <summary>True iff the cell has been resolved (any path).</summary>
        public bool IsCompleted => _tcs.Task.IsCompleted;

        /// <summary>Write the value. Returns false if the cell is
        /// already resolved (matching the spec's single-write
        /// constraint).</summary>
        public bool TrySetResult(T value) => _tcs.TrySetResult(value);

        /// <summary>Fault the cell with the supplied exception.
        /// Returns false if already resolved.</summary>
        public bool TrySetException(Exception exception) =>
            _tcs.TrySetException(exception);

        /// <summary>Cancel the cell. Returns false if already resolved.</summary>
        public bool TrySetCanceled() => _tcs.TrySetCanceled();
    }
}
