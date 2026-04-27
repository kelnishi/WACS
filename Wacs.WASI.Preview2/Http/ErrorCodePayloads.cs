// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT
    /// <c>record DNS-error-payload {
    ///   rcode: option&lt;string&gt;,
    ///   info-code: option&lt;u16&gt;,
    /// }</c>. Carried by
    /// <see cref="ErrorCodeDnsError"/>.</summary>
    public sealed class DnsErrorPayload
    {
        public string? Rcode { get; }
        public ushort? InfoCode { get; }

        public DnsErrorPayload(string? rcode, ushort? infoCode)
        {
            Rcode = rcode;
            InfoCode = infoCode;
        }
    }

    /// <summary>WIT
    /// <c>record TLS-alert-received-payload {
    ///   alert-id: option&lt;u8&gt;,
    ///   alert-message: option&lt;string&gt;,
    /// }</c>. Carried by
    /// <see cref="ErrorCodeTlsAlertReceived"/>.</summary>
    public sealed class TlsAlertReceivedPayload
    {
        public byte? AlertId { get; }
        public string? AlertMessage { get; }

        public TlsAlertReceivedPayload(byte? alertId, string? alertMessage)
        {
            AlertId = alertId;
            AlertMessage = alertMessage;
        }
    }

    /// <summary>WIT
    /// <c>record field-size-payload {
    ///   field-name: option&lt;string&gt;,
    ///   field-size: option&lt;u32&gt;,
    /// }</c>. Carried by several HTTP-*-size error-code
    /// cases (HTTP-request-trailer-size, HTTP-response-
    /// header-size, HTTP-response-trailer-size, plus
    /// option-wrapped on HTTP-request-header-size).</summary>
    public sealed class FieldSizePayload
    {
        public string? FieldName { get; }
        public uint? FieldSize { get; }

        public FieldSizePayload(string? fieldName, uint? fieldSize)
        {
            FieldName = fieldName;
            FieldSize = fieldSize;
        }
    }
}
