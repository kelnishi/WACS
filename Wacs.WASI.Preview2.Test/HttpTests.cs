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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-get-len", (uint)handle)!);
            Assert.Equal((uint)'b', (uint)ci.Invoke(
                "ask-get-first-byte", (uint)handle)!);
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
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<OutgoingRequest>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<OutgoingRequest>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<RequestOptions>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<RequestOptions>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<OutgoingRequest>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<OutgoingRequest>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<IncomingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<OutgoingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<IncomingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<OutgoingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
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
                runtime.BindWasiResource<IncomingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<OutgoingResponse>(
                    "wasi:http/types@0.2.3", resources);
                runtime.BindWasiResource<Fields>(
                    "wasi:http/types@0.2.3", resources);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-headers", (uint)handle)!);
            // Returned Fields handle was dropped by the
            // guest; only the original IncomingResponse
            // handle remains.
            Assert.Equal(0, resources.TableFor(typeof(Fields)).Count);
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
