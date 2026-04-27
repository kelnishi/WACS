// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;

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
        public static void Write(byte[] memory, int offset,
            ErrorCode value, Func<int, int, int> allocate)
        {
            // Zero the whole 32-byte slot first; the case
            // writers only touch the bytes their payload
            // occupies, so anything else stays clean.
            for (int i = 0; i < Size; i++)
                memory[offset + i] = 0;

            memory[offset] = value.Discriminant;
            int p = offset + PayloadOffset;

            switch (value)
            {
                // No-payload cases — the disc byte is enough.
                case ErrorCodeDnsTimeout:
                case ErrorCodeDestinationNotFound:
                case ErrorCodeDestinationUnavailable:
                case ErrorCodeDestinationIpProhibited:
                case ErrorCodeDestinationIpUnroutable:
                case ErrorCodeConnectionRefused:
                case ErrorCodeConnectionTerminated:
                case ErrorCodeConnectionTimeout:
                case ErrorCodeConnectionReadTimeout:
                case ErrorCodeConnectionWriteTimeout:
                case ErrorCodeConnectionLimitReached:
                case ErrorCodeTlsProtocolError:
                case ErrorCodeTlsCertificateError:
                case ErrorCodeHttpRequestDenied:
                case ErrorCodeHttpRequestLengthRequired:
                case ErrorCodeHttpRequestMethodInvalid:
                case ErrorCodeHttpRequestUriInvalid:
                case ErrorCodeHttpRequestUriTooLong:
                case ErrorCodeHttpResponseIncomplete:
                case ErrorCodeHttpResponseTimeout:
                case ErrorCodeHttpUpstreamResponseTimeout:
                case ErrorCodeHttpProtocolError:
                case ErrorCodeLoopDetected:
                case ErrorCodeConfigurationError:
                    return;

                case ErrorCodeDnsError d:
                    WriteDnsErrorPayload(memory, p,
                        d.Payload, allocate);
                    return;

                case ErrorCodeTlsAlertReceived t:
                    WriteTlsAlertReceivedPayload(memory, p,
                        t.Payload, allocate);
                    return;

                case ErrorCodeHttpRequestBodySize x:
                    WriteOptionU64(memory, p, x.Size);
                    return;

                case ErrorCodeHttpRequestHeaderSectionSize x:
                    WriteOptionU32(memory, p, x.Size);
                    return;

                case ErrorCodeHttpRequestHeaderSize x:
                    WriteOptionFieldSize(memory, p, x.Field, allocate);
                    return;

                case ErrorCodeHttpRequestTrailerSectionSize x:
                    WriteOptionU32(memory, p, x.Size);
                    return;

                case ErrorCodeHttpRequestTrailerSize x:
                    WriteFieldSizePayload(memory, p, x.Field, allocate);
                    return;

                case ErrorCodeHttpResponseHeaderSectionSize x:
                    WriteOptionU32(memory, p, x.Size);
                    return;

                case ErrorCodeHttpResponseHeaderSize x:
                    WriteFieldSizePayload(memory, p, x.Field, allocate);
                    return;

                case ErrorCodeHttpResponseBodySize x:
                    WriteOptionU64(memory, p, x.Size);
                    return;

                case ErrorCodeHttpResponseTrailerSectionSize x:
                    WriteOptionU32(memory, p, x.Size);
                    return;

                case ErrorCodeHttpResponseTrailerSize x:
                    WriteFieldSizePayload(memory, p, x.Field, allocate);
                    return;

                case ErrorCodeHttpResponseTransferCoding x:
                    WriteOptionString(memory, p, x.Coding, allocate);
                    return;

                case ErrorCodeHttpResponseContentCoding x:
                    WriteOptionString(memory, p, x.Coding, allocate);
                    return;

                case ErrorCodeInternalError x:
                    WriteOptionString(memory, p, x.Description, allocate);
                    return;

                default:
                    throw new ArgumentException(
                        "Unknown ErrorCode subclass: "
                        + value.GetType());
            }
        }

        // option<string>: 12 bytes — 1B disc + 3B padding +
        // 4B ptr + 4B len. ptr/len are zero when None.
        private static void WriteOptionString(byte[] memory,
            int offset, string? s, Func<int, int, int> allocate)
        {
            if (s == null)
            {
                memory[offset] = 0;
                return;
            }
            memory[offset] = 1;
            var bytes = Encoding.UTF8.GetBytes(s);
            int dataPtr = bytes.Length == 0 ? 0
                : allocate(1, bytes.Length);
            if (bytes.Length > 0)
                Array.Copy(bytes, 0, memory, dataPtr, bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                memory.AsSpan(offset + 4, 4), dataPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                memory.AsSpan(offset + 8, 4), bytes.Length);
        }

        // option<u8>: 2 bytes — disc + value.
        private static void WriteOptionU8(byte[] memory,
            int offset, byte? v)
        {
            if (v == null) { memory[offset] = 0; return; }
            memory[offset] = 1;
            memory[offset + 1] = v.Value;
        }

        // option<u16>: 4 bytes — disc + 1B padding + 2B value.
        private static void WriteOptionU16(byte[] memory,
            int offset, ushort? v)
        {
            if (v == null) { memory[offset] = 0; return; }
            memory[offset] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                memory.AsSpan(offset + 2, 2), v.Value);
        }

        // option<u32>: 8 bytes — disc + 3B padding + 4B value.
        private static void WriteOptionU32(byte[] memory,
            int offset, uint? v)
        {
            if (v == null) { memory[offset] = 0; return; }
            memory[offset] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                memory.AsSpan(offset + 4, 4), v.Value);
        }

        // option<u64>: 16 bytes — disc + 7B padding + 8B value.
        private static void WriteOptionU64(byte[] memory,
            int offset, ulong? v)
        {
            if (v == null) { memory[offset] = 0; return; }
            memory[offset] = 1;
            BinaryPrimitives.WriteUInt64LittleEndian(
                memory.AsSpan(offset + 8, 8), v.Value);
        }

        // DnsErrorPayload (16B align 4): rcode @0, info-code @12.
        private static void WriteDnsErrorPayload(byte[] memory,
            int offset, DnsErrorPayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionString(memory, offset, p.Rcode, allocate);
            WriteOptionU16(memory, offset + 12, p.InfoCode);
        }

        // TlsAlertReceivedPayload (16B align 4):
        // alert-id (option<u8>) @0 (2 bytes), padding @2..3,
        // alert-message (option<string>) @4 (12 bytes).
        private static void WriteTlsAlertReceivedPayload(
            byte[] memory, int offset,
            TlsAlertReceivedPayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionU8(memory, offset, p.AlertId);
            // zero pad bytes 2-3
            memory[offset + 2] = 0;
            memory[offset + 3] = 0;
            WriteOptionString(memory, offset + 4,
                p.AlertMessage, allocate);
        }

        // FieldSizePayload (20B align 4): field-name @0,
        // field-size @12.
        private static void WriteFieldSizePayload(byte[] memory,
            int offset, FieldSizePayload p,
            Func<int, int, int> allocate)
        {
            WriteOptionString(memory, offset, p.FieldName, allocate);
            WriteOptionU32(memory, offset + 12, p.FieldSize);
        }

        // option<FieldSizePayload> (24B align 4): disc @0,
        // 3B padding, payload @4 (20 bytes).
        private static void WriteOptionFieldSize(byte[] memory,
            int offset, FieldSizePayload? p,
            Func<int, int, int> allocate)
        {
            if (p == null) { memory[offset] = 0; return; }
            memory[offset] = 1;
            WriteFieldSizePayload(memory, offset + 4, p, allocate);
        }
    }
}
