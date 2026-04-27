// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Tests for wasi:http surface. v0 covers the resource
    /// markers + a few simple methods on Fields. The full
    /// interface (request/response lifecycle, body streams,
    /// outgoing-handler) lands incrementally as binder shapes
    /// fill in.
    /// </summary>
    public class HttpTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        private sealed class TrackingFields : Fields
        {
            public string? LastDeleted;
            public override void Delete(string name)
            {
                LastDeleted = name;
                base.Delete(name);
            }
        }

        [Fact]
        public void Fields_resource_binds_and_delete_threads_string_param()
        {
            // Fixture: ask-delete(handle) calls
            // fields.delete(handle, "X-Custom") and returns
            // the outer disc byte (always-Ok = 0). Stub
            // captures the deleted name; test asserts both
            // the return + the captured name.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new TrackingFields();
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-delete", (uint)handle)!);
            Assert.Equal("X-Custom", fields.LastDeleted);
        }

        [Fact]
        public void Fields_has_decodes_string_param_on_primitive_resource_method()
        {
            // Fixture: ask-has(handle) calls fields.has(handle,
            // "X-Present"). Stub fields starts with one entry
            // ("X-Present", "v") via AppendEntry; expect
            // has() == 1.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            fields.AppendEntry("X-Present", new byte[] { (byte)'v' });
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-has", (uint)handle)!);
        }

        [Fact]
        public void Fields_append_decodes_string_and_byte_array_params()
        {
            // Fixture: ask-append(handle) calls fields.append
            // (handle, "X-New", "value"). Stub captures both
            // via override of Append; test asserts the entry
            // landed in the field collection.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-append", (uint)handle)!);
            Assert.Single(fields.Entries);
            Assert.Equal("X-New", fields.Entries[0].Key);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("value"),
                fields.Entries[0].Value);
        }

        [Fact]
        public void Fields_entries_returns_list_of_string_byte_array_pairs()
        {
            // Stub Fields seeded with two entries:
            //   "X-Foo" / "bar"
            //   "X-Baz" / "quux"
            // ask-entries-len returns 2, ask-entries-first-key
            // returns 'X' (first byte of "X-Foo"),
            // ask-entries-first-val returns 'b' (first byte
            // of "bar").
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            fields.AppendEntry("X-Foo",
                System.Text.Encoding.UTF8.GetBytes("bar"));
            fields.AppendEntry("X-Baz",
                System.Text.Encoding.UTF8.GetBytes("quux"));
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-entries-len", (uint)handle)!);
            Assert.Equal((uint)'X', (uint)ci.Invoke(
                "ask-entries-first-key", (uint)handle)!);
            Assert.Equal((uint)'b', (uint)ci.Invoke(
                "ask-entries-first-val", (uint)handle)!);
        }

        [Fact]
        public void Fields_get_returns_list_of_byte_arrays_for_matching_key()
        {
            // Stub seeded with two values for "X-Foo":
            //   "bar" and "baz" (with an unrelated entry in
            //   between). ask-get-len returns 2, ask-get-
            //   first-byte returns 'b' (first byte of "bar").
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            fields.AppendEntry("X-Foo",
                System.Text.Encoding.UTF8.GetBytes("bar"));
            fields.AppendEntry("X-Other",
                System.Text.Encoding.UTF8.GetBytes("zzz"));
            fields.AppendEntry("X-Foo",
                System.Text.Encoding.UTF8.GetBytes("baz"));
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-get-len", (uint)handle)!);
            Assert.Equal((uint)'b', (uint)ci.Invoke(
                "ask-get-first-byte", (uint)handle)!);
        }

        [Fact]
        public void Fields_set_decodes_string_and_byte_array_list_params()
        {
            // Fixture: ask-set(handle) calls fields.set(handle,
            // "X-Set", ["one", "two"]). Stub starts with one
            // existing X-Set entry; set replaces it with the
            // two new values.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            fields.AppendEntry("X-Set",
                System.Text.Encoding.UTF8.GetBytes("OLD"));
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set", (uint)handle)!);
            // After set: 2 entries, both keyed "X-Set", with
            // values "one" and "two". The original "OLD"
            // entry was wiped.
            Assert.Equal(2, fields.Entries.Count);
            Assert.Equal("X-Set", fields.Entries[0].Key);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("one"),
                fields.Entries[0].Value);
            Assert.Equal("X-Set", fields.Entries[1].Key);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("two"),
                fields.Entries[1].Value);
        }

        [Fact]
        public void Fields_clone_returns_fresh_resource_handle()
        {
            // Fixture: ask-clone(handle) calls fields.clone,
            // drops the returned handle, returns 1 if non-zero.
            // Validates the resource-return-resource path on
            // an http resource.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-component", "httpfields.component.wasm"));
            var resources = new ResourceContext();
            var fields = new Fields();
            int handle = resources.TableFor(typeof(Fields))
                .Allocate(fields);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-clone", (uint)handle)!);
            // Original Fields handle still in table; cloned
            // one was dropped by the guest.
            Assert.Equal(1, resources.TableFor(typeof(Fields)).Count);
        }

        private sealed class MethodOnlyRequest : OutgoingRequest
        {
            public MethodOnlyRequest(HttpMethod method)
            {
                _method = method;
            }
        }

        [Fact]
        public void OutgoingRequest_method_returns_named_variant_case()
        {
            // Stub returns POST. Variant disc 2 = post.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-method-component", "httpmethod.component.wasm"));
            var resources = new ResourceContext();
            var req = new MethodOnlyRequest(new HttpMethodPost());
            int handle = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-disc", (uint)handle)!);
        }

        [Fact]
        public void OutgoingRequest_method_returns_other_string_payload()
        {
            // Stub returns Other("PURGE"). Variant disc 9 +
            // string payload at +4/+8 holds "PURGE";
            // ask-other-first yields 'P' = 0x50.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-method-component", "httpmethod.component.wasm"));
            var resources = new ResourceContext();
            var req = new MethodOnlyRequest(new HttpMethodOther("PURGE"));
            int handle = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(9u, (uint)ci.Invoke(
                "ask-disc", (uint)handle)!);
            Assert.Equal((uint)'P', (uint)ci.Invoke(
                "ask-other-first", (uint)handle)!);
        }

        private sealed class TimeoutOptions : RequestOptions
        {
            public TimeoutOptions(ulong? connectTimeout)
            {
                _connectTimeout = connectTimeout;
            }
        }

        [Fact]
        public void RequestOptions_connect_timeout_returns_option_u64_some()
        {
            // Stub returns Some(5_000_000_000) — 5 second
            // timeout in nanoseconds. Fixture reads (disc,
            // u64 payload) at retArea+0/+8.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-reqopts-component", "httpreqopts.component.wasm"));
            var resources = new ResourceContext();
            var opts = new TimeoutOptions(5_000_000_000UL);
            int handle = resources.TableFor(typeof(RequestOptions))
                .Allocate(opts);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // Some
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-disc", (uint)handle)!);
            Assert.Equal(5_000_000_000UL, (ulong)ci.Invoke(
                "ask-timeout", (uint)handle)!);
        }

        [Fact]
        public void RequestOptions_connect_timeout_returns_option_u64_none()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-reqopts-component", "httpreqopts.component.wasm"));
            var resources = new ResourceContext();
            var opts = new TimeoutOptions(null);
            int handle = resources.TableFor(typeof(RequestOptions))
                .Allocate(opts);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // None disc=0
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-disc", (uint)handle)!);
        }

        private sealed class ConfiguredOutgoingRequest : OutgoingRequest
        {
            public ConfiguredOutgoingRequest(string? path, string? authority)
            {
                _pathWithQuery = path;
                _authority = authority;
            }
        }

        [Fact]
        public void OutgoingRequest_path_with_query_returns_option_string_some()
        {
            // Stub returns "/abc" for path; ask-path packs
            // (option-disc=1, 'a'=0x61) → 0x6101.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-request-component", "httprequest.component.wasm"));
            var resources = new ResourceContext();
            var req = new ConfiguredOutgoingRequest(
                "/abc", "host.example:8080");
            int handle = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // path: Some("/abc") → disc=1 + first byte '/'=0x2F
            Assert.Equal(0x2F01u, (uint)ci.Invoke(
                "ask-path", (uint)handle)!);
            // authority: Some("host.example:8080") →
            //   disc=1 + 'h'=0x68
            Assert.Equal(0x6801u, (uint)ci.Invoke(
                "ask-authority", (uint)handle)!);
        }

        [Fact]
        public void OutgoingRequest_path_with_query_returns_option_string_none()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-request-component", "httprequest.component.wasm"));
            var resources = new ResourceContext();
            var req = new ConfiguredOutgoingRequest(null, null);
            int handle = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // None disc=0 + payload byte stays 0 → 0.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-path", (uint)handle)!);
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-authority", (uint)handle)!);
        }

        private sealed class TeapotResponse : IncomingResponse
        {
            public override ushort Status() => 418;   // I'm a teapot
        }

        [Fact]
        public void IncomingResponse_status_returns_u16()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-response-component", "httpresponse.component.wasm"));
            var resources = new ResourceContext();
            var resp = new TeapotResponse();
            int handle = resources.TableFor(typeof(IncomingResponse))
                .Allocate(resp);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(418u, (uint)ci.Invoke(
                "ask-status", (uint)handle)!);
        }

        [Fact]
        public void OutgoingResponse_set_status_code_round_trips()
        {
            // Fixture: ask-set-status(handle) calls
            // outgoing-response.set-status-code(handle, 404)
            // then status-code(handle), returning the new
            // value. Validates u16 param + result wrap +
            // u16 return on the same instance.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-response-component", "httpresponse.component.wasm"));
            var resources = new ResourceContext();
            var resp = new OutgoingResponse();
            int handle = resources.TableFor(typeof(OutgoingResponse))
                .Allocate(resp);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(404u, (uint)ci.Invoke(
                "ask-set-status", (uint)handle)!);
            Assert.Equal((ushort)404, resp.StatusCode());
        }

        [Fact]
        public void IncomingResponse_headers_yields_fields_handle()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-response-component", "httpresponse.component.wasm"));
            var resources = new ResourceContext();
            var resp = new IncomingResponse();
            int handle = resources.TableFor(typeof(IncomingResponse))
                .Allocate(resp);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-headers", (uint)handle)!);
            // Returned Fields handle was dropped by the
            // guest; only the original IncomingResponse
            // handle remains.
            Assert.Equal(0, resources.TableFor(typeof(Fields)).Count);
        }

        [Fact]
        public void OutgoingRequest_body_yields_outgoing_body_then_output_stream()
        {
            // Fixture: ask-write-chain(handle) calls
            //   outgoing-request.body(handle) → outgoing-body
            //   outgoing-body.write() → output-stream
            // Drops both. Returns the outer-disc byte from
            // body() (always-Ok = 0). Verifies the chain of
            // resource-on-result calls hand back fresh
            // resource handles each step.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-body-component", "httpbody.component.wasm"));
            var resources = new ResourceContext();
            var req = new OutgoingRequest();
            int handle = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
                new StreamBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-write-chain", (uint)handle)!);
            // Both body + stream were dropped by the guest;
            // tables empty modulo the original request.
            Assert.Equal(0, resources.TableFor(
                typeof(OutgoingBody)).Count);
            Assert.Equal(0, resources.TableFor(
                typeof(OutputStream)).Count);
        }

        [Fact]
        public void IncomingResponse_consume_yields_incoming_body_then_input_stream()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-body-component", "httpbody.component.wasm"));
            var resources = new ResourceContext();
            var resp = new IncomingResponse();
            int handle = resources.TableFor(typeof(IncomingResponse))
                .Allocate(resp);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
                new StreamBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-read-chain", (uint)handle)!);
            Assert.Equal(0, resources.TableFor(
                typeof(IncomingBody)).Count);
            Assert.Equal(0, resources.TableFor(
                typeof(InputStream)).Count);
        }

        private sealed class CapturingHandler : IOutgoingHandler
        {
            public OutgoingRequest? CapturedRequest;
            public RequestOptions? CapturedOptions;

            [WasiErrorResult]
            public FutureIncomingResponse Handle(
                OutgoingRequest request,
                [WasiOptionalParam] RequestOptions? options)
            {
                CapturedRequest = request;
                CapturedOptions = options;
                return new FutureIncomingResponse();
            }
        }

        [Fact]
        public void OutgoingHandler_handle_with_no_options()
        {
            // Fixture: ask-handle-none(req) calls handle(req,
            // None) — passes option-disc=0 + handle=0 for
            // the option<own<request-options>> param. Stub
            // captures the resolved request + options (null).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-handler-component", "httphandler.component.wasm"));
            var resources = new ResourceContext();
            var handler = new CapturingHandler();
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:http/outgoing-handler@0.2.3",
                    handler, resources);
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-handle-none", (uint)hReq)!);
            Assert.Same(req, handler.CapturedRequest);
            Assert.Null(handler.CapturedOptions);
        }

        [Fact]
        public void OutgoingHandler_handle_with_some_options()
        {
            // Same fixture, ask-handle-some(req, opts) →
            // handle(req, Some(opts)) — passes option-disc=1
            // + opts handle. Stub captures both resolved
            // resources.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-handler-component", "httphandler.component.wasm"));
            var resources = new ResourceContext();
            var handler = new CapturingHandler();
            var req = new OutgoingRequest();
            var opts = new RequestOptions();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);
            int hOpts = resources.TableFor(typeof(RequestOptions))
                .Allocate(opts);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:http/outgoing-handler@0.2.3",
                    handler, resources);
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-handle-some", (uint)hReq, (uint)hOpts)!);
            Assert.Same(req, handler.CapturedRequest);
            Assert.Same(opts, handler.CapturedOptions);
        }

        private sealed class CapturingOutgoingBody : OutgoingBody
        {
            public bool FinishCalled;
            public Fields? CapturedTrailers;

            public override void Finish(Fields? trailers)
            {
                FinishCalled = true;
                CapturedTrailers = trailers;
            }
        }

        [Fact]
        public void OutgoingBody_finish_with_no_trailers()
        {
            // Fixture: ask-finish-none(body) calls
            // [static]outgoing-body.finish(body, None) —
            // option disc=0, handle=0 for the trailers
            // param. Stub records the resolved trailers
            // (null) and returns Ok.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-bodyfinish-component",
                "httpbodyfinish.component.wasm"));
            var resources = new ResourceContext();
            var body = new CapturingOutgoingBody();
            int hBody = resources.TableFor(typeof(OutgoingBody))
                .Allocate(body);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-finish-none", (uint)hBody)!);
            Assert.True(body.FinishCalled);
            Assert.Null(body.CapturedTrailers);
        }

        [Fact]
        public void OutgoingBody_finish_with_trailers()
        {
            // Same fixture, ask-finish-some(body, trailers)
            // → finish(body, Some(trailers)). Option disc=1
            // + a real fields handle. Stub captures the
            // resolved Fields object.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-bodyfinish-component",
                "httpbodyfinish.component.wasm"));
            var resources = new ResourceContext();
            var body = new CapturingOutgoingBody();
            var trailers = new Fields();
            int hBody = resources.TableFor(typeof(OutgoingBody))
                .Allocate(body);
            int hTrailers = resources.TableFor(typeof(Fields))
                .Allocate(trailers);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-finish-some", (uint)hBody, (uint)hTrailers)!);
            Assert.True(body.FinishCalled);
            Assert.Same(trailers, body.CapturedTrailers);
        }

        private sealed class TrackingIncomingBody : IncomingBody
        {
            public bool FinishCalled;
            public FutureTrailers Trailers = new FutureTrailers();

            public override FutureTrailers Finish()
            {
                FinishCalled = true;
                return Trailers;
            }
        }

        [Fact]
        public void IncomingBody_finish_yields_future_trailers_handle()
        {
            // Fixture: ask-finish(body) calls
            // [static]incoming-body.finish(body) and returns
            // the future-trailers handle. Stub returns a
            // pinned FutureTrailers instance so the test can
            // assert table-allocation succeeded and the
            // returned handle resolves back to it.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-incomingbody-finish-component",
                "httpincomingfinish.component.wasm"));
            var resources = new ResourceContext();
            var body = new TrackingIncomingBody();
            int hBody = resources.TableFor(typeof(IncomingBody))
                .Allocate(body);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            uint hTrailers = (uint)ci.Invoke(
                "ask-finish", (uint)hBody)!;
            Assert.True(body.FinishCalled);
            Assert.NotEqual(0u, hTrailers);
            Assert.Same(body.Trailers,
                resources.TableFor(typeof(FutureTrailers))
                    .Get((int)hTrailers));
        }

        private sealed class CapturingResponseOutparam : ResponseOutparam
        {
            public bool SetCalled;
            public OutgoingResponse? CapturedResponse;

            public override void Set(OutgoingResponse? response)
            {
                SetCalled = true;
                CapturedResponse = response;
            }
        }

        [Fact]
        public void ResponseOutparam_set_with_ok_resolves_response()
        {
            // Fixture: ask-set-ok(param, response) calls
            // [static]response-outparam.set(param,
            // Ok(response)) — wire 3 slots: param handle,
            // outer disc=0, payload handle. Stub records the
            // resolved response.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-responseoutparam-component",
                "httpresponseoutparam.component.wasm"));
            var resources = new ResourceContext();
            var outparam = new CapturingResponseOutparam();
            var response = new OutgoingResponse();
            int hParam = resources.TableFor(typeof(ResponseOutparam))
                .Allocate(outparam);
            int hResponse = resources.TableFor(typeof(OutgoingResponse))
                .Allocate(response);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-ok", (uint)hParam, (uint)hResponse)!);
            Assert.True(outparam.SetCalled);
            Assert.Same(response, outparam.CapturedResponse);
        }

        [Fact]
        public void ResponseOutparam_set_with_err_passes_null()
        {
            // Same fixture, ask-set-err(param) calls
            // set(param, Err(internal-error)) — outer disc=1,
            // payload slot ignored. Stub gets null since v0
            // doesn't surface the error payload.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-responseoutparam-component",
                "httpresponseoutparam.component.wasm"));
            var resources = new ResourceContext();
            var outparam = new CapturingResponseOutparam();
            int hParam = resources.TableFor(typeof(ResponseOutparam))
                .Allocate(outparam);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-err", (uint)hParam)!);
            Assert.True(outparam.SetCalled);
            Assert.Null(outparam.CapturedResponse);
        }

        [Fact]
        public void Fields_constructor_yields_fresh_handle()
        {
            // Fixture: ask-new() calls [constructor]fields()
            // and returns the new handle. The auto-binder
            // walks Fields' static methods, finds
            // [WasiConstructor] on Fields.New, and registers
            // the import. Each call allocates a new instance
            // and table-allocates its handle.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fieldsctor-component",
                "httpfieldsctor.component.wasm"));
            var resources = new ResourceContext();

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            uint h = (uint)ci.Invoke("ask-new")!;
            Assert.NotEqual(0u, h);
            var inst = resources.TableFor(typeof(Fields)).Get((int)h);
            Assert.IsType<Fields>(inst);
        }

        [Fact]
        public void Constructors_zero_arg_and_resource_param()
        {
            // Fixture: three [constructor] imports — zero-arg
            // request-options + own<fields>-param outgoing-request
            // and outgoing-response. All three new shapes
            // route through BindResourceConstructor's expression-
            // tree path (param-bearing branch).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-ctors-component",
                "httpctors.component.wasm"));
            var resources = new ResourceContext();
            var headers = new Fields();
            int hHeaders = resources.TableFor(typeof(Fields))
                .Allocate(headers);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            uint hOpts = (uint)ci.Invoke("ask-new-request-options")!;
            uint hReq = (uint)ci.Invoke(
                "ask-new-outgoing-request", (uint)hHeaders)!;
            uint hResp = (uint)ci.Invoke(
                "ask-new-outgoing-response", (uint)hHeaders)!;

            Assert.NotEqual(0u, hOpts);
            Assert.NotEqual(0u, hReq);
            Assert.NotEqual(0u, hResp);
            Assert.IsType<RequestOptions>(
                resources.TableFor(typeof(RequestOptions))
                    .Get((int)hOpts));
            var req = (OutgoingRequest)resources
                .TableFor(typeof(OutgoingRequest)).Get((int)hReq)!;
            var resp = (OutgoingResponse)resources
                .TableFor(typeof(OutgoingResponse)).Get((int)hResp)!;
            // Headers ownership transferred in — the pinned
            // Fields instance flows through to both.
            Assert.Same(headers, req.Headers());
            Assert.Same(headers, resp.Headers());
        }

        [Fact]
        public void RequestOptions_set_connect_timeout_with_none_and_some()
        {
            // Fixture: option<u64> param shape — wire 2 slots
            // (disc i32 + value i64). ask-set-none calls
            // set-connect-timeout(opts, None) — disc=0,
            // value=0 (ignored). ask-set-some calls with
            // Some(42) — disc=1, value=42.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-reqopts-setters-component",
                "httpreqoptsset.component.wasm"));
            var resources = new ResourceContext();
            var opts = new RequestOptions();
            int hOpts = resources.TableFor(typeof(RequestOptions))
                .Allocate(opts);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // None — clears the timeout (already null but
            // sticks).
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-none", (uint)hOpts)!);
            Assert.Null(opts.ConnectTimeout());

            // Some(12345)
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-some", (uint)hOpts, 12345UL)!);
            Assert.Equal(12345UL, opts.ConnectTimeout());

            // None again — clears back.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-none", (uint)hOpts)!);
            Assert.Null(opts.ConnectTimeout());
        }

        [Fact]
        public void OutgoingRequest_set_path_and_authority_with_option_string()
        {
            // Fixture exercises option<string> param shape:
            // 3 wire slots (disc i32 + ptr i32 + len i32).
            // Three calls: set-path-with-query(None) clears
            // path; set-path-with-query(Some(s)) sets it;
            // set-authority(Some(s)) sets the authority.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-req-setters-component",
                "httpreqset.component.wasm"));
            var resources = new ResourceContext();
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // None — leaves _pathWithQuery null.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-path-none", (uint)hReq)!);
            Assert.Null(req.PathWithQuery());

            // Some("/foo/bar?x=1")
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-path-some", (uint)hReq)!);
            Assert.Equal("/foo/bar?x=1", req.PathWithQuery());

            // Some("example.com:443") via set-authority
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-authority-some", (uint)hReq)!);
            Assert.Equal("example.com:443", req.Authority());
        }

        [Fact]
        public void OutgoingRequest_set_method_with_no_payload_and_other_string()
        {
            // Fixture exercises HttpMethod variant param +
            // bare result return. Three calls cover:
            //   disc 0 (Get) — no payload
            //   disc 2 (Post) — no payload
            //   disc 9 (Other("PURGE")) — string payload via
            //   joined-flat (ptr+len).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-setmethod-component",
                "setmethod.component.wasm"));
            var resources = new ResourceContext();
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // Get
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-get", (uint)hReq)!);
            Assert.IsType<HttpMethodGet>(req.Method());

            // Post
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-post", (uint)hReq)!);
            Assert.IsType<HttpMethodPost>(req.Method());

            // Other("PURGE")
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-other", (uint)hReq)!);
            var other = Assert.IsType<HttpMethodOther>(req.Method());
            Assert.Equal("PURGE", other.Name);
        }

        private sealed class CapturingOutgoingRequest : OutgoingRequest
        {
            public HttpScheme? CapturedScheme;
            public bool SetSchemeCalled;
            public override void SetScheme(HttpScheme? scheme)
            {
                SetSchemeCalled = true;
                CapturedScheme = scheme;
            }
        }

        [Fact]
        public void OutgoingRequest_set_scheme_with_option_variant()
        {
            // Fixture exercises option<scheme> param — 4
            // wire slots: option disc + variant disc + ptr +
            // len. Three calls: None, Some(HTTPS) (no
            // payload), Some(Other("ftp")) (string payload).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-setscheme-component",
                "setscheme.component.wasm"));
            var resources = new ResourceContext();
            var req = new CapturingOutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // None
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-none", (uint)hReq)!);
            Assert.True(req.SetSchemeCalled);
            Assert.Null(req.CapturedScheme);

            // Some(HTTPS)
            req.SetSchemeCalled = false;
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-https", (uint)hReq)!);
            Assert.True(req.SetSchemeCalled);
            Assert.IsType<HttpSchemeHttps>(req.CapturedScheme);

            // Some(Other("ftp"))
            req.SetSchemeCalled = false;
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-other", (uint)hReq)!);
            Assert.True(req.SetSchemeCalled);
            var other = Assert.IsType<HttpSchemeOther>(req.CapturedScheme);
            Assert.Equal("ftp", other.Name);
        }

        private sealed class SchemedOutgoingRequest : OutgoingRequest
        {
            public SchemedOutgoingRequest(HttpScheme? scheme)
            {
                _scheme = scheme;
            }
        }

        [Fact]
        public void OutgoingRequest_scheme_getter_writes_option_variant_retArea()
        {
            // Three calls cover: None (option disc=0), HTTPS
            // (option=1, variant=1, no payload), Other("ftp")
            // (option=1, variant=2, ptr+len at offsets 8/12).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-getscheme-component",
                "getscheme.component.wasm"));

            // Subtest 1: None
            {
                var resources = new ResourceContext();
                var req = new SchemedOutgoingRequest(null);
                int hReq = resources.TableFor(typeof(OutgoingRequest))
                    .Allocate(req);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-scheme-disc", (uint)hReq)!);
            }

            // Subtest 2: Some(HTTPS)
            {
                var resources = new ResourceContext();
                var req = new SchemedOutgoingRequest(new HttpSchemeHttps());
                int hReq = resources.TableFor(typeof(OutgoingRequest))
                    .Allocate(req);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-scheme-disc", (uint)hReq)!);
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-scheme-variant", (uint)hReq)!);
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-scheme-other-len", (uint)hReq)!);
            }

            // Subtest 3: Some(Other("ftp"))
            {
                var resources = new ResourceContext();
                var req = new SchemedOutgoingRequest(
                    new HttpSchemeOther("ftp"));
                int hReq = resources.TableFor(typeof(OutgoingRequest))
                    .Allocate(req);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-scheme-disc", (uint)hReq)!);
                Assert.Equal(2u, (uint)ci.Invoke(
                    "ask-scheme-variant", (uint)hReq)!);
                // "ftp" UTF-8 = 3 bytes
                Assert.Equal(3u, (uint)ci.Invoke(
                    "ask-scheme-other-len", (uint)hReq)!);
            }
        }

        [Fact]
        public void Fields_from_list_decodes_list_of_tuple_string_bytes()
        {
            // Fixture: ask-from-list calls
            // [static]fields.from-list with two entries laid
            // out at fixed memory offsets:
            //   ("content-type", "text/plain")
            //   ("authorization", "Bearer XYZ")
            // The binder decodes list-ptr/list-len + per-elem
            // 16 bytes (kPtr, kLen, vPtr, vLen). Test asserts
            // both entries surface through Fields.Get().
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-fields-fromlist-component",
                "fieldsfromlist.component.wasm"));
            var resources = new ResourceContext();

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            uint h = (uint)ci.Invoke("ask-from-list")!;
            Assert.NotEqual(0u, h);
            var fields = (Fields)resources.TableFor(typeof(Fields))
                .Get((int)h)!;
            var ct = fields.Get("content-type");
            Assert.Single(ct);
            Assert.Equal("text/plain",
                System.Text.Encoding.UTF8.GetString(ct[0]));
            var auth = fields.Get("authorization");
            Assert.Single(auth);
            Assert.Equal("Bearer XYZ",
                System.Text.Encoding.UTF8.GetString(auth[0]));
        }

        private sealed class StagedTrailers : FutureTrailers
        {
            public bool Ready;
            public Fields? Trailers;
            public override (bool ready, Fields? trailers) Get()
                => (Ready, Trailers);
        }

        [Fact]
        public void FutureTrailers_get_writes_three_state_retArea()
        {
            // Tests the deeply nested option<result<option<own
            // <trailers>>, error-code>> retArea encoder.
            // Three states:
            //   not-ready          → outer disc=0
            //   ready-no-trailers  → outer=1, result=0, inner=0
            //   ready-with-trailers→ outer=1, result=0, inner=1,
            //                        handle at +20
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-future-trailers-get-component",
                "futuretrailers.component.wasm"));

            // Subtest 1: not ready
            {
                var resources = new ResourceContext();
                var ft = new StagedTrailers { Ready = false };
                int hFt = resources.TableFor(typeof(FutureTrailers))
                    .Allocate(ft);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-outer-disc", (uint)hFt)!);
            }

            // Subtest 2: ready, no trailers
            {
                var resources = new ResourceContext();
                var ft = new StagedTrailers { Ready = true,
                    Trailers = null };
                int hFt = resources.TableFor(typeof(FutureTrailers))
                    .Allocate(ft);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-outer-disc", (uint)hFt)!);
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-result-disc", (uint)hFt)!);
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-inner-disc", (uint)hFt)!);
            }

            // Subtest 3: ready with trailers
            {
                var resources = new ResourceContext();
                var trailers = new Fields();
                var ft = new StagedTrailers { Ready = true,
                    Trailers = trailers };
                int hFt = resources.TableFor(typeof(FutureTrailers))
                    .Allocate(ft);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-outer-disc", (uint)hFt)!);
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-inner-disc", (uint)hFt)!);
                uint hTrailers = (uint)ci.Invoke(
                    "ask-trailers-handle", (uint)hFt)!;
                Assert.NotEqual(0u, hTrailers);
                Assert.Same(trailers,
                    resources.TableFor(typeof(Fields))
                        .Get((int)hTrailers));
            }
        }

        private sealed class StagedFutureResponse : FutureIncomingResponse
        {
            public bool Ready;
            public IncomingResponse? Response;
            public override (bool ready, IncomingResponse? response) Get()
                => (Ready, Response);
        }

        [Fact]
        public void FutureIncomingResponse_get_writes_three_state_retArea()
        {
            // Tests the deeply nested option<result<result<own
            // <incoming-response>, error-code>, _>> retArea
            // encoder. Two states exercised:
            //   not-ready  → outer disc=0
            //   ready      → outer=1, outer-result=0,
            //                inner-result=0, handle at +24
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-future-response-get-component",
                "futureresp.component.wasm"));

            // Subtest 1: not ready
            {
                var resources = new ResourceContext();
                var fr = new StagedFutureResponse { Ready = false };
                int hFr = resources.TableFor(typeof(FutureIncomingResponse))
                    .Allocate(fr);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-outer-disc", (uint)hFr)!);
            }

            // Subtest 2: ready with response
            {
                var resources = new ResourceContext();
                var resp = new IncomingResponse();
                var fr = new StagedFutureResponse { Ready = true,
                    Response = resp };
                int hFr = resources.TableFor(typeof(FutureIncomingResponse))
                    .Allocate(fr);
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    new HttpTypes(resources).BindToRuntime(runtime);
                });
                Assert.Equal(1u, (uint)ci.Invoke(
                    "ask-outer-disc", (uint)hFr)!);
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-outer-result-disc", (uint)hFr)!);
                Assert.Equal(0u, (uint)ci.Invoke(
                    "ask-inner-result-disc", (uint)hFr)!);
                uint hResp = (uint)ci.Invoke(
                    "ask-response-handle", (uint)hFr)!;
                Assert.NotEqual(0u, hResp);
                Assert.Same(resp,
                    resources.TableFor(typeof(IncomingResponse))
                        .Get((int)hResp));
            }
        }

        // Stub mapper that returns whatever ErrorCode the
        // test pre-stages. Used to drive the encoder through
        // the http-error-code retArea path with a real wasm
        // call. Reflection-walked attributes don't transfer
        // from the interface to the implementing method, so
        // the [WasiOptionalReturn] / [WasiMethodName] markers
        // are repeated on the impl — same pattern as
        // HttpErrorCodeMapperSource.
        private sealed class StagedHttpErrorCodeMapper
            : IHttpErrorCodeMapper
        {
            public ErrorCode? Staged;

            [WasiOptionalReturn]
            [WasiMethodName("http-error-code")]
            public ErrorCode? HttpErrorCode(
                Wacs.WASI.Preview2.Io.Error err) => Staged;
        }

        private static ComponentInstance MakeErrCodeInstance(
            byte[] bytes, ResourceContext resources,
            StagedHttpErrorCodeMapper mapper)
        {
            return ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources, mapper).BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });
        }

        [Fact]
        public void HttpErrorCode_with_no_payload_writes_disc_only()
        {
            // Some(connection-refused) — disc 6, no payload.
            // The wasm reads the variant disc byte at retArea+8
            // and the option<u64> payload (used to confirm
            // it stayed zero).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-error-code-component",
                "httperrcode.component.wasm"));
            var resources = new ResourceContext();
            var ioErr = new Wacs.WASI.Preview2.Io.Error("e");
            int hErr = resources.TableFor(typeof(
                Wacs.WASI.Preview2.Io.Error)).Allocate(ioErr);
            var mapper = new StagedHttpErrorCodeMapper {
                Staged = new ErrorCodeConnectionRefused() };
            var ci = MakeErrCodeInstance(bytes, resources, mapper);

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-http-error-code", (uint)hErr)!);
            Assert.Equal(6u, (uint)ci.Invoke(
                "ask-variant-disc", (uint)hErr)!);
            // No-payload case leaves the variant payload area
            // zeroed; option-disc byte at variant-payload+0
            // (= retArea+16) reads as 0.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-payload-option-disc", (uint)hErr)!);
        }

        [Fact]
        public void HttpErrorCode_with_option_u64_payload()
        {
            // Some(HTTP-request-body-size(Some(0xCAFEBABE_DEADBEEF)))
            // — disc 17, option<u64> payload.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-error-code-component",
                "httperrcode.component.wasm"));
            var resources = new ResourceContext();
            var ioErr = new Wacs.WASI.Preview2.Io.Error("e");
            int hErr = resources.TableFor(typeof(
                Wacs.WASI.Preview2.Io.Error)).Allocate(ioErr);
            var mapper = new StagedHttpErrorCodeMapper {
                Staged = new ErrorCodeHttpRequestBodySize(
                    0xCAFEBABEDEADBEEFUL) };
            var ci = MakeErrCodeInstance(bytes, resources, mapper);

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-http-error-code", (uint)hErr)!);
            Assert.Equal(17u, (uint)ci.Invoke(
                "ask-variant-disc", (uint)hErr)!);
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-payload-option-disc", (uint)hErr)!);
            // u64 lives at variant-payload + 8 = retArea + 24
            Assert.Equal(0xCAFEBABEDEADBEEFUL,
                (ulong)ci.Invoke(
                    "ask-payload-u64", (uint)hErr)!);
        }

        [Fact]
        public void HttpErrorCode_with_option_string_payload()
        {
            // Some(internal-error(Some("oops, host died")))
            // — disc 38, option<string> payload.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-error-code-component",
                "httperrcode.component.wasm"));
            var resources = new ResourceContext();
            var ioErr = new Wacs.WASI.Preview2.Io.Error("e");
            int hErr = resources.TableFor(typeof(
                Wacs.WASI.Preview2.Io.Error)).Allocate(ioErr);
            const string msg = "oops, host died";
            var mapper = new StagedHttpErrorCodeMapper {
                Staged = new ErrorCodeInternalError(msg) };
            var ci = MakeErrCodeInstance(bytes, resources, mapper);

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-http-error-code", (uint)hErr)!);
            Assert.Equal(38u, (uint)ci.Invoke(
                "ask-variant-disc", (uint)hErr)!);
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-payload-option-disc", (uint)hErr)!);
            uint ptr = (uint)ci.Invoke(
                "ask-payload-string-ptr", (uint)hErr)!;
            uint len = (uint)ci.Invoke(
                "ask-payload-string-len", (uint)hErr)!;
            Assert.Equal((uint)msg.Length, len);
            Assert.NotEqual(0u, ptr);
            // Reconstruct the string by reading byte-by-byte
            // through the read-byte export.
            var bytesOut = new byte[len];
            for (uint i = 0; i < len; i++)
                bytesOut[i] = (byte)(uint)ci.Invoke(
                    "read-byte", ptr + i)!;
            Assert.Equal(msg, System.Text.Encoding.UTF8
                .GetString(bytesOut));
        }

        [Fact]
        public void HttpErrorCode_top_level_returns_option_none_disc()
        {
            // wasi:http/types.http-error-code maps an io-error
            // borrow → option<error-code>. v0 always returns
            // None — host method returns null, binder writes
            // disc=0 to the retArea.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-error-code-component",
                "httperrcode.component.wasm"));
            var resources = new ResourceContext();
            var ioError = new Wacs.WASI.Preview2.Io.Error(
                "stub: not a real error");
            int hErr = resources.TableFor(typeof(
                Wacs.WASI.Preview2.Io.Error))
                .Allocate(ioError);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources,
                    new HttpErrorCodeMapperSource())
                    .BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-http-error-code", (uint)hErr)!);
        }

        // Handler that uses [WasiSpecErrorCode] — opts in to
        // the full WASI-HTTP error-code variant retArea
        // layout (align 8, 40 bytes). Throws
        // WasiErrorCodeException to drive the Err side
        // through ErrorCodeEncoder.
        private sealed class SpecHandler : IOutgoingHandler
        {
            public ErrorCode? StagedError;
            public FutureIncomingResponse Response =
                new FutureIncomingResponse();

            [WasiErrorResult]
            [WasiSpecErrorCode]
            public FutureIncomingResponse Handle(
                OutgoingRequest request,
                [WasiOptionalParam] RequestOptions? options)
            {
                if (StagedError != null)
                    throw new WasiErrorCodeException(StagedError);
                return Response;
            }
        }

        [Fact]
        public void OutgoingHandler_handle_spec_layout_writes_ok_at_offset_8()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-handler-spec-component",
                "handlerspec.component.wasm"));
            var resources = new ResourceContext();
            var handler = new SpecHandler();
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:http/outgoing-handler@0.2.3",
                    handler, resources);
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // Ok path: outer disc=0, handle at +8.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hReq)!);
            uint hResp = (uint)ci.Invoke(
                "ask-ok-handle", (uint)hReq)!;
            Assert.NotEqual(0u, hResp);
            Assert.Same(handler.Response,
                resources.TableFor(typeof(FutureIncomingResponse))
                    .Get((int)hResp));
        }

        [Fact]
        public void OutgoingHandler_handle_spec_layout_writes_err_no_payload()
        {
            // Throw connection-refused (disc 6, no payload).
            // The encoder writes disc=6 at retArea+8;
            // payload area at +16 stays zeroed.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-handler-spec-component",
                "handlerspec.component.wasm"));
            var resources = new ResourceContext();
            var handler = new SpecHandler {
                StagedError = new ErrorCodeConnectionRefused() };
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:http/outgoing-handler@0.2.3",
                    handler, resources);
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hReq)!);
            Assert.Equal(6u, (uint)ci.Invoke(
                "ask-err-disc", (uint)hReq)!);
            // Payload area at +16: option-disc reads 0
            // (no-payload case leaves bytes zeroed).
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-err-payload-opt-disc", (uint)hReq)!);
        }

        [Fact]
        public void OutgoingHandler_handle_spec_layout_writes_err_with_string_payload()
        {
            // Throw internal-error("upstream rejected") —
            // disc 38 + option<string> payload.
            const string msg = "upstream rejected";
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-handler-spec-component",
                "handlerspec.component.wasm"));
            var resources = new ResourceContext();
            var handler = new SpecHandler {
                StagedError = new ErrorCodeInternalError(msg) };
            var req = new OutgoingRequest();
            int hReq = resources.TableFor(typeof(OutgoingRequest))
                .Allocate(req);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:http/outgoing-handler@0.2.3",
                    handler, resources);
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hReq)!);
            Assert.Equal(38u, (uint)ci.Invoke(
                "ask-err-disc", (uint)hReq)!);
            // Payload at +16: option<string> Some
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-err-payload-opt-disc", (uint)hReq)!);
            uint ptr = (uint)ci.Invoke(
                "ask-err-payload-string-ptr", (uint)hReq)!;
            uint len = (uint)ci.Invoke(
                "ask-err-payload-string-len", (uint)hReq)!;
            Assert.Equal((uint)msg.Length, len);
            // Reconstruct the string byte-by-byte.
            var bytesOut = new byte[len];
            for (uint i = 0; i < len; i++)
                bytesOut[i] = (byte)(uint)ci.Invoke(
                    "read-byte", ptr + i)!;
            Assert.Equal(msg, System.Text.Encoding.UTF8
                .GetString(bytesOut));
        }

        // FutureTrailers stub that throws to surface Err.
        private sealed class ErringTrailers : FutureTrailers
        {
            public ErrorCode Error =
                new ErrorCodeConnectionTimeout();
            public override (bool ready, Fields? trailers) Get()
                => throw new WasiErrorCodeException(Error);
        }

        [Fact]
        public void FutureTrailers_get_inner_err_path()
        {
            // Throwing WasiErrorCodeException routes the
            // future-trailers.get retArea to the inner Err
            // shape: outer=1, result=1 (Err), encoder writes
            // error-code at retArea+16 (32 bytes).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-future-trailers-get-component",
                "futuretrailers.component.wasm"));
            var resources = new ResourceContext();
            var ft = new ErringTrailers {
                Error = new ErrorCodeConnectionTimeout() };
            int hFt = resources.TableFor(typeof(FutureTrailers))
                .Allocate(ft);
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            // outer=1 (Some), result=1 (Err) at fixture's
            // ask-result-disc which reads retArea+8
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hFt)!);
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-result-disc", (uint)hFt)!);
            // ask-inner-disc reads retArea+16 — for the Err
            // path that's the error-code variant disc, which
            // is 8 (connection-timeout).
            Assert.Equal(8u, (uint)ci.Invoke(
                "ask-inner-disc", (uint)hFt)!);
        }

        // FutureIncomingResponse stub that throws Err.
        private sealed class ErringFutureResponse : FutureIncomingResponse
        {
            public ErrorCode Error =
                new ErrorCodeHttpResponseTimeout();
            public override (bool ready, IncomingResponse? response) Get()
                => throw new WasiErrorCodeException(Error);
        }

        [Fact]
        public void FutureIncomingResponse_get_inner_err_path()
        {
            // Throwing surfaces the inner Err side: outer=1,
            // outer-result=0 (Ok of outer), inner-result=1
            // (Err of inner), encoder writes error-code at
            // retArea+24.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-future-response-get-component",
                "futureresp.component.wasm"));
            var resources = new ResourceContext();
            var fr = new ErringFutureResponse {
                Error = new ErrorCodeHttpResponseTimeout() };
            int hFr = resources.TableFor(typeof(FutureIncomingResponse))
                .Allocate(fr);
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hFr)!);
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-outer-result-disc", (uint)hFr)!);
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-inner-result-disc", (uint)hFr)!);
            // ask-response-handle reads retArea+24 — for Err
            // that's the error-code variant disc byte. We
            // can't extract just 1 byte through this i32-load
            // export, so verify only the low byte.
            uint raw = (uint)ci.Invoke(
                "ask-response-handle", (uint)hFr)!;
            Assert.Equal(33u, raw & 0xFFu);  // disc 33 = HTTP-response-timeout
        }

        // FutureIncomingResponse stub that throws the
        // already-consumed signal on every Get() call.
        private sealed class ConsumedFutureResponse
            : FutureIncomingResponse
        {
            public override (bool ready, IncomingResponse? response) Get()
                => throw new WasiFutureAlreadyConsumedException();
        }

        [Fact]
        public void FutureIncomingResponse_get_outer_err_already_consumed()
        {
            // Throwing WasiFutureAlreadyConsumedException
            // signals the outer-Err state of
            // option<result<result<own<X>, error-code>, _>>:
            //   outer option = Some
            //   outer result = Err (bare unit, no payload)
            // The fixture reads outer disc=1 + outer-result
            // disc=1 + the rest of the 56-byte retArea
            // staying zeroed.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-http-future-response-get-component",
                "futureresp.component.wasm"));
            var resources = new ResourceContext();
            var fr = new ConsumedFutureResponse();
            int hFr = resources.TableFor(typeof(FutureIncomingResponse))
                .Allocate(fr);
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new HttpTypes(resources).BindToRuntime(runtime);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-disc", (uint)hFr)!);
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-outer-result-disc", (uint)hFr)!);
        }

        [Fact]
        public void Http_resource_markers_are_allocatable()
        {
            // Smoke test: every wasi:http resource type can
            // be allocated through ResourceContext (the
            // [WasiResource] attribute + table machinery
            // works for the entire marker set).
            var ctx = new ResourceContext();
            Assert.True(ctx.TableFor(typeof(OutgoingRequest))
                .Allocate(new OutgoingRequest()) > 0);
            Assert.True(ctx.TableFor(typeof(IncomingRequest))
                .Allocate(new IncomingRequest()) > 0);
            Assert.True(ctx.TableFor(typeof(IncomingResponse))
                .Allocate(new IncomingResponse()) > 0);
            Assert.True(ctx.TableFor(typeof(OutgoingResponse))
                .Allocate(new OutgoingResponse()) > 0);
            Assert.True(ctx.TableFor(typeof(RequestOptions))
                .Allocate(new RequestOptions()) > 0);
            Assert.True(ctx.TableFor(typeof(IncomingBody))
                .Allocate(new IncomingBody()) > 0);
            Assert.True(ctx.TableFor(typeof(OutgoingBody))
                .Allocate(new OutgoingBody()) > 0);
            Assert.True(ctx.TableFor(typeof(FutureIncomingResponse))
                .Allocate(new FutureIncomingResponse()) > 0);
            Assert.True(ctx.TableFor(typeof(FutureTrailers))
                .Allocate(new FutureTrailers()) > 0);
            Assert.True(ctx.TableFor(typeof(ResponseOutparam))
                .Allocate(new ResponseOutparam()) > 0);
        }
    }
}
