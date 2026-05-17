// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// Canonical-ABI lift / lower for the WIT <c>string</c> type.
    /// v0 implements the UTF-8 path; the canonical ABI also defines
    /// UTF-16 + Latin1 encodings, deferred until the richer fixture
    /// exercises a component that opts into them.
    ///
    /// <para>Each call site in emitted harness IL becomes one
    /// invocation here, so a future swap to a different code path
    /// (e.g. the wasi js-string-builtins externref handoff, per
    /// <c>feedback_js_string_externref</c>) lands in one place.</para>
    /// </summary>
    public static class StringCoding
    {
        /// <summary>
        /// Read a UTF-8 string out of wasm linear memory:
        /// <paramref name="byteLength"/> bytes starting at
        /// <paramref name="ptr"/> are decoded to a CLR string.
        /// Used at lift sites (string flowing from wasm to host).
        /// </summary>
        public static string LiftUtf8(MemoryInstance memory, int ptr, int byteLength)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            return Encoding.UTF8.GetString(memory.Data, ptr, byteLength);
        }

        /// <summary>
        /// Allocate space inside wasm linear memory via
        /// <paramref name="cabiRealloc"/>, copy the UTF-8 bytes of
        /// <paramref name="value"/> into it, and return the
        /// (pointer, byteLength) pair. Used at lower sites (string
        /// flowing from host to wasm).
        ///
        /// <para><paramref name="cabiRealloc"/> follows the canonical
        /// ABI <c>cabi_realloc(oldPtr, oldLen, align, newLen) -&gt;
        /// newPtr</c> shape — typically a delegate captured from
        /// the component's exported <c>cabi_realloc</c> function.</para>
        /// </summary>
        public static void LowerUtf8(
            MemoryInstance memory,
            string value,
            Func<int, int, int, int, int> cabiRealloc,
            out int ptr,
            out int byteLength)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (cabiRealloc == null) throw new ArgumentNullException(nameof(cabiRealloc));

            var utf8 = Encoding.UTF8.GetBytes(value);
            byteLength = utf8.Length;
            ptr = cabiRealloc(0, 0, 1, byteLength);
            if (ptr == 0 && byteLength != 0)
                throw new OutOfMemoryException(
                    "cabi_realloc returned 0 when lowering a non-empty string.");
            utf8.CopyTo(memory.Data, ptr);
        }
    }
}
