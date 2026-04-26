// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// End-to-end test: component receives a descriptor
    /// handle, calls descriptor.read-via-stream(0) to get an
    /// input-stream handle, then reads bytes through it.
    /// Exercises the descriptor → stream bridge — the
    /// bedrock of WASI filesystem access.
    /// </summary>
    public class FilesystemTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        /// <summary>Test descriptor that synthesizes file
        /// content from a fixed string instead of going to
        /// disk — keeps the test hermetic.</summary>
        private sealed class StringDescriptor : Descriptor
        {
            private readonly byte[] _bytes;
            public StringDescriptor(string content)
                : base("/synthetic")
            {
                _bytes = System.Text.Encoding.UTF8.GetBytes(content);
            }

            public override InputStream ReadViaStream(ulong offset)
            {
                int n = (int)System.Math.Min(offset, (ulong)_bytes.Length);
                var slice = new byte[_bytes.Length - n];
                System.Array.Copy(_bytes, n, slice, 0, slice.Length);
                return new InMemoryInputStream(slice);
            }
        }

        private sealed class InMemoryInputStream : InputStream
        {
            private readonly byte[] _bytes;
            private int _pos;
            public InMemoryInputStream(byte[] bytes) { _bytes = bytes; }

            public override byte[] Read(ulong len)
            {
                int avail = _bytes.Length - _pos;
                int n = (int)System.Math.Min(
                    System.Math.Min(len, (ulong)int.MaxValue),
                    (ulong)avail);
                var slice = new byte[n];
                System.Array.Copy(_bytes, _pos, slice, 0, n);
                _pos += n;
                return slice;
            }

            public override byte[] BlockingRead(ulong len) => Read(len);
        }

        [Fact]
        public void ReadViaStream_yields_input_stream_then_read_returns_bytes()
        {
            // Component imports descriptor + input-stream
            // resources. read-first(handle, len) calls
            // descriptor.read-via-stream(handle, 0) → gets an
            // input-stream handle, then input-stream.read(len)
            // → byte[]. Returns the byte count actually read.
            //
            // Stub descriptor returns content "filesystem
            // chain works" (24 bytes). With len=10, read-first
            // returns 10.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-read-component", "fsread.component.wasm"));
            var resources = new ResourceContext();
            var desc = new StringDescriptor("filesystem chain works");
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
                runtime.BindWasiResource<InputStream>(
                    "wasi:io/streams@0.2.3", resources);
            });

            Assert.Equal(10u, (uint)ci.Invoke(
                "read-first", (uint)handle, 10UL)!);
            // After drop the input-stream handle should be
            // released; descriptor still in the table since
            // we didn't drop it.
            Assert.Equal(0, resources.TableFor(typeof(InputStream)).Count);
        }
    }
}
