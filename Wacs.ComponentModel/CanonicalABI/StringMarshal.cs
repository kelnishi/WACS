// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Canonical-ABI string lift / lower helpers — the single
    /// chokepoint at which <c>System.String</c> bytes cross the
    /// component boundary. Every adapter path (interpreter
    /// binding, transpiler-emitted IL, CSharpEmit wrapper
    /// bodies) routes through these methods rather than inlining
    /// UTF-8 encode/decode calls.
    ///
    /// <para><b>Why a chokepoint:</b> WACS ships a JS-string-
    /// builtins path (<c>js-string-builtins</c> branch merged on
    /// main) that represents strings as externref handles backed
    /// by <c>System.String</c>. The Component Model will
    /// eventually grow a canonical option for JS-string
    /// encoding; when that lands we add <c>LowerJsExternref</c>
    /// / <c>LiftJsExternref</c> siblings to this class and
    /// dispatch on the option bits — not a rewrite of every
    /// adapter body. Keep UTF-8 logic behind the one name.</para>
    ///
    /// <para><b>v0 scope:</b> UTF-8 encoding only, the canonical
    /// default. UTF-16 and Latin-1+UTF-16 variants (plus the
    /// encoding-adapter negotiation matrix) land in Phase 2 per
    /// the scope plan. JS-string externref is a further future
    /// addition once the spec lands that canonical option.</para>
    /// </summary>
    public static class StringMarshal
    {
        /// <summary>
        /// Lower a <see cref="string"/> into a UTF-8 byte buffer
        /// pinned for the duration of a host-side adapter call.
        /// Returns the pinned buffer's address and length, plus a
        /// <see cref="GCHandle"/> the caller <b>must</b> free
        /// after the core call returns (usually inside a
        /// try/finally).
        ///
        /// <para>The pinning avoids a second memcpy — when the
        /// core canon-lift expects a realloc+copy into the guest
        /// memory, the guest will copy from this buffer. When
        /// the core reads directly (same-address-space hosting
        /// for the interpreter path), the buffer IS the data.</para>
        /// </summary>
        public static GCHandle LowerUtf8(string value, out nint addr, out int len)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            addr = handle.AddrOfPinnedObject();
            len = bytes.Length;
            return handle;
        }

        /// <summary>
        /// Lift a UTF-8 byte run at <paramref name="source"/>
        /// offset <paramref name="ptr"/>, length
        /// <paramref name="len"/> into a <see cref="string"/>.
        /// <paramref name="source"/> is the core module's linear
        /// memory (a <c>byte[]</c> snapshot or equivalent).
        /// </summary>
        public static string LiftUtf8(byte[] source, int ptr, int len)
        {
            if (ptr < 0 || len < 0 || ptr + len > source.Length)
                throw new ArgumentOutOfRangeException(nameof(ptr),
                    "String span out of range of provided memory buffer.");
            return Encoding.UTF8.GetString(source, ptr, len);
        }

        /// <summary>
        /// Lift a UTF-8 byte run from a <see cref="Span{T}"/>
        /// into guest memory. Used by IL-emitted lift in the
        /// transpiler path where the caller has a span over the
        /// pinned memory region rather than a managed array.
        /// </summary>
        public static string LiftUtf8(ReadOnlySpan<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
