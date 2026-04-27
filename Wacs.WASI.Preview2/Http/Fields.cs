// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT
    /// <c>wasi:http/types.fields</c> — case-insensitive
    /// HTTP header / trailer key-value collection.
    ///
    /// <para>v0 base class is array-backed and mutable. The
    /// WIT API has constructor + has/get/set/append/delete
    /// /entries/clone surface; this v0 ships the methods
    /// whose canon-lower shape rides existing binder paths
    /// (delete + clone). has + append + entries land as
    /// the binder gains string-param-on-primitive-return /
    /// byte[]-param-on-void+result / list-of-(string,byte[])-
    /// return support respectively.</para></summary>
    [WasiResource("fields")]
    public class Fields : IDisposable
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
        [WasiConstructor]
        public static Fields New() => new Fields();

        /// <summary>WIT <c>from-list: static func(
        ///   entries: list&lt;tuple&lt;field-key,
        ///                          field-value&gt;&gt;)
        ///   -&gt; result&lt;own&lt;fields&gt;,
        ///                header-error&gt;</c>. Bulk-construct
        /// a fields collection from a list of (key, value)
        /// pairs. Imports under <c>[static]fields.from-list</c>;
        /// the binder decodes each list element as 16 bytes
        /// (key-ptr, key-len, val-ptr, val-len). v0 always
        /// succeeds — header-error Err side not surfaced
        /// (would carry invalid-syntax(tuple<string,
        /// list<u8>>) / forbidden / immutable).</summary>
        [WasiStaticMethod]
        [WasiMethodName("from-list")]
        [WasiErrorResult]
        public static Fields FromList(
            System.ValueTuple<string, byte[]>[] entries)
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
        [WasiErrorResult]
        public virtual void Append(string name, byte[] value)
            => _entries.Add((name, value));

        /// <summary>Replace all entries matching
        /// <paramref name="name"/> with the supplied list
        /// of values. WIT
        /// <c>set: func(name: field-key,
        ///              value: list&lt;field-value&gt;)
        ///   -&gt; result&lt;_, header-error&gt;</c>.</summary>
        [WasiErrorResult]
        public virtual void Set(string name, byte[][] value)
        {
            _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));
            foreach (var v in value)
                _entries.Add((name, v));
        }

        /// <summary>Host-side alias for
        /// <see cref="Append(string, byte[])"/> kept for
        /// fixture setup paths that pre-seed the entry list
        /// without going through the canon-lower wire.</summary>
        public void AppendEntry(string name, byte[] value)
            => Append(name, value);

        /// <summary>Remove every entry matching
        /// <paramref name="name"/> (case-insensitive).</summary>
        [WasiErrorResult]
        public virtual void Delete(string name)
            => _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Deep-clone the entry list into a fresh
        /// Fields instance. WIT
        /// <c>clone: func() -&gt; fields</c>.</summary>
        public virtual Fields Clone()
        {
            var copy = new Fields();
            foreach (var (k, v) in _entries)
                copy._entries.Add((k, (byte[])v.Clone()));
            return copy;
        }

        /// <summary>Host-side List<(Key, Value)> backing
        /// store accessor. Used by tests to inspect the
        /// captured entry list; the WIT-bound entries() goes
        /// through <see cref="EntriesArray"/> which returns
        /// a fresh ValueTuple array (the shape the canon-
        /// lower binder consumes).</summary>
        public System.Collections.Generic.IReadOnlyList<
            (string Key, byte[] Value)> Entries => _entries;

        /// <summary>WIT <c>entries: func() -&gt;
        ///   list&lt;tuple&lt;field-key, field-value&gt;&gt;</c>.
        /// Snapshot of the entry list as ValueTuple<string,
        /// byte[]>[]; the binder writes the canon-lower form
        /// (list-ptr, list-len) at retArea + element pairs
        /// at the allocated array.</summary>
        [WasiMethodName("entries")]
        public virtual System.ValueTuple<string, byte[]>[] EntriesArray()
        {
            var arr = new System.ValueTuple<string, byte[]>[
                _entries.Count];
            for (int i = 0; i < _entries.Count; i++)
                arr[i] = (_entries[i].Key, _entries[i].Value);
            return arr;
        }

        public virtual void Dispose() { }
    }
}
