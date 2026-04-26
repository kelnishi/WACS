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
    /// <summary>
    /// Tests for wasi:io/streams output-stream resource.
    /// Covers list&lt;u8&gt; param + result&lt;_, stream-error&gt;
    /// return canon-lower wrappers — the heart of the streams
    /// surface.
    /// </summary>
    public class OutputStreamTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        /// <summary>Capturing OutputStream — collects bytes
        /// instead of discarding them, so the test can assert
        /// on what the guest wrote.</summary>
        public sealed class CapturingStream : OutputStream
        {
            public System.Collections.Generic.List<byte> Captured =
                new System.Collections.Generic.List<byte>();

            public override void Write(byte[] contents)
            {
                Captured.AddRange(contents);
            }
        }

        [Fact]
        public void Write_list_u8_param_propagates_to_host_stream()
        {
            // Fixture imports
            //   wasi:io/streams.[method]output-stream.write
            //   wasi:io/streams.[resource-drop]output-stream
            // and exports
            //   try-write(handle: u32) -> u32
            // which calls write(handle, "hello") then drops the
            // handle. The test pre-allocates an OutputStream
            // handle via the resource table, calls try-write,
            // and asserts the captured bytes match "hello".
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-output-stream-component", "sw.component.wasm"));
            var resources = new ResourceContext();
            var stream = new CapturingStream();

            // Pre-seed the resource table with our stream so
            // the test can hand the handle to try-write directly.
            // (In a real component, the handle would come from
            // wasi:cli/stdout.get-stdout — that interface ships
            // in a follow-up; for now we manually allocate.)
            int handle = resources.TableFor(typeof(OutputStream))
                .Allocate(stream);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<OutputStream>(
                    "wasi:io/streams@0.2.3", resources);
            });

            ci.Invoke("try-write", (uint)handle);

            Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l',
                (byte)'l', (byte)'o' }, stream.Captured.ToArray());

            // After drop the table should be empty.
            Assert.Equal(0, resources.TableFor(typeof(OutputStream)).Count);
        }

        public sealed class TrackingStream : OutputStream
        {
            public ulong LastWriteZeroes;
            public ulong LastBlockingWriteZeroes;
            public override void WriteZeroes(ulong len)
                => LastWriteZeroes = len;
            public override void BlockingWriteZeroesAndFlush(ulong len)
                => LastBlockingWriteZeroes = len;
        }

        [Fact]
        public void WriteZeroes_threads_u64_param_through_void_result_wrapper()
        {
            // Fixture: ask-zeroes(handle) calls
            // write-zeroes(8) then blocking-write-zeroes-and-flush(16).
            // Stub captures both lengths; test asserts both
            // and that the outer-disc bytes summed to 0.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-io-zeroes-component", "iozeroes.component.wasm"));
            var resources = new ResourceContext();
            var stream = new TrackingStream();
            int handle = resources.TableFor(typeof(OutputStream))
                .Allocate(stream);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<OutputStream>(
                    "wasi:io/streams@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-zeroes", (uint)handle)!);
            Assert.Equal(8UL, stream.LastWriteZeroes);
            Assert.Equal(16UL, stream.LastBlockingWriteZeroes);
        }
    }
}
