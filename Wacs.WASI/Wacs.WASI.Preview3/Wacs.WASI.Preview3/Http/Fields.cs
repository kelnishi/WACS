// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// Host interface for the WIT
    /// <c>resource fields</c> in
    /// <c>wasi:http/types@0.3.0-rc-2026-03-15</c>. Models an
    /// HTTP header/trailer bag — case-insensitive field-name
    /// lookup, multi-value per name, mutability toggle for
    /// frozen request/response fields.
    ///
    /// <para><b>Mutability.</b> A fields collection becomes
    /// immutable when attached to an in-flight request /
    /// response (the spec's "frozen" state). Set / append /
    /// delete operations on a frozen collection throw
    /// <see cref="HeaderException"/> with
    /// <see cref="HeaderError.Immutable"/>.</para>
    /// </summary>
    public interface IFields
    {
        IReadOnlyList<byte[]> Get(string name);
        bool Has(string name);
        void Set(string name, IReadOnlyList<byte[]> value);
        void Delete(string name);
        IReadOnlyList<byte[]> GetAndDelete(string name);
        void Append(string name, byte[] value);
        IReadOnlyList<(string Name, byte[] Value)> CopyAll();
        IFields Clone();

        /// <summary>Mark the collection immutable. Subsequent
        /// mutating ops throw <see cref="HeaderError.Immutable"/>.</summary>
        void Freeze();

        bool IsFrozen { get; }
    }

    /// <summary>
    /// Default in-memory <see cref="IFields"/> implementation
    /// backed by a case-insensitive dictionary of multi-valued
    /// entries. Names are stored canonical-case but compared
    /// case-insensitively, matching HTTP semantics.
    /// </summary>
    public sealed class Fields : IFields
    {
        private readonly Dictionary<string, List<byte[]>> _entries =
            new Dictionary<string, List<byte[]>>(
                StringComparer.OrdinalIgnoreCase);

        public bool IsFrozen { get; private set; }

        public Fields() { }

        /// <summary>WIT <c>from-list: static func(entries: list&lt;tuple&lt;field-name,
        /// field-value&gt;&gt;) -&gt; result&lt;fields, header-error&gt;</c>.</summary>
        public static Fields FromList(
            IReadOnlyList<(string Name, byte[] Value)> entries)
        {
            if (entries == null)
                throw new HeaderException(
                    HeaderError.InvalidSyntax, "entries is null");
            var fields = new Fields();
            foreach (var (name, value) in entries)
            {
                ValidateName(name);
                if (!fields._entries.TryGetValue(name, out var list))
                {
                    list = new List<byte[]>();
                    fields._entries[name] = list;
                }
                list.Add(value ?? Array.Empty<byte>());
            }
            return fields;
        }

        public IReadOnlyList<byte[]> Get(string name)
        {
            if (name == null) return Array.Empty<byte[]>();
            return _entries.TryGetValue(name, out var list)
                ? (IReadOnlyList<byte[]>)list
                : Array.Empty<byte[]>();
        }

        public bool Has(string name) =>
            name != null && _entries.ContainsKey(name);

        public void Set(string name, IReadOnlyList<byte[]> value)
        {
            EnsureMutable();
            ValidateName(name);
            if (value == null)
                throw new HeaderException(
                    HeaderError.InvalidSyntax, "value is null");
            _entries[name] = new List<byte[]>(value);
        }

        public void Delete(string name)
        {
            EnsureMutable();
            if (name != null) _entries.Remove(name);
        }

        public IReadOnlyList<byte[]> GetAndDelete(string name)
        {
            EnsureMutable();
            if (name == null) return Array.Empty<byte[]>();
            if (_entries.TryGetValue(name, out var list))
            {
                _entries.Remove(name);
                return list;
            }
            return Array.Empty<byte[]>();
        }

        public void Append(string name, byte[] value)
        {
            EnsureMutable();
            ValidateName(name);
            if (!_entries.TryGetValue(name, out var list))
            {
                list = new List<byte[]>();
                _entries[name] = list;
            }
            list.Add(value ?? Array.Empty<byte>());
        }

        public IReadOnlyList<(string Name, byte[] Value)> CopyAll()
        {
            var result = new List<(string Name, byte[] Value)>();
            foreach (var kv in _entries)
                foreach (var v in kv.Value)
                    result.Add((kv.Key, v));
            return result;
        }

        public IFields Clone()
        {
            var copy = new Fields();
            foreach (var kv in _entries)
                copy._entries[kv.Key] =
                    new List<byte[]>(kv.Value.Select(v => (byte[])v.Clone()));
            return copy;
        }

        public void Freeze() => IsFrozen = true;

        private void EnsureMutable()
        {
            if (IsFrozen)
                throw new HeaderException(
                    HeaderError.Immutable,
                    "fields collection is frozen; no mutation allowed.");
        }

        // Basic syntactic validation. Names must be non-empty
        // tokens per RFC 9110 §5.6.2 — printable ASCII excluding
        // delimiters. Full validation is the embedder's choice;
        // here we reject empty and obviously-bad names.
        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new HeaderException(
                    HeaderError.InvalidSyntax, "field name is empty");
            foreach (var c in name)
                if (c <= 0x20 || c >= 0x7F
                    || c == '(' || c == ')' || c == ',' || c == '/'
                    || c == ':' || c == ';' || c == '<' || c == '='
                    || c == '>' || c == '?' || c == '@' || c == '['
                    || c == '\\' || c == ']' || c == '{' || c == '}')
                    throw new HeaderException(
                        HeaderError.InvalidSyntax,
                        $"field name '{name}' contains invalid character.");
        }
    }
}
