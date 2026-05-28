// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core;
using Wacs.Core.Runtime;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice K coverage: end-to-end validation of the
    /// host-function wire-ups by hand-building minimal core
    /// .wasm modules that import a Preview 3 host function,
    /// wrap it in an exported function, and round-trip through
    /// <see cref="WasmRuntime"/>. The actual interpreter loop
    /// runs the wasm wrapper, the wrapper calls the host
    /// function via the registered binding, and the test
    /// confirms the value the host returned.
    ///
    /// <para>This is the integration test the prior slices'
    /// unit tests can't directly produce — they exercise the
    /// <c>Invoke*</c> bodies directly. Slice K wraps the same
    /// bodies behind the wasm-runtime indirection that real
    /// component-wasm fixtures would.</para>
    ///
    /// <para>Builds wasm bytes by hand because (a) we don't
    /// want a build-time wasm toolchain dependency in the test
    /// project, and (b) the byte-level construction makes the
    /// expected wire shape explicit.</para>
    /// </summary>
    public class SyntheticFixtureTests
    {
        // ---- wasm-bytes builder helpers ------------------------------

        private static void WriteLeb128U32(BinaryWriter w, uint value)
        {
            while (value >= 0x80)
            {
                w.Write((byte)(value | 0x80));
                value >>= 7;
            }
            w.Write((byte)value);
        }

        private static void WriteName(BinaryWriter w, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            WriteLeb128U32(w, (uint)bytes.Length);
            w.Write(bytes);
        }

        // ---- Hand-built wasm module:
        //
        //   (module
        //     (import "wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15"
        //             "now" (func $now (result i64)))
        //     (func (export "test-now") (result i64)
        //       call $now))
        private static byte[] BuildClockNowFixture()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            // magic + version
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });

            // Section helpers — write section id, then leb128-
            // prefixed body bytes.
            void WriteSection(byte id, Action<BinaryWriter> body)
            {
                using var bodyMs = new MemoryStream();
                using var bodyW = new BinaryWriter(bodyMs);
                body(bodyW);
                var bodyBytes = bodyMs.ToArray();
                w.Write(id);
                WriteLeb128U32(w, (uint)bodyBytes.Length);
                w.Write(bodyBytes);
            }

            // Type section (id 1):
            //   1 type: () -> i64
            WriteSection(1, bw =>
            {
                WriteLeb128U32(bw, 1); // num types
                bw.Write((byte)0x60);  // function-type marker
                WriteLeb128U32(bw, 0); // 0 params
                WriteLeb128U32(bw, 1); // 1 result
                bw.Write((byte)0x7E);  // i64
            });

            // Import section (id 2):
            //   1 import: (module, "now", func, type 0)
            WriteSection(2, bw =>
            {
                WriteLeb128U32(bw, 1); // num imports
                WriteName(bw,
                    "wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15");
                WriteName(bw, "now");
                bw.Write((byte)0x00); // func kind
                WriteLeb128U32(bw, 0); // type index
            });

            // Function section (id 3):
            //   1 local func of type 0
            WriteSection(3, bw =>
            {
                WriteLeb128U32(bw, 1); // num funcs
                WriteLeb128U32(bw, 0); // type index
            });

            // Export section (id 7):
            //   1 export: "test-now" func index 1
            //   (index 0 is the import; index 1 is the local func)
            WriteSection(7, bw =>
            {
                WriteLeb128U32(bw, 1); // num exports
                WriteName(bw, "test-now");
                bw.Write((byte)0x00); // func kind
                WriteLeb128U32(bw, 1); // func index 1
            });

            // Code section (id 10):
            //   1 code body:
            //     locals: 0 groups
            //     instructions: call 0, end
            WriteSection(10, bw =>
            {
                WriteLeb128U32(bw, 1); // num bodies
                using var codeMs = new MemoryStream();
                using var codeW = new BinaryWriter(codeMs);
                WriteLeb128U32(codeW, 0);   // 0 local groups
                codeW.Write((byte)0x10);    // call
                WriteLeb128U32(codeW, 0);   // func idx 0 (import)
                codeW.Write((byte)0x0B);    // end
                var codeBytes = codeMs.ToArray();
                WriteLeb128U32(bw, (uint)codeBytes.Length);
                bw.Write(codeBytes);
            });

            return ms.ToArray();
        }

        // Same shape but for `wasi:random/random.get-random-u64`:
        //   (import "wasi:random/random@0.3.0-rc-2026-03-15"
        //           "get-random-u64" (func (result i64)))
        //   (func (export "test-random") (result i64) call 0)
        private static byte[] BuildRandomU64Fixture()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });

            void WriteSection(byte id, Action<BinaryWriter> body)
            {
                using var bodyMs = new MemoryStream();
                using var bodyW = new BinaryWriter(bodyMs);
                body(bodyW);
                var bodyBytes = bodyMs.ToArray();
                w.Write(id);
                WriteLeb128U32(w, (uint)bodyBytes.Length);
                w.Write(bodyBytes);
            }

            WriteSection(1, bw =>
            {
                WriteLeb128U32(bw, 1);
                bw.Write((byte)0x60);
                WriteLeb128U32(bw, 0);
                WriteLeb128U32(bw, 1);
                bw.Write((byte)0x7E);
            });
            WriteSection(2, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteName(bw,
                    "wasi:random/random@0.3.0-rc-2026-03-15");
                WriteName(bw, "get-random-u64");
                bw.Write((byte)0x00);
                WriteLeb128U32(bw, 0);
            });
            WriteSection(3, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteLeb128U32(bw, 0);
            });
            WriteSection(7, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteName(bw, "test-random");
                bw.Write((byte)0x00);
                WriteLeb128U32(bw, 1);
            });
            WriteSection(10, bw =>
            {
                WriteLeb128U32(bw, 1);
                using var codeMs = new MemoryStream();
                using var codeW = new BinaryWriter(codeMs);
                WriteLeb128U32(codeW, 0);
                codeW.Write((byte)0x10);
                WriteLeb128U32(codeW, 0);
                codeW.Write((byte)0x0B);
                var codeBytes = codeMs.ToArray();
                WriteLeb128U32(bw, (uint)codeBytes.Length);
                bw.Write(codeBytes);
            });

            return ms.ToArray();
        }

        // ---- End-to-end fixture tests --------------------------------

        [Fact]
        public void Fixture_clock_now_round_trips_through_interpreter()
        {
            // Build the fixture wasm, register the WASI Preview 3
            // host bindings, instantiate, and invoke the wrapper.
            // The wrapper calls the host's monotonic-clock.now()
            // and returns the i64 value; we assert it's positive.
            var runtime = new WasmRuntime();
            var host = new WasiPreview3Host();
            host.BindToRuntime(runtime);

            using var ms = new MemoryStream(BuildClockNowFixture());
            var module = BinaryModuleParser.ParseWasm(ms);
            runtime.InstantiateModule(module);

            Assert.True(runtime.TryGetExportedFunction(
                "test-now", out var addr));
            var invoke = runtime.CreateInvokerFunc<long>(
                addr, new InvokerOptions());

            var n1 = invoke();
            var n2 = invoke();
            // Monotonic clock should be non-decreasing and
            // strictly positive on a freshly-allocated host
            // (Stopwatch elapsed since process start).
            Assert.True(n1 > 0,
                $"Expected monotonic-clock.now() > 0, got {n1}.");
            Assert.True(n2 >= n1,
                $"Monotonic non-decreasing: n1={n1}, n2={n2}.");
        }

        [Fact]
        public void Fixture_random_u64_round_trips_through_interpreter()
        {
            var runtime = new WasmRuntime();
            var host = new WasiPreview3Host();
            host.BindToRuntime(runtime);

            using var ms = new MemoryStream(BuildRandomU64Fixture());
            var module = BinaryModuleParser.ParseWasm(ms);
            runtime.InstantiateModule(module);

            Assert.True(runtime.TryGetExportedFunction(
                "test-random", out var addr));
            var invoke = runtime.CreateInvokerFunc<long>(
                addr, new InvokerOptions());

            // CSPRNG-fill of 8 bytes — three consecutive zeros
            // is astronomically unlikely. Pin the bound to
            // "at least one non-zero value across three reads".
            long a = invoke();
            long b = invoke();
            long c = invoke();
            Assert.True(a != 0 || b != 0 || c != 0,
                "Three consecutive CSPRNG reads all returned 0 — " +
                "the BCL's RandomNumberGenerator is misbehaving.");
        }
    }
}
