// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.response-outparam — canonical-ABI flat
        // signature for the static set() method.
        //
        // [static]response-outparam.set(
        //   param: own<response-outparam>,
        //   response: result<own<outgoing-response>, error-code>
        // )
        //
        // Wire (9 slots, joined-flat per canon ABI):
        //   0: paramHandle (i32)
        //   1: resultDisc (i32)
        //   2: rp0 (i32)  — ok-handle OR errDisc
        //   3: rp1 (i32)  — err-content slot 0
        //   4: rp2 (i64)  — err-content slot 1 (joined to i64
        //                   because some error-code cases carry
        //                   option<u64>)
        //   5: rp3 (i32)  — err-content slot 2
        //   6: rp4 (i32)  — err-content slot 3
        //   7: rp5 (i32)  — err-content slot 4
        //   8: rp6 (i32)  — err-content slot 5
        //
        // For Ok, rp0 = response handle, rp1..rp6 zero-padded.
        // For Err, rp0 = errDisc, rp1..rp6 = error-code's 6
        // joined payload slots.
        private static void BindResponseOutparam(WasmRuntime runtime,
            ResourceContext resources)
        {
            var outparams = resources.Table<ResponseOutparam>();
            var responses = resources.Table<OutgoingResponse>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]response-outparam"),
                (_, h) => outparams.Drop(h));

            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, long, int, int, int, int>>(
                (Ns, "[static]response-outparam.set"),
                (ctx, paramHandle, resultDisc,
                    rp0, rp1, rp2, rp3, rp4, rp5, rp6) =>
                {
                    var inst = (ResponseOutparam)outparams.Get(paramHandle);
                    Result<IOutgoingResponse, ErrorCode> result;
                    if (resultDisc == 0)
                    {
                        // Ok: rp0 carries the outgoing-response handle.
                        result = Result<IOutgoingResponse, ErrorCode>
                            .FromOk((OutgoingResponse)responses.Get(rp0));
                    }
                    else
                    {
                        // Err: rp0 = errDisc, rp1..rp6 = err payload.
                        var ec = DecodeErrorCodeFlat(ctx, rp0,
                            rp1, rp2, rp3, rp4, rp5, rp6);
                        result = Result<IOutgoingResponse, ErrorCode>
                            .FromErr(ec);
                    }
                    inst.Set(inst, result);
                });
        }

        // Decode the joined-flat error-code variant from the 6
        // err-content slots that follow the disc.
        //
        // Slot widths in joined form: c0=i32, c1=i64, c2..c5=i32.
        // For cases that don't use the i64 width, c1 is narrowed
        // back to i32 via (int)c1 (canon-ABI lowering zero-
        // extends i32 into i64 slots, so the low 32 bits are
        // the original value).
        private static ErrorCode DecodeErrorCodeFlat(ExecContext ctx,
            int disc, int c0, long c1, int c2, int c3, int c4, int c5)
        {
            switch (disc)
            {
                case 0: return new ErrorCode.ErrorCodeDNSTimeout();
                case 1:
                {
                    // DNS-error(record { rcode: option<string>,
                    //                    info-code: option<u16> })
                    // flatten: [rcode-disc, rcode-ptr, rcode-len,
                    //           info-disc, info-val]
                    var rcode = c0 == 0
                        ? Option<string>.None
                        : Option<string>.Some(
                            ctx.ReadUtf8String((int)c1, c2));
                    var info = c3 == 0
                        ? Option<ushort>.None
                        : Option<ushort>.Some((ushort)c4);
                    return new ErrorCode.ErrorCodeDNSError(
                        new DNSErrorPayload
                        {
                            Rcode = rcode,
                            InfoCode = info,
                        });
                }
                case 2: return new ErrorCode.ErrorCodeDestinationNotFound();
                case 3: return new ErrorCode.ErrorCodeDestinationUnavailable();
                case 4: return new ErrorCode.ErrorCodeDestinationIPProhibited();
                case 5: return new ErrorCode.ErrorCodeDestinationIPUnroutable();
                case 6: return new ErrorCode.ErrorCodeConnectionRefused();
                case 7: return new ErrorCode.ErrorCodeConnectionTerminated();
                case 8: return new ErrorCode.ErrorCodeConnectionTimeout();
                case 9: return new ErrorCode.ErrorCodeConnectionReadTimeout();
                case 10: return new ErrorCode.ErrorCodeConnectionWriteTimeout();
                case 11: return new ErrorCode.ErrorCodeConnectionLimitReached();
                case 12: return new ErrorCode.ErrorCodeTLSProtocolError();
                case 13: return new ErrorCode.ErrorCodeTLSCertificateError();
                case 14:
                {
                    // TLS-alert-received(record { alert-id: option<u8>,
                    //   alert-message: option<string> })
                    // flatten: [alertid-disc, alertid-val,
                    //           alertmsg-disc, alertmsg-ptr, alertmsg-len]
                    var alertId = c0 == 0
                        ? Option<byte>.None
                        : Option<byte>.Some((byte)(int)c1);
                    var alertMsg = c2 == 0
                        ? Option<string>.None
                        : Option<string>.Some(
                            ctx.ReadUtf8String(c3, c4));
                    return new ErrorCode.ErrorCodeTLSAlertReceived(
                        new TLSAlertReceivedPayload
                        {
                            AlertId = alertId,
                            AlertMessage = alertMsg,
                        });
                }
                case 15: return new ErrorCode.ErrorCodeHTTPRequestDenied();
                case 16: return new ErrorCode.ErrorCodeHTTPRequestLengthRequired();
                case 17:
                {
                    // option<u64>: [opt-disc, val-i64]
                    var v = c0 == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some((ulong)c1);
                    return new ErrorCode.ErrorCodeHTTPRequestBodySize(v);
                }
                case 18: return new ErrorCode.ErrorCodeHTTPRequestMethodInvalid();
                case 19: return new ErrorCode.ErrorCodeHTTPRequestURIInvalid();
                case 20: return new ErrorCode.ErrorCodeHTTPRequestURITooLong();
                case 21:
                {
                    var v = c0 == 0
                        ? Option<uint>.None
                        : Option<uint>.Some((uint)(int)c1);
                    return new ErrorCode.ErrorCodeHTTPRequestHeaderSectionSize(v);
                }
                case 22:
                {
                    // option<field-size-payload>: 1 (opt-disc)
                    // + 5 (record flatten) = 6 slots.
                    if (c0 == 0)
                        return new ErrorCode.ErrorCodeHTTPRequestHeaderSize(
                            Option<FieldSizePayload>.None);
                    var rec = DecodeFieldSizePayloadFlat(ctx,
                        (int)c1, c2, c3, c4, c5);
                    return new ErrorCode.ErrorCodeHTTPRequestHeaderSize(
                        Option<FieldSizePayload>.Some(rec));
                }
                case 23:
                {
                    var v = c0 == 0
                        ? Option<uint>.None
                        : Option<uint>.Some((uint)(int)c1);
                    return new ErrorCode.ErrorCodeHTTPRequestTrailerSectionSize(v);
                }
                case 24:
                {
                    // field-size-payload directly: 5 slots.
                    var rec = DecodeFieldSizePayloadFlat(ctx,
                        c0, (int)c1, c2, c3, c4);
                    return new ErrorCode.ErrorCodeHTTPRequestTrailerSize(rec);
                }
                case 25: return new ErrorCode.ErrorCodeHTTPResponseIncomplete();
                case 26:
                {
                    var v = c0 == 0
                        ? Option<uint>.None
                        : Option<uint>.Some((uint)(int)c1);
                    return new ErrorCode.ErrorCodeHTTPResponseHeaderSectionSize(v);
                }
                case 27:
                {
                    var rec = DecodeFieldSizePayloadFlat(ctx,
                        c0, (int)c1, c2, c3, c4);
                    return new ErrorCode.ErrorCodeHTTPResponseHeaderSize(rec);
                }
                case 28:
                {
                    var v = c0 == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some((ulong)c1);
                    return new ErrorCode.ErrorCodeHTTPResponseBodySize(v);
                }
                case 29:
                {
                    var v = c0 == 0
                        ? Option<uint>.None
                        : Option<uint>.Some((uint)(int)c1);
                    return new ErrorCode.ErrorCodeHTTPResponseTrailerSectionSize(v);
                }
                case 30:
                {
                    var rec = DecodeFieldSizePayloadFlat(ctx,
                        c0, (int)c1, c2, c3, c4);
                    return new ErrorCode.ErrorCodeHTTPResponseTrailerSize(rec);
                }
                case 31:
                {
                    // option<string>: [opt-disc, ptr, len]
                    var v = c0 == 0
                        ? Option<string>.None
                        : Option<string>.Some(
                            ctx.ReadUtf8String((int)c1, c2));
                    return new ErrorCode.ErrorCodeHTTPResponseTransferCoding(v);
                }
                case 32:
                {
                    var v = c0 == 0
                        ? Option<string>.None
                        : Option<string>.Some(
                            ctx.ReadUtf8String((int)c1, c2));
                    return new ErrorCode.ErrorCodeHTTPResponseContentCoding(v);
                }
                case 33: return new ErrorCode.ErrorCodeHTTPResponseTimeout();
                case 34: return new ErrorCode.ErrorCodeHTTPUpstreamResponseTimeout();
                case 35: return new ErrorCode.ErrorCodeHTTPProtocolError();
                case 36: return new ErrorCode.ErrorCodeLoopDetected();
                case 37: return new ErrorCode.ErrorCodeConfigurationError();
                case 38:
                {
                    var v = c0 == 0
                        ? Option<string>.None
                        : Option<string>.Some(
                            ctx.ReadUtf8String((int)c1, c2));
                    return new ErrorCode.ErrorCodeInternalError(v);
                }
                default:
                    throw new ArgumentException(
                        "Unknown error-code variant disc: " + disc);
            }
        }

        // record field-size-payload {
        //   field-name: option<string>,
        //   field-size: option<u32>,
        // }
        // flatten: 5 slots [fname-disc, fname-ptr, fname-len,
        //                   fsize-disc, fsize-val]
        private static FieldSizePayload DecodeFieldSizePayloadFlat(
            ExecContext ctx,
            int fnameDisc, int fnamePtr, int fnameLen,
            int fsizeDisc, int fsizeVal)
        {
            var fname = fnameDisc == 0
                ? Option<string>.None
                : Option<string>.Some(
                    ctx.ReadUtf8String(fnamePtr, fnameLen));
            var fsize = fsizeDisc == 0
                ? Option<uint>.None
                : Option<uint>.Some((uint)fsizeVal);
            return new FieldSizePayload
            {
                FieldName = fname,
                FieldSize = fsize,
            };
        }
    }
}
