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

        /// <summary>
        /// UTF-8 encode step, separated from the realloc+copy
        /// step so the transpiler's emitted IL can thread the
        /// byte count through a call to the guest's
        /// <c>cabi_realloc</c> before any memcpy. The IL path
        /// is: bytes = EncodeUtf8(s); len = bytes.Length;
        /// ptr = core.CabiRealloc(0, 0, 1, len); CopyToGuest(
        /// bytes, core.Memory, ptr). Keeping encode and copy
        /// as separate chokepoint methods preserves the
        /// "single UTF-8 site" discipline described in this
        /// class summary — a future <c>Encode</c> / <c>Copy</c>
        /// pair that consults a canonical-option bit for
        /// JS-string-externref slots in here without rewriting
        /// callers.
        /// </summary>
        public static byte[] EncodeUtf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        /// <summary>
        /// Copy a pre-encoded UTF-8 byte array into guest
        /// memory at <paramref name="dstPtr"/>. Companion to
        /// <see cref="EncodeUtf8"/> for the transpiler's lower
        /// path; see that method's remarks for the IL contract.
        /// </summary>
        public static void CopyToGuest(byte[] bytes, byte[] memory, int dstPtr)
        {
            if (dstPtr < 0 || dstPtr + bytes.Length > memory.Length)
                throw new ArgumentOutOfRangeException(nameof(dstPtr),
                    "UTF-8 copy span out of range of guest memory buffer.");
            Array.Copy(bytes, 0, memory, dstPtr, bytes.Length);
        }

        /// <summary>
        /// Lift a UTF-16LE byte run at <paramref name="source"/>
        /// offset <paramref name="ptr"/>, length
        /// <paramref name="codeUnitCount"/> (in u16 code units,
        /// not bytes — per CanonicalABI.md "for utf16, len is in
        /// units of u16's, not bytes"). Used when the canon-lift
        /// option specifies <c>string-encoding=utf16</c>; the
        /// guest's string buffer is laid out as 2 bytes per code
        /// unit, little-endian on every component-supporting
        /// platform we care about.
        /// </summary>
        public static string LiftUtf16(byte[] source, int ptr, int codeUnitCount)
        {
            if (ptr < 0 || codeUnitCount < 0
                || ptr + 2L * codeUnitCount > source.Length)
                throw new ArgumentOutOfRangeException(nameof(ptr),
                    "UTF-16 string span out of range of provided memory buffer.");
            return Encoding.Unicode.GetString(source, ptr, codeUnitCount * 2);
        }

        /// <summary>
        /// UTF-16LE encode step — companion to
        /// <see cref="EncodeUtf8"/> for the lower path when the
        /// canon-lower option specifies
        /// <c>string-encoding=utf16</c>. Returns the raw little-
        /// endian bytes; the IL splits them across cabi_realloc
        /// (align=2, byteLen=bytes.Length) and CopyToGuest, then
        /// pushes (ptr, codeUnitCount) on the stack — note the
        /// length passed to the guest is u16 code units, not
        /// bytes (canonical-ABI rule).
        /// </summary>
        public static byte[] EncodeUtf16(string value)
        {
            return Encoding.Unicode.GetBytes(value);
        }

        /// <summary>The high bit of <c>tagged_code_units</c>
        /// distinguishes UTF-16 (set) from Latin-1 (clear) when
        /// <c>string-encoding=latin1+utf16</c>. Mask off to read
        /// the actual count.</summary>
        public const uint Latin1OrUtf16Tag = 1u << 31;

        /// <summary>
        /// Lift a string under <c>string-encoding=latin1+utf16</c>
        /// (wasm-tools spelling: <c>compact-utf16</c>). The
        /// per-string encoding is dynamic: if the high bit of
        /// <paramref name="taggedCodeUnits"/> is set, treat as
        /// UTF-16LE with <c>byte_length = 2 * (tagged &amp; ~tag)</c>;
        /// otherwise treat as Latin-1 with <c>byte_length = tagged</c>
        /// (each byte one code point in U+0000..U+00FF).
        /// </summary>
        public static string LiftLatin1OrUtf16(byte[] source, int ptr, int taggedCodeUnits)
        {
            // Cast to uint to inspect the high bit safely — the
            // wire value is treated as unsigned per CanonicalABI.
            var tagged = (uint)taggedCodeUnits;
            if ((tagged & Latin1OrUtf16Tag) != 0)
            {
                var codeUnits = (int)(tagged & ~Latin1OrUtf16Tag);
                return LiftUtf16(source, ptr, codeUnits);
            }
            // Latin-1: each byte at source[ptr+i] is the code
            // point. ISO-8859-1 is the single-byte decoder; spelled
            // by codepage rather than the .NET 5+ Encoding.Latin1
            // shorthand to keep netstandard2.1 happy.
            var byteLen = (int)tagged;
            if (ptr < 0 || byteLen < 0 || ptr + byteLen > source.Length)
                throw new ArgumentOutOfRangeException(nameof(ptr),
                    "Latin-1 string span out of range of provided memory buffer.");
            return Latin1.GetString(source, ptr, byteLen);
        }

        /// <summary>Cached ISO-8859-1 decoder. Equivalent to
        /// .NET 5+'s <c>Encoding.Latin1</c> — separate constant
        /// keeps the netstandard2.1 build clean.</summary>
        private static readonly Encoding Latin1 =
            Encoding.GetEncoding("ISO-8859-1");

        /// <summary>Lower-path helper for
        /// <c>string-encoding=latin1+utf16</c>. The choice between
        /// Latin-1 and UTF-16 is implementation-defined — we
        /// always emit UTF-16 with the high-bit tag set: simpler
        /// emission code, always correct, never silently truncates
        /// on non-Latin-1 chars. The Latin-1-when-all-bytes-fit
        /// optimization is a follow-up.
        ///
        /// <para>Returns the same shape as <see cref="EncodeUtf16"/>;
        /// the caller stamps the high bit of the code-unit count
        /// when pushing the (ptr, taggedLen) pair on the wasm
        /// stack. Provided here as a marker for the chosen
        /// strategy so downstream emitters can find it
        /// uniformly.</para></summary>
        public static byte[] EncodeLatin1OrUtf16(string value)
        {
            return Encoding.Unicode.GetBytes(value);
        }
    }
}
