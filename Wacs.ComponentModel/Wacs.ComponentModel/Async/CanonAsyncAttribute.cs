// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Tags a method on <see cref="AsyncDispatcher"/> (or a
    /// subclass) as the implementation of a Component Model
    /// canon-async builtin. The carried <see cref="Name"/> is
    /// the canonical wasmtime <c>symbol_name()</c> spelling
    /// for the op — kebab-case, lowercase, no brackets:
    ///
    /// <code>
    /// task-return            stream-cancel-read
    /// task-cancel            stream-cancel-write
    /// subtask-drop           stream-drop-readable
    /// subtask-cancel         stream-drop-writable
    /// backpressure-set       future-new
    /// backpressure-inc       future-read
    /// backpressure-dec       future-write
    /// thread-yield           future-cancel-read
    /// context-get            future-cancel-write
    /// context-set            future-drop-readable
    /// waitable-set-new       future-drop-writable
    /// waitable-set-wait      error-context-new
    /// waitable-set-poll      error-context-debug-message
    /// waitable-set-drop      error-context-drop
    /// waitable-join
    /// stream-new
    /// stream-read
    /// stream-write
    /// </code>
    ///
    /// <para>This spelling matches wasmtime's
    /// <c>crates/environ/src/component/info.rs</c>
    /// <c>Trampoline::symbol_name()</c> exactly, and matches
    /// (after dot→dash normalization) the debug-name strings
    /// wit-component writes into its shim module's function-
    /// name custom section. Adopting this set as the canonical
    /// name keeps WACS interoperable with the existing
    /// wasm-tools community without inventing a parallel
    /// vocabulary.</para>
    ///
    /// <para>Discovery: at component-instantiation time, the
    /// canon-async binder walks attributed methods to build
    /// the canon-op-name → method map. In Slice G1 this is
    /// reflective (<see cref="System.Reflection"/>); in Slice
    /// G2 a Roslyn source generator emits the same map at
    /// build time, eliminating the runtime reflection.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true,
        AllowMultiple = false)]
    public sealed class CanonAsyncAttribute : Attribute
    {
        /// <summary>Wasmtime <c>symbol_name()</c> spelling.</summary>
        public string Name { get; }

        public CanonAsyncAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
