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

        private sealed class TypedDescriptor : Descriptor
        {
            private readonly DescriptorType _type;
            public TypedDescriptor(DescriptorType type)
                : base("/synthetic") { _type = type; }
            public override DescriptorType GetDescriptorType() => _type;
        }

        private sealed class TrackingDescriptor : Descriptor
        {
            public int SyncCalls;
            public ulong LastSetSize;
            public TrackingDescriptor() : base("/synthetic") { }
            public override void Sync() => SyncCalls++;
            public override void SetSize(ulong size) => LastSetSize = size;
        }

        [Fact]
        public void Sync_returns_Ok_disc_zero_through_result_void_wrapper()
        {
            // Component imports descriptor.sync, exports
            // ask-sync(handle) → u32 returning the outer disc.
            // Always-Ok: 0.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-sync-component", "fssync.component.wasm"));
            var resources = new ResourceContext();
            var desc = new TrackingDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke("ask-sync", (uint)handle)!);
            Assert.Equal(1, desc.SyncCalls);
        }

        [Fact]
        public void SetSize_threads_u64_through_result_void_wrapper()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-sync-component", "fssync.component.wasm"));
            var resources = new ResourceContext();
            var desc = new TrackingDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke("ask-set-size",
                (uint)handle, 4096UL)!);
            Assert.Equal(4096UL, desc.LastSetSize);
        }

        [Fact]
        public void GetType_returns_enum_value_through_result_wrapper()
        {
            // Component imports descriptor.get-type, exports
            // ask-type(handle) → u32 reading the inner enum
            // disc from retArea+1. Stub returns Directory (3);
            // expected: 3.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-type-component", "fstype.component.wasm"));
            var resources = new ResourceContext();
            var desc = new TypedDescriptor(DescriptorType.Directory);
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal((uint)DescriptorType.Directory,
                (uint)ci.Invoke("ask-type", (uint)handle)!);
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
