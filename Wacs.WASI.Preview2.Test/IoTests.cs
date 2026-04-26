// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class IoTests
    {
        [Fact]
        public void Pollable_default_Ready_returns_true()
        {
            var p = new Pollable();
            Assert.True(p.Ready());
        }

        [Fact]
        public void Pollable_default_Block_returns_immediately()
        {
            // Default impl is "always ready" — Block should
            // return without blocking. If it ever hangs, the
            // test infrastructure will fail it on timeout
            // rather than passing silently.
            var p = new Pollable();
            p.Block();
        }

        [Fact]
        public void Error_carries_message_through_ToDebugString()
        {
            var e = new Error("disk full");
            Assert.Equal("disk full", e.ToDebugString());
        }

        [Fact]
        public void Error_default_constructor_normalizes_null_message()
        {
            // Defensive: a null message argument shouldn't
            // surface as a NullReferenceException when guests
            // call to-debug-string. Norm to empty string.
            var e = new Error(null!);
            Assert.Equal("", e.ToDebugString());
        }

        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void Poll_resolves_borrow_pollable_array_and_returns_u32_indices()
        {
            // Fixture: ask-poll(a, b) builds list<borrow<pollable>>
            // = [a, b] in linear memory, calls poll, returns the
            // output list-len. Default Pollable is always-ready,
            // so both inputs report ready → count == 2.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-io-poll-component", "iopoll.component.wasm"));
            var resources = new ResourceContext();
            var poll = new PollSource();
            var p1 = new Pollable();
            var p2 = new Pollable();
            int hA = resources.TableFor(typeof(Pollable))
                .Allocate(p1);
            int hB = resources.TableFor(typeof(Pollable))
                .Allocate(p2);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:io/poll@0.2.3", poll, resources);
                runtime.BindWasiResource<Pollable>(
                    "wasi:io/poll@0.2.3", resources);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-poll", (uint)hA, (uint)hB)!);
        }

        [Fact]
        public void Error_to_debug_string_lifts_string_through_resource_method()
        {
            // Fixture: ask-debug(handle) calls
            // error.to-debug-string(handle) into a callee-
            // allocated retArea, returns that retArea pointer.
            // Outer canon-lift on the export reads (ptr, len)
            // from retArea / retArea+4 and lifts the string.
            // Stub Error has Message="disk full"; expect that
            // string back from the canon-lifted return.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-io-error-component", "ioerror.component.wasm"));
            var resources = new ResourceContext();
            var err = new Error("disk full");
            int handle = resources.TableFor(typeof(Error))
                .Allocate(err);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Error>(
                    "wasi:io/error@0.2.3", resources);
            });

            var result = ci.Invoke("ask-debug", (uint)handle);
            Assert.Equal("disk full", result);
        }
    }
}
