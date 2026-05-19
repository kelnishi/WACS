// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// WIT <c>variant method</c>. Tag-only cases for the
    /// standard HTTP methods plus an <c>other(string)</c>
    /// catch-all for extension methods.
    /// </summary>
    public readonly struct HttpMethod : IEquatable<HttpMethod>
    {
        public enum Tag
        {
            Get, Head, Post, Put, Delete,
            Connect, Options, Trace, Patch, Other,
        }

        public Tag Kind { get; }
        public string OtherName { get; }

        private HttpMethod(Tag kind, string otherName)
        {
            Kind = kind;
            OtherName = otherName ?? string.Empty;
        }

        public static HttpMethod Get => new HttpMethod(Tag.Get, string.Empty);
        public static HttpMethod Head => new HttpMethod(Tag.Head, string.Empty);
        public static HttpMethod Post => new HttpMethod(Tag.Post, string.Empty);
        public static HttpMethod Put => new HttpMethod(Tag.Put, string.Empty);
        public static HttpMethod Delete => new HttpMethod(Tag.Delete, string.Empty);
        public static HttpMethod Connect => new HttpMethod(Tag.Connect, string.Empty);
        public static HttpMethod Options_ => new HttpMethod(Tag.Options, string.Empty);
        public static HttpMethod Trace => new HttpMethod(Tag.Trace, string.Empty);
        public static HttpMethod Patch => new HttpMethod(Tag.Patch, string.Empty);
        public static HttpMethod Other(string name) =>
            new HttpMethod(Tag.Other, name ?? string.Empty);

        public bool Equals(HttpMethod other) =>
            Kind == other.Kind &&
            (Kind != Tag.Other
                || string.Equals(OtherName, other.OtherName, StringComparison.Ordinal));
        public override bool Equals(object? obj) =>
            obj is HttpMethod m && Equals(m);
        public override int GetHashCode() =>
            Kind == Tag.Other
                ? OtherName.GetHashCode()
                : (int)Kind;
        public override string ToString() =>
            Kind == Tag.Other ? OtherName : Kind.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// WIT <c>variant scheme { HTTP, HTTPS, other(string) }</c>.
    /// </summary>
    public readonly struct HttpScheme : IEquatable<HttpScheme>
    {
        public enum Tag { Http, Https, Other }
        public Tag Kind { get; }
        public string OtherName { get; }

        private HttpScheme(Tag kind, string otherName)
        {
            Kind = kind;
            OtherName = otherName ?? string.Empty;
        }

        public static HttpScheme Http => new HttpScheme(Tag.Http, string.Empty);
        public static HttpScheme Https => new HttpScheme(Tag.Https, string.Empty);
        public static HttpScheme Other(string name) =>
            new HttpScheme(Tag.Other, name ?? string.Empty);

        public bool Equals(HttpScheme other) =>
            Kind == other.Kind &&
            (Kind != Tag.Other
                || string.Equals(OtherName, other.OtherName, StringComparison.Ordinal));
        public override bool Equals(object? obj) =>
            obj is HttpScheme s && Equals(s);
        public override int GetHashCode() =>
            Kind == Tag.Other ? OtherName.GetHashCode() : (int)Kind;
        public override string ToString() =>
            Kind == Tag.Http ? "http"
            : Kind == Tag.Https ? "https"
            : OtherName;
    }

    /// <summary>
    /// WIT <c>variant error-code</c>. Most cases are tag-only;
    /// a handful carry typed payloads (DNS rcode + info-code,
    /// TLS alert id + message, optional sizes). The payloads
    /// are exposed via <see cref="HttpException"/> properties
    /// rather than embedded in the enum so the per-case
    /// payload-shape diversity stays out of the discriminator.
    /// </summary>
    public enum HttpErrorCode
    {
        DnsTimeout,
        DnsError,
        DestinationNotFound,
        DestinationUnavailable,
        DestinationIpProhibited,
        DestinationIpUnroutable,
        ConnectionRefused,
        ConnectionTerminated,
        ConnectionTimeout,
        ConnectionReadTimeout,
        ConnectionWriteTimeout,
        ConnectionLimitReached,
        TlsProtocolError,
        TlsCertificateError,
        TlsAlertReceived,
        HttpRequestDenied,
        HttpRequestLengthRequired,
        HttpRequestBodySize,
        HttpRequestMethodInvalid,
        HttpRequestUriInvalid,
        HttpRequestUriTooLong,
        HttpRequestHeaderSectionSize,
        HttpRequestHeaderSize,
        HttpRequestTrailerSectionSize,
        HttpRequestTrailerSize,
        HttpResponseIncomplete,
        HttpResponseHeaderSectionSize,
        HttpResponseHeaderSize,
        HttpResponseBodySize,
        HttpResponseTrailerSectionSize,
        HttpResponseTrailerSize,
        HttpResponseTransferCoding,
        HttpResponseContentCoding,
        HttpResponseTimeout,
        HttpUpgradeFailed,
        HttpProtocolError,
        LoopDetected,
        ConfigurationError,
        InternalError,
    }

    /// <summary>
    /// WIT <c>variant header-error</c>.
    /// </summary>
    public enum HeaderError
    {
        InvalidSyntax,
        Forbidden,
        Immutable,
        SizeExceeded,
        Other,
    }

    /// <summary>
    /// WIT <c>variant request-options-error</c>.
    /// </summary>
    public enum RequestOptionsError
    {
        NotSupported,
        Immutable,
        Other,
    }

    /// <summary>
    /// Host-side exception that carries an
    /// <see cref="HttpErrorCode"/> + the typed payloads for the
    /// few cases that have them. Method bodies throw these for
    /// the canon-async binding to lower into
    /// <c>result&lt;_, error-code&gt;</c> err values.
    /// </summary>
    public sealed class HttpException : Exception
    {
        public HttpErrorCode Code { get; }

        /// <summary>For <see cref="HttpErrorCode.DnsError"/>:
        /// the DNS rcode if available.</summary>
        public string? DnsRcode { get; init; }
        /// <summary>For <see cref="HttpErrorCode.DnsError"/>:
        /// the DNS info-code if available.</summary>
        public ushort? DnsInfoCode { get; init; }
        /// <summary>For <see cref="HttpErrorCode.TlsAlertReceived"/>.</summary>
        public byte? TlsAlertId { get; init; }
        /// <summary>For <see cref="HttpErrorCode.TlsAlertReceived"/>.</summary>
        public string? TlsAlertMessage { get; init; }
        /// <summary>For body-size cases.</summary>
        public ulong? BodySize { get; init; }
        /// <summary>For header/trailer section-size cases.</summary>
        public uint? SectionSize { get; init; }
        /// <summary>For per-field size cases.</summary>
        public string? FieldName { get; init; }
        /// <summary>For per-field size cases.</summary>
        public uint? FieldSize { get; init; }
        /// <summary>For coding cases + InternalError.</summary>
        public string? OtherPayload { get; init; }

        public HttpException(HttpErrorCode code, string? message = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            if (code == HttpErrorCode.InternalError)
                OtherPayload = message;
        }
    }

    /// <summary>
    /// Host-side exception for fields-related errors.
    /// </summary>
    public sealed class HeaderException : Exception
    {
        public HeaderError Code { get; }
        public string? OtherPayload { get; }

        public HeaderException(HeaderError code, string? message = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            OtherPayload = code == HeaderError.Other ? message : null;
        }
    }

    /// <summary>
    /// Host-side exception for request-options errors.
    /// </summary>
    public sealed class RequestOptionsException : Exception
    {
        public RequestOptionsError Code { get; }
        public string? OtherPayload { get; }

        public RequestOptionsException(
            RequestOptionsError code, string? message = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            OtherPayload = code == RequestOptionsError.Other ? message : null;
        }
    }
}
