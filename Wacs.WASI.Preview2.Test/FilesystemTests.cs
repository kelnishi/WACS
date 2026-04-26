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

        private sealed class CapturingDescriptor : Descriptor
        {
            public byte[] LastWritten = System.Array.Empty<byte>();
            public ulong LastOffset;
            public CapturingDescriptor() : base("/synthetic") { }
            public override ulong Write(byte[] buffer, ulong offset)
            {
                LastWritten = buffer;
                LastOffset = offset;
                return (ulong)buffer.Length;
            }
        }

        [Fact]
        public void Write_threads_byte_array_param_and_u64_return()
        {
            // Component: ask-write(handle) calls
            // descriptor.write(handle, "hello", 0) and reads
            // back the u64 count from retArea+8 (the Ok payload
            // of result<u64, error-code>). Stub captures the
            // bytes; test asserts both the count returned and
            // the captured bytes.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-write-component", "fswrite.component.wasm"));
            var resources = new ResourceContext();
            var desc = new CapturingDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(5UL, (ulong)ci.Invoke(
                "ask-write", (uint)handle)!);
            Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l',
                (byte)'l', (byte)'o' }, desc.LastWritten);
            Assert.Equal(0UL, desc.LastOffset);
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

        private sealed class MutatingDescriptor : Descriptor
        {
            public System.Collections.Generic.List<string> Created
                = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> Removed
                = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> Unlinked
                = new System.Collections.Generic.List<string>();
            public MutatingDescriptor() : base("/synthetic") { }
            public override void CreateDirectoryAt(string path)
                => Created.Add(path);
            public override void RemoveDirectoryAt(string path)
                => Removed.Add(path);
            public override void UnlinkFileAt(string path)
                => Unlinked.Add(path);
        }

        [Fact]
        public void MutatingPath_ops_thread_string_through_void_result_wrapper()
        {
            // Component imports descriptor.{create,remove,unlink}-*
            // and sums the outer disc bytes from each retArea.
            // Always-Ok = 0; expected return: 0. Stub records
            // each path so we can also assert the string round-
            // tripped correctly.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-mutate-component", "fsmutate.component.wasm"));
            var resources = new ResourceContext();
            var desc = new MutatingDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke("ask-mutate", (uint)handle)!);
            Assert.Equal(new[] { "child" }, desc.Created);
            Assert.Equal(new[] { "child" }, desc.Removed);
            Assert.Equal(new[] { "child" }, desc.Unlinked);
        }

        private sealed class PathDescriptor : Descriptor
        {
            public PathDescriptor(string path) : base(path) { }
        }

        [Fact]
        public void IsSameObject_resolves_borrow_descriptor_param()
        {
            // Fixture: ask-same(self, other) →
            // descriptor.is-same-object(self, other) returning
            // bool as u32. Stub uses the path-equality default;
            // descriptors with the same path → 1, different →
            // 0.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-same-component", "fssame.component.wasm"));
            var resources = new ResourceContext();
            var a = new PathDescriptor("/data/x");
            var b = new PathDescriptor("/data/x");
            var c = new PathDescriptor("/data/y");
            var table = resources.TableFor(typeof(Descriptor));
            int hA = table.Allocate(a);
            int hB = table.Allocate(b);
            int hC = table.Allocate(c);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-same", (uint)hA, (uint)hB)!);
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-same", (uint)hA, (uint)hC)!);
        }

        private sealed class SymlinkDescriptor : Descriptor
        {
            public string LastOldPath = "";
            public string LastNewPath = "";
            public SymlinkDescriptor() : base("/synthetic") { }
            public override void SymlinkAt(string oldPath, string newPath)
            {
                LastOldPath = oldPath;
                LastNewPath = newPath;
            }
        }

        [Fact]
        public void SymlinkAt_threads_two_strings_through_void_result_wrapper()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-symlink-component", "fssymlink.component.wasm"));
            var resources = new ResourceContext();
            var desc = new SymlinkDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-symlink", (uint)handle)!);
            Assert.Equal("src", desc.LastOldPath);
            Assert.Equal("dst", desc.LastNewPath);
        }

        private sealed class OpenAtDescriptor : Descriptor
        {
            public string LastPath = "";
            public PathFlags LastPathFlags;
            public OpenFlags LastOpenFlags;
            public DescriptorFlags LastDescriptorFlags;
            public OpenAtDescriptor() : base("/synthetic") { }
            public override Descriptor OpenAt(PathFlags pathFlags,
                string path, OpenFlags openFlags,
                DescriptorFlags descriptorFlags)
            {
                LastPath = path;
                LastPathFlags = pathFlags;
                LastOpenFlags = openFlags;
                LastDescriptorFlags = descriptorFlags;
                return new Descriptor("/synthetic/" + path);
            }
        }

        [Fact]
        public void OpenAt_threads_string_param_and_returns_resource_handle()
        {
            // Component: ask-open(handle) calls
            // descriptor.open-at(handle, 0, "child", Create=1, 0)
            // and returns the i32 handle from retArea+4 (Ok
            // payload of result<own<descriptor>, error-code>).
            // Drops the returned handle so resource-table count
            // is clean. Test asserts: returned handle is non-
            // zero, host received the right path + flags, and
            // the returned descriptor's table is empty after
            // drop.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-open-component", "fsopen.component.wasm"));
            var resources = new ResourceContext();
            var desc = new OpenAtDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            uint returned = (uint)ci.Invoke(
                "ask-open", (uint)handle)!;
            Assert.NotEqual(0u, returned);
            Assert.Equal("child", desc.LastPath);
            Assert.Equal(OpenFlags.Create, desc.LastOpenFlags);
            Assert.Equal(PathFlags.None, desc.LastPathFlags);
            Assert.Equal(DescriptorFlags.None, desc.LastDescriptorFlags);
            // After drop only the original descriptor remains.
            Assert.Equal(1, resources.TableFor(typeof(Descriptor)).Count);
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
