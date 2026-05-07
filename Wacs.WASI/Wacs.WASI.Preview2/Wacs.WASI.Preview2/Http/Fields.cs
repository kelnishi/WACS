// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT
    /// <c>wasi:http/types.fields</c> — case-insensitive
    /// HTTP header / trailer key-value collection.
    ///
    /// <para>Implements the generated <see cref="IFields"/>
    /// interface directly. Result-returning methods (set,
    /// delete, append, from-list) return Result of Unit /
    /// IFields over the generated <see cref="HeaderError"/>
    /// variant. v0 always returns Ok — header-error Err side
    /// not surfaced.</para></summary>
    [WasiResource("fields")]
    public class Fields : IFields, IDisposable
    {
        private readonly System.Collections.Generic.List<
            (string Key, byte[] Value)> _entries
            = new System.Collections.Generic.List<
                (string Key, byte[] Value)>();

        /// <summary>WIT <c>constructor()</c> — guest calls
        /// <c>[constructor]fields</c> to create a fresh empty
        /// fields collection. The auto-binder registers this
        /// factory under that import name and table-allocates
        /// the returned instance, handing back the i32 handle.
        /// </summary>
        public static Fields New() => new Fields();

        /// <summary>Generated-interface constructor stub —
        /// the WIT constructor is dispatched through
        /// <see cref="New"/> by the binder; this satisfies
        /// the IFields surface but is unused at runtime.</summary>
        public virtual void Create() { }

        /// <summary>WIT <c>from-list: static func(
        ///   entries: list&lt;tuple&lt;field-key,
        ///                          field-value&gt;&gt;)
        ///   -&gt; result&lt;own&lt;fields&gt;,
        ///                header-error&gt;</c>. Bulk-construct
        /// a fields collection from a list of (key, value)
        /// pairs. v0 always succeeds.</summary>
        public virtual Result<IFields, HeaderError> FromList(
            (string, byte[])[] entries)
        {
            var f = new Fields();
            foreach (var (k, v) in entries)
                f._entries.Add((k, v));
            return Result<IFields, HeaderError>.FromOk(f);
        }

        /// <summary>Static factory matching the WIT
        /// <c>[static]fields.from-list</c> wire shape;
        /// the binder allocates the resource handle from the
        /// returned concrete <see cref="Fields"/>.</summary>
        public static Fields FromListStatic(
            (string, byte[])[] entries)
        {
            var f = new Fields();
            foreach (var (k, v) in entries)
                f._entries.Add((k, v));
            return f;
        }

        /// <summary>True iff there is at least one entry
        /// with key matching <paramref name="name"/> (case-
        /// insensitive). WIT
        /// <c>has: func(name: field-key) -&gt; bool</c>.</summary>
        public virtual bool Has(string name)
        {
            foreach (var (k, _) in _entries)
                if (string.Equals(k, name,
                    System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>All values for entries matching
        /// <paramref name="name"/> (case-insensitive). WIT
        /// <c>get: func(name: field-key)
        ///   -&gt; list&lt;field-value&gt;</c>.</summary>
        public virtual byte[][] Get(string name)
        {
            var result = new System.Collections.Generic.List<byte[]>();
            foreach (var (k, v) in _entries)
                if (string.Equals(k, name,
                    System.StringComparison.OrdinalIgnoreCase))
                    result.Add(v);
            return result.ToArray();
        }

        /// <summary>Append a (key, value) entry. WIT
        /// <c>append(field-key, field-value)
        ///   -&gt; result&lt;_, header-error&gt;</c>.</summary>
        public virtual Result<Unit, HeaderError> Append(
            string name, byte[] value)
        {
            _entries.Add((name, value));
            return Result<Unit, HeaderError>.FromOk(Unit.Value);
        }

        /// <summary>Replace all entries matching
        /// <paramref name="name"/> with the supplied list
        /// of values. WIT
        /// <c>set: func(name: field-key,
        ///              value: list&lt;field-value&gt;)
        ///   -&gt; result&lt;_, header-error&gt;</c>.</summary>
        public virtual Result<Unit, HeaderError> Set(
            string name, byte[][] value)
        {
            _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));
            foreach (var v in value)
                _entries.Add((name, v));
            return Result<Unit, HeaderError>.FromOk(Unit.Value);
        }

        /// <summary>Host-side alias for
        /// <see cref="Append(string, byte[])"/> kept for
        /// fixture setup paths that pre-seed the entry list
        /// without going through the canon-lower wire.</summary>
        public void AppendEntry(string name, byte[] value)
            => _entries.Add((name, value));

        /// <summary>Remove every entry matching
        /// <paramref name="name"/> (case-insensitive).</summary>
        public virtual Result<Unit, HeaderError> Delete(string name)
        {
            _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));
            return Result<Unit, HeaderError>.FromOk(Unit.Value);
        }

        /// <summary>Deep-clone the entry list into a fresh
        /// Fields instance. WIT
        /// <c>clone: func() -&gt; fields</c>.</summary>
        public virtual IFields Clone()
        {
            var copy = new Fields();
            foreach (var (k, v) in _entries)
                copy._entries.Add((k, (byte[])v.Clone()));
            return copy;
        }

        /// <summary>Host-side List<(Key, Value)> backing
        /// store accessor. Used by tests to inspect the
        /// captured entry list. Returns the live underlying
        /// list (not a snapshot — mutations to Fields are
        /// reflected here).</summary>
        public System.Collections.Generic.IReadOnlyList<
            (string Key, byte[] Value)> EntriesList => _entries;

        /// <summary>WIT <c>entries: func() -&gt;
        ///   list&lt;tuple&lt;field-key, field-value&gt;&gt;</c>.
        /// Snapshot of the entry list as ValueTuple<string,
        /// byte[]>[].</summary>
        public virtual (string, byte[])[] Entries()
        {
            var arr = new (string, byte[])[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
                arr[i] = (_entries[i].Key, _entries[i].Value);
            return arr;
        }

        public virtual void Dispose() { }
    }
}
