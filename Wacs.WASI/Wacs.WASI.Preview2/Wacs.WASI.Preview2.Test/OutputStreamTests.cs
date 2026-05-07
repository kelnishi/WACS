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

            public override Result<Unit, StreamError> Write(byte[] contents)
            {
                Captured.AddRange(contents);
                return Result<Unit, StreamError>.FromOk(Unit.Value);
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
                new StreamBindings(resources).BindToRuntime(runtime));

            ci.Invoke("try-write", (uint)handle);

            Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l',
                (byte)'l', (byte)'o' }, stream.Captured.ToArray());

            // After drop the table should be empty.
            Assert.Equal(0, resources.TableFor(typeof(OutputStream)).Count);
        }

        public sealed class FixedSourceStream : InputStream
        {
            private readonly byte[] _bytes;
            private int _pos;
            public FixedSourceStream(byte[] bytes) { _bytes = bytes; }
            public override Result<byte[], StreamError> Read(ulong len)
            {
                int avail = _bytes.Length - _pos;
                int n = (int)System.Math.Min(
                    System.Math.Min(len, (ulong)int.MaxValue),
                    (ulong)avail);
                var slice = new byte[n];
                System.Array.Copy(_bytes, _pos, slice, 0, n);
                _pos += n;
                return Result<byte[], StreamError>.FromOk(slice);
            }
            public override Result<byte[], StreamError> BlockingRead(ulong len)
                => Read(len);
        }

        public sealed class SinkStream : OutputStream
        {
            public System.Collections.Generic.List<byte> Captured =
                new System.Collections.Generic.List<byte>();
            public override Result<Unit, StreamError> Write(byte[] contents)
            {
                Captured.AddRange(contents);
                return Result<Unit, StreamError>.FromOk(Unit.Value);
            }
        }

        [Fact]
        public void Splice_resolves_borrow_input_stream_and_returns_u64_count()
        {
            // Fixture: ask-splice(out, in) calls splice(out, in,
            // 32). Stub source has 12 bytes; splice copies all
            // 12 (length 32 caps to source remainder), returns
            // 12 in the Ok u64 payload. Test asserts the count
            // and that the sink received the source bytes.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-io-splice-component", "iosplice.component.wasm"));
            var resources = new ResourceContext();
            var source = new FixedSourceStream(
                System.Text.Encoding.UTF8.GetBytes("hello, splice"));
            var sink = new SinkStream();
            int hOut = resources.TableFor(typeof(OutputStream))
                .Allocate(sink);
            int hIn = resources.TableFor(typeof(InputStream))
                .Allocate(source);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new StreamBindings(resources).BindToRuntime(runtime));

            // "hello, splice" = 13 bytes, len=32 caps at 13.
            Assert.Equal(13UL, (ulong)ci.Invoke(
                "ask-splice", (uint)hOut, (uint)hIn)!);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(
                "hello, splice"), sink.Captured.ToArray());
        }

        public sealed class TrackingStream : OutputStream
        {
            public ulong LastWriteZeroes;
            public ulong LastBlockingWriteZeroes;
            public override Result<Unit, StreamError> WriteZeroes(ulong len)
            {
                LastWriteZeroes = len;
                return Result<Unit, StreamError>.FromOk(Unit.Value);
            }
            public override Result<Unit, StreamError>
                BlockingWriteZeroesAndFlush(ulong len)
            {
                LastBlockingWriteZeroes = len;
                return Result<Unit, StreamError>.FromOk(Unit.Value);
            }
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
                new StreamBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-zeroes", (uint)handle)!);
            Assert.Equal(8UL, stream.LastWriteZeroes);
            Assert.Equal(16UL, stream.LastBlockingWriteZeroes);
        }
    }
}
