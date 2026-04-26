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
    public class InputStreamTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        /// <summary>InputStream returning a fixed buffer slice
        /// — useful for asserting on length round-trips.</summary>
        public sealed class CannedInputStream : InputStream
        {
            private readonly byte[] _data;
            public CannedInputStream(byte[] data) { _data = data; }

            public override byte[] Read(ulong len)
            {
                int n = (int)System.Math.Min(len, (ulong)_data.Length);
                var slice = new byte[n];
                System.Array.Copy(_data, 0, slice, 0, n);
                return slice;
            }
        }

        [Fact]
        public void Read_returns_bytes_via_canon_lower_list_u8()
        {
            // Fixture: try-read(handle, len) calls
            // input-stream.read(handle, len) into retArea, then
            // returns the byte count read (from retArea+8 — the
            // (ptr, len) pair in result<list<u8>, ...>'s Ok
            // payload). Stub returns "abcdefghij" (10 bytes);
            // try-read with len=4 should report 4.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-input-stream-component", "sr.component.wasm"));
            var resources = new ResourceContext();
            var stream = new CannedInputStream(
                System.Text.Encoding.ASCII.GetBytes("abcdefghij"));
            int handle = resources.TableFor(typeof(InputStream))
                .Allocate(stream);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<InputStream>(
                    "wasi:io/streams@0.2.3", resources);
            });

            var n = (uint)ci.Invoke("try-read", (uint)handle, 4UL)!;
            Assert.Equal(4u, n);
            Assert.Equal(0, resources.TableFor(typeof(InputStream)).Count);
        }
    }
}
