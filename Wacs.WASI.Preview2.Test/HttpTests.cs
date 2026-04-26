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
