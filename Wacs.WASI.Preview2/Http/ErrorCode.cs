// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.error-code</c> — the
    /// 39-case variant returned on the Err side of HTTP
    /// dispatch / future.get / setter operations. Each case
    /// is a sealed subclass; payload-bearing cases carry
    /// their data as fields. The byte
    /// <see cref="Discriminant"/> matches the case index in
    /// the spec WIT (0 = DNS-timeout, 38 = internal-error).
    ///
    /// <para>The encoder writes:
    /// <code>
    /// align = 8 (option<u64> in HTTP-request-body-size /
    ///            HTTP-response-body-size raises max_align)
    /// disc bytes = 1 (u8 disc, since 39 cases &lt; 256)
    /// payload area = 24 bytes (max payload =
    ///                          option&lt;field-size-payload&gt;)
    /// total size = 8 + 24 = 32 bytes
    /// </code>
    /// Payload for each case starts at offset
    /// <c>max_align = 8</c> within the variant.</para>
    /// </summary>
    public abstract class ErrorCode
    {
        /// <summary>Discriminant byte matching the WIT case
        /// index (u8 since case count &lt; 256).</summary>
        public abstract byte Discriminant { get; }

        private protected ErrorCode() { }
    }

    public sealed class ErrorCodeDnsTimeout : ErrorCode
    { public override byte Discriminant => 0; }

    public sealed class ErrorCodeDnsError : ErrorCode
    {
        public DnsErrorPayload Payload { get; }
        public ErrorCodeDnsError(DnsErrorPayload payload)
        {
            Payload = payload;
        }
        public override byte Discriminant => 1;
    }

    public sealed class ErrorCodeDestinationNotFound : ErrorCode
    { public override byte Discriminant => 2; }

    public sealed class ErrorCodeDestinationUnavailable : ErrorCode
    { public override byte Discriminant => 3; }

    public sealed class ErrorCodeDestinationIpProhibited : ErrorCode
    { public override byte Discriminant => 4; }

    public sealed class ErrorCodeDestinationIpUnroutable : ErrorCode
    { public override byte Discriminant => 5; }

    public sealed class ErrorCodeConnectionRefused : ErrorCode
    { public override byte Discriminant => 6; }

    public sealed class ErrorCodeConnectionTerminated : ErrorCode
    { public override byte Discriminant => 7; }

    public sealed class ErrorCodeConnectionTimeout : ErrorCode
    { public override byte Discriminant => 8; }

    public sealed class ErrorCodeConnectionReadTimeout : ErrorCode
    { public override byte Discriminant => 9; }

    public sealed class ErrorCodeConnectionWriteTimeout : ErrorCode
    { public override byte Discriminant => 10; }

    public sealed class ErrorCodeConnectionLimitReached : ErrorCode
    { public override byte Discriminant => 11; }

    public sealed class ErrorCodeTlsProtocolError : ErrorCode
    { public override byte Discriminant => 12; }

    public sealed class ErrorCodeTlsCertificateError : ErrorCode
    { public override byte Discriminant => 13; }

    public sealed class ErrorCodeTlsAlertReceived : ErrorCode
    {
        public TlsAlertReceivedPayload Payload { get; }
        public ErrorCodeTlsAlertReceived(TlsAlertReceivedPayload payload)
        {
            Payload = payload;
        }
        public override byte Discriminant => 14;
    }

    public sealed class ErrorCodeHttpRequestDenied : ErrorCode
    { public override byte Discriminant => 15; }

    public sealed class ErrorCodeHttpRequestLengthRequired : ErrorCode
    { public override byte Discriminant => 16; }

    /// <summary><c>HTTP-request-body-size(option&lt;u64&gt;)</c></summary>
    public sealed class ErrorCodeHttpRequestBodySize : ErrorCode
    {
        public ulong? Size { get; }
        public ErrorCodeHttpRequestBodySize(ulong? size) { Size = size; }
        public override byte Discriminant => 17;
    }

    public sealed class ErrorCodeHttpRequestMethodInvalid : ErrorCode
    { public override byte Discriminant => 18; }

    public sealed class ErrorCodeHttpRequestUriInvalid : ErrorCode
    { public override byte Discriminant => 19; }

    public sealed class ErrorCodeHttpRequestUriTooLong : ErrorCode
    { public override byte Discriminant => 20; }

    /// <summary><c>HTTP-request-header-section-size(option&lt;u32&gt;)</c></summary>
    public sealed class ErrorCodeHttpRequestHeaderSectionSize : ErrorCode
    {
        public uint? Size { get; }
        public ErrorCodeHttpRequestHeaderSectionSize(uint? size) { Size = size; }
        public override byte Discriminant => 21;
    }

    /// <summary><c>HTTP-request-header-size(option&lt;field-size-payload&gt;)</c></summary>
    public sealed class ErrorCodeHttpRequestHeaderSize : ErrorCode
    {
        public FieldSizePayload? Field { get; }
        public ErrorCodeHttpRequestHeaderSize(FieldSizePayload? field)
        {
            Field = field;
        }
        public override byte Discriminant => 22;
    }

    /// <summary><c>HTTP-request-trailer-section-size(option&lt;u32&gt;)</c></summary>
    public sealed class ErrorCodeHttpRequestTrailerSectionSize : ErrorCode
    {
        public uint? Size { get; }
        public ErrorCodeHttpRequestTrailerSectionSize(uint? size) { Size = size; }
        public override byte Discriminant => 23;
    }

    /// <summary><c>HTTP-request-trailer-size(field-size-payload)</c></summary>
    public sealed class ErrorCodeHttpRequestTrailerSize : ErrorCode
    {
        public FieldSizePayload Field { get; }
        public ErrorCodeHttpRequestTrailerSize(FieldSizePayload field)
        {
            Field = field;
        }
        public override byte Discriminant => 24;
    }

    public sealed class ErrorCodeHttpResponseIncomplete : ErrorCode
    { public override byte Discriminant => 25; }

    /// <summary><c>HTTP-response-header-section-size(option&lt;u32&gt;)</c></summary>
    public sealed class ErrorCodeHttpResponseHeaderSectionSize : ErrorCode
    {
        public uint? Size { get; }
        public ErrorCodeHttpResponseHeaderSectionSize(uint? size) { Size = size; }
        public override byte Discriminant => 26;
    }

    /// <summary><c>HTTP-response-header-size(field-size-payload)</c></summary>
    public sealed class ErrorCodeHttpResponseHeaderSize : ErrorCode
    {
        public FieldSizePayload Field { get; }
        public ErrorCodeHttpResponseHeaderSize(FieldSizePayload field)
        {
            Field = field;
        }
        public override byte Discriminant => 27;
    }

    /// <summary><c>HTTP-response-body-size(option&lt;u64&gt;)</c></summary>
    public sealed class ErrorCodeHttpResponseBodySize : ErrorCode
    {
        public ulong? Size { get; }
        public ErrorCodeHttpResponseBodySize(ulong? size) { Size = size; }
        public override byte Discriminant => 28;
    }

    /// <summary><c>HTTP-response-trailer-section-size(option&lt;u32&gt;)</c></summary>
    public sealed class ErrorCodeHttpResponseTrailerSectionSize : ErrorCode
    {
        public uint? Size { get; }
        public ErrorCodeHttpResponseTrailerSectionSize(uint? size) { Size = size; }
        public override byte Discriminant => 29;
    }

    /// <summary><c>HTTP-response-trailer-size(field-size-payload)</c></summary>
    public sealed class ErrorCodeHttpResponseTrailerSize : ErrorCode
    {
        public FieldSizePayload Field { get; }
        public ErrorCodeHttpResponseTrailerSize(FieldSizePayload field)
        {
            Field = field;
        }
        public override byte Discriminant => 30;
    }

    /// <summary><c>HTTP-response-transfer-coding(option&lt;string&gt;)</c></summary>
    public sealed class ErrorCodeHttpResponseTransferCoding : ErrorCode
    {
        public string? Coding { get; }
        public ErrorCodeHttpResponseTransferCoding(string? coding) { Coding = coding; }
        public override byte Discriminant => 31;
    }

    /// <summary><c>HTTP-response-content-coding(option&lt;string&gt;)</c></summary>
    public sealed class ErrorCodeHttpResponseContentCoding : ErrorCode
    {
        public string? Coding { get; }
        public ErrorCodeHttpResponseContentCoding(string? coding) { Coding = coding; }
        public override byte Discriminant => 32;
    }

    public sealed class ErrorCodeHttpResponseTimeout : ErrorCode
    { public override byte Discriminant => 33; }

    public sealed class ErrorCodeHttpUpstreamResponseTimeout : ErrorCode
    { public override byte Discriminant => 34; }

    public sealed class ErrorCodeHttpProtocolError : ErrorCode
    { public override byte Discriminant => 35; }

    public sealed class ErrorCodeLoopDetected : ErrorCode
    { public override byte Discriminant => 36; }

    public sealed class ErrorCodeConfigurationError : ErrorCode
    { public override byte Discriminant => 37; }

    /// <summary><c>internal-error(option&lt;string&gt;)</c> —
    /// catch-all for unstructured failure descriptions.</summary>
    public sealed class ErrorCodeInternalError : ErrorCode
    {
        public string? Description { get; }
        public ErrorCodeInternalError(string? description)
        {
            Description = description;
        }
        public override byte Discriminant => 38;
    }
}
