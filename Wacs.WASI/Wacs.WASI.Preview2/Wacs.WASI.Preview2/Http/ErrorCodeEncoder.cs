// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using Wacs.ComponentModel.Runtime;

using Wacs.Core.Runtime.Types;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>Canonical-ABI encoder for the 39-case
    /// <see cref="ErrorCode"/> variant. The encoder writes
    /// the variant's wire form into a 32-byte slot at
    /// <c>retAreaPtr + offset</c>:
    /// <code>
    /// align = 8
    /// disc bytes = 1 (u8 disc)
    /// payload area = 24 bytes (max_payload =
    ///                          option&lt;field-size-payload&gt;)
    /// payload starts at offset + 8 (within the variant's
    ///                                 32-byte slot)
    /// total size = 32 bytes
    /// </code>
    /// String payloads allocate fresh memory through the
    /// supplied <c>allocate</c> callback (typically wired to
    /// <c>cabi_realloc</c>) and write a UTF-8-encoded copy
    /// at the allocated offset.</summary>
    public static class ErrorCodeEncoder
    {
        /// <summary>Total bytes the encoder writes
        /// (including the disc + padding + payload area).
        /// Always 32 — the retArea allocator must reserve
        /// at least this much for the variant.</summary>
        public const int Size = 32;

        /// <summary>The variant's natural alignment. Driven
        /// by the option<u64> case payload alignment.</summary>
        public const int Align = 8;

        /// <summary>Byte offset (within the variant slot) at
        /// which the case payload starts. Equal to
        /// <see cref="Align"/> since the disc takes one byte
        /// and is padded out to the variant alignment.</summary>
        public const int PayloadOffset = 8;

        /// <summary>WIT-spec discriminant byte for an
        /// <see cref="ErrorCode"/> case. The generator emits
        /// a flat nested-class hierarchy without a stored
        /// discriminant; this maps each subtype back to its
        /// case index per the spec WIT (0 = DNS-timeout,
        /// 38 = internal-error).</summary>
        public static byte Discriminant(ErrorCode value)
        {
            return value switch
            {
                ErrorCode.ErrorCodeDNSTimeout => 0,
                ErrorCode.ErrorCodeDNSError => 1,
                ErrorCode.ErrorCodeDestinationNotFound => 2,
                ErrorCode.ErrorCodeDestinationUnavailable => 3,
                ErrorCode.ErrorCodeDestinationIPProhibited => 4,
                ErrorCode.ErrorCodeDestinationIPUnroutable => 5,
                ErrorCode.ErrorCodeConnectionRefused => 6,
                ErrorCode.ErrorCodeConnectionTerminated => 7,
                ErrorCode.ErrorCodeConnectionTimeout => 8,
                ErrorCode.ErrorCodeConnectionReadTimeout => 9,
                ErrorCode.ErrorCodeConnectionWriteTimeout => 10,
                ErrorCode.ErrorCodeConnectionLimitReached => 11,
                ErrorCode.ErrorCodeTLSProtocolError => 12,
                ErrorCode.ErrorCodeTLSCertificateError => 13,
                ErrorCode.ErrorCodeTLSAlertReceived => 14,
                ErrorCode.ErrorCodeHTTPRequestDenied => 15,
                ErrorCode.ErrorCodeHTTPRequestLengthRequired => 16,
                ErrorCode.ErrorCodeHTTPRequestBodySize => 17,
                ErrorCode.ErrorCodeHTTPRequestMethodInvalid => 18,
                ErrorCode.ErrorCodeHTTPRequestURIInvalid => 19,
                ErrorCode.ErrorCodeHTTPRequestURITooLong => 20,
                ErrorCode.ErrorCodeHTTPRequestHeaderSectionSize => 21,
                ErrorCode.ErrorCodeHTTPRequestHeaderSize => 22,
                ErrorCode.ErrorCodeHTTPRequestTrailerSectionSize => 23,
                ErrorCode.ErrorCodeHTTPRequestTrailerSize => 24,
                ErrorCode.ErrorCodeHTTPResponseIncomplete => 25,
                ErrorCode.ErrorCodeHTTPResponseHeaderSectionSize => 26,
                ErrorCode.ErrorCodeHTTPResponseHeaderSize => 27,
                ErrorCode.ErrorCodeHTTPResponseBodySize => 28,
                ErrorCode.ErrorCodeHTTPResponseTrailerSectionSize => 29,
                ErrorCode.ErrorCodeHTTPResponseTrailerSize => 30,
                ErrorCode.ErrorCodeHTTPResponseTransferCoding => 31,
                ErrorCode.ErrorCodeHTTPResponseContentCoding => 32,
                ErrorCode.ErrorCodeHTTPResponseTimeout => 33,
                ErrorCode.ErrorCodeHTTPUpstreamResponseTimeout => 34,
                ErrorCode.ErrorCodeHTTPProtocolError => 35,
                ErrorCode.ErrorCodeLoopDetected => 36,
                ErrorCode.ErrorCodeConfigurationError => 37,
                ErrorCode.ErrorCodeInternalError => 38,
                _ => throw new ArgumentException(
                    "Unknown ErrorCode subclass: "
                    + value.GetType()),
            };
        }

        /// <summary>Write the encoded form of
        /// <paramref name="value"/> at
        /// <paramref name="offset"/> within
        /// <paramref name="memory"/>.
        /// <list type="bullet">
        /// <item>Writes the discriminant byte at offset+0</item>
        /// <item>Zeroes the padding bytes offset+1..offset+7</item>
        /// <item>Writes the case payload at offset+8 according
        /// to its WIT shape</item>
        /// <item>Zeroes any unused trailing bytes within the
        /// 32-byte slot so the guest sees a clean variant</item>
        /// </list>
        /// </summary>
        public static void Write(MemoryInstance memory, int offset,
            ErrorCode value, Func<int, int, int> allocate)
        {
            // Zero the whole 32-byte slot first; the case
            // writers only touch the bytes their payload
            // occupies, so anything else stays clean.
            for (int i = 0; i < Size; i++)
                memory.AsSpan(offset + i, 1)[0] = 0;

            memory.AsSpan(offset, 1)[0] = Discriminant(value);
            int p = offset + PayloadOffset;

            switch (value)
            {
                // No-payload cases — the disc byte is enough.
                case ErrorCode.ErrorCodeDNSTimeout:
                case ErrorCode.ErrorCodeDestinationNotFound:
                case ErrorCode.ErrorCodeDestinationUnavailable:
                case ErrorCode.ErrorCodeDestinationIPProhibited:
                case ErrorCode.ErrorCodeDestinationIPUnroutable:
                case ErrorCode.ErrorCodeConnectionRefused:
                case ErrorCode.ErrorCodeConnectionTerminated:
                case ErrorCode.ErrorCodeConnectionTimeout:
                case ErrorCode.ErrorCodeConnectionReadTimeout:
                case ErrorCode.ErrorCodeConnectionWriteTimeout:
                case ErrorCode.ErrorCodeConnectionLimitReached:
                case ErrorCode.ErrorCodeTLSProtocolError:
                case ErrorCode.ErrorCodeTLSCertificateError:
                case ErrorCode.ErrorCodeHTTPRequestDenied:
                case ErrorCode.ErrorCodeHTTPRequestLengthRequired:
                case ErrorCode.ErrorCodeHTTPRequestMethodInvalid:
                case ErrorCode.ErrorCodeHTTPRequestURIInvalid:
                case ErrorCode.ErrorCodeHTTPRequestURITooLong:
                case ErrorCode.ErrorCodeHTTPResponseIncomplete:
                case ErrorCode.ErrorCodeHTTPResponseTimeout:
                case ErrorCode.ErrorCodeHTTPUpstreamResponseTimeout:
                case ErrorCode.ErrorCodeHTTPProtocolError:
                case ErrorCode.ErrorCodeLoopDetected:
                case ErrorCode.ErrorCodeConfigurationError:
                    return;

                case ErrorCode.ErrorCodeDNSError d:
                    WriteDnsErrorPayload(memory, p,
                        d.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeTLSAlertReceived t:
                    WriteTlsAlertReceivedPayload(memory, p,
                        t.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPRequestBodySize x:
                    WriteOptionU64(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPRequestHeaderSectionSize x:
                    WriteOptionU32(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPRequestHeaderSize x:
                    WriteOptionFieldSize(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPRequestTrailerSectionSize x:
                    WriteOptionU32(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPRequestTrailerSize x:
                    WriteFieldSizePayload(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseHeaderSectionSize x:
                    WriteOptionU32(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseHeaderSize x:
                    WriteFieldSizePayload(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseBodySize x:
                    WriteOptionU64(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseTrailerSectionSize x:
                    WriteOptionU32(memory, p, x.Value);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseTrailerSize x:
                    WriteFieldSizePayload(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseTransferCoding x:
                    WriteOptionString(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeHTTPResponseContentCoding x:
                    WriteOptionString(memory, p, x.Value, allocate);
                    return;

                case ErrorCode.ErrorCodeInternalError x:
                    WriteOptionString(memory, p, x.Value, allocate);
                    return;

                default:
                    throw new ArgumentException(
                        "Unknown ErrorCode subclass: "
                        + value.GetType());
            }
        }

        // option<string>: 12 bytes — 1B disc + 3B padding +
        // 4B ptr + 4B len. ptr/len are zero when None.
        private static void WriteOptionString(MemoryInstance memory,
            int offset, Option<string> opt,
            Func<int, int, int> allocate)
        {
            if (!opt.HasValue)
            {
                memory.AsSpan(offset, 1)[0] = 0;
                return;
            }
            memory.AsSpan(offset, 1)[0] = 1;
            var bytes = Encoding.UTF8.GetBytes(opt.Value);
            int dataPtr = bytes.Length == 0 ? 0
                : allocate(1, bytes.Length);
            if (bytes.Length > 0)
                new ReadOnlySpan<byte>(bytes, 0, bytes.Length).CopyTo(memory.AsSpan(dataPtr, bytes.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                memory.AsSpan(offset + 4, 4), dataPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                memory.AsSpan(offset + 8, 4), bytes.Length);
        }

        // option<u8>: 2 bytes — disc + value.
        private static void WriteOptionU8(MemoryInstance memory,
            int offset, Option<byte> opt)
        {
            if (!opt.HasValue) { memory.AsSpan(offset, 1)[0] = 0; return; }
            memory.AsSpan(offset, 1)[0] = 1;
            memory.AsSpan(offset + 1, 1)[0] = opt.Value;
        }

        // option<u16>: 4 bytes — disc + 1B padding + 2B value.
        private static void WriteOptionU16(MemoryInstance memory,
            int offset, Option<ushort> opt)
        {
            if (!opt.HasValue) { memory.AsSpan(offset, 1)[0] = 0; return; }
            memory.AsSpan(offset, 1)[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                memory.AsSpan(offset + 2, 2), opt.Value);
        }

        // option<u32>: 8 bytes — disc + 3B padding + 4B value.
        private static void WriteOptionU32(MemoryInstance memory,
            int offset, Option<uint> opt)
        {
            if (!opt.HasValue) { memory.AsSpan(offset, 1)[0] = 0; return; }
            memory.AsSpan(offset, 1)[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                memory.AsSpan(offset + 4, 4), opt.Value);
        }

        // option<u64>: 16 bytes — disc + 7B padding + 8B value.
        private static void WriteOptionU64(MemoryInstance memory,
            int offset, Option<ulong> opt)
        {
            if (!opt.HasValue) { memory.AsSpan(offset, 1)[0] = 0; return; }
            memory.AsSpan(offset, 1)[0] = 1;
            BinaryPrimitives.WriteUInt64LittleEndian(
                memory.AsSpan(offset + 8, 8), opt.Value);
        }

        // DnsErrorPayload (16B align 4): rcode @0, info-code @12.
        private static void WriteDnsErrorPayload(MemoryInstance memory,
            int offset, DNSErrorPayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionString(memory, offset, p.Rcode, allocate);
            WriteOptionU16(memory, offset + 12, p.InfoCode);
        }

        // TlsAlertReceivedPayload (16B align 4):
        // alert-id (option<u8>) @0 (2 bytes), padding @2..3,
        // alert-message (option<string>) @4 (12 bytes).
        private static void WriteTlsAlertReceivedPayload(
            MemoryInstance memory, int offset,
            TLSAlertReceivedPayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionU8(memory, offset, p.AlertId);
            // zero pad bytes 2-3
            memory.AsSpan(offset + 2, 1)[0] = 0;
            memory.AsSpan(offset + 3, 1)[0] = 0;
            WriteOptionString(memory, offset + 4,
                p.AlertMessage, allocate);
        }

        // FieldSizePayload (20B align 4): field-name @0,
        // field-size @12.
        private static void WriteFieldSizePayload(MemoryInstance memory,
            int offset, FieldSizePayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionString(memory, offset, p.FieldName, allocate);
            WriteOptionU32(memory, offset + 12, p.FieldSize);
        }

        // option<FieldSizePayload> (24B align 4): disc @0,
        // 3B padding, payload @4 (20 bytes).
        private static void WriteOptionFieldSize(MemoryInstance memory,
            int offset, Option<FieldSizePayload> p,
            Func<int, int, int> allocate)
        {
            if (!p.HasValue) { memory.AsSpan(offset, 1)[0] = 0; return; }
            memory.AsSpan(offset, 1)[0] = 1;
            WriteFieldSizePayload(memory, offset + 4, p.Value, allocate);
        }
    }
}
