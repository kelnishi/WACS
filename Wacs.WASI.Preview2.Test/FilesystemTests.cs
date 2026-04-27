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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke("ask-set-size",
                (uint)handle, 4096UL)!);
            Assert.Equal(4096UL, desc.LastSetSize);
        }

        private sealed class ReadlinkDescriptor : Descriptor
        {
            public string LastPath = "";
            public ReadlinkDescriptor() : base("/synthetic") { }
            public override string ReadlinkAt(string path)
            {
                LastPath = path;
                return "/links/target/" + path;
            }
        }

        [Fact]
        public void ReadlinkAt_returns_string_through_result_wrapper()
        {
            // Fixture: ask-readlink(handle) → string. WAT calls
            // readlink-at(handle, "src") and returns the Ok
            // payload string. Stub returns "/links/target/src".
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-readlink-component", "fsreadlink.component.wasm"));
            var resources = new ResourceContext();
            var desc = new ReadlinkDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal("/links/target/src",
                ci.Invoke("ask-readlink", (uint)handle));
            Assert.Equal("src", desc.LastPath);
        }

        private sealed class FlagsDescriptor : Descriptor
        {
            private readonly DescriptorFlags _flags;
            public FlagsDescriptor(DescriptorFlags flags)
                : base("/synthetic") { _flags = flags; }
            public override DescriptorFlags GetFlags() => _flags;
        }

        private sealed class StubFsErrCode : IFilesystemErrorCode
        {
            [WasiOptionalReturn]
            [WasiMethodName("filesystem-error-code")]
            public FilesystemErrorCode? FilesystemErrorCode(
                Wacs.WASI.Preview2.Io.Error err)
            {
                // Classify any Error whose message starts
                // with "fs:" as a filesystem error keyed by
                // the byte value after the colon. Otherwise
                // null = not a filesystem error.
                var msg = err.ToDebugString();
                if (msg.StartsWith("fs:",
                    System.StringComparison.Ordinal)
                    && byte.TryParse(msg.Substring(3), out var b))
                    return (FilesystemErrorCode)b;
                return null;
            }
        }

        [Fact]
        public void FilesystemErrorCode_returns_some_when_classified()
        {
            // Fixture: ask-errcode(err-handle) calls
            // filesystem-error-code(err); packs (option-disc,
            // payload-byte) as u32 (disc in bits 0-7, byte
            // in bits 8-15). Stub recognizes "fs:20" → Some(20).
            // Expected: (1) | (20 << 8) = 0x1401.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-errcode-component", "fserrcode.component.wasm"));
            var resources = new ResourceContext();
            var fec = new StubFsErrCode();
            var err = new Wacs.WASI.Preview2.Io.Error("fs:20");
            int handle = resources.TableFor(
                typeof(Wacs.WASI.Preview2.Io.Error)).Allocate(err);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new FilesystemBindings(resources, errorCode: fec)
                    .BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0x1401u, (uint)ci.Invoke(
                "ask-errcode", (uint)handle)!);
        }

        [Fact]
        public void FilesystemErrorCode_returns_none_when_not_classified()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-errcode-component", "fserrcode.component.wasm"));
            var resources = new ResourceContext();
            var fec = new StubFsErrCode();
            var err = new Wacs.WASI.Preview2.Io.Error("network");
            int handle = resources.TableFor(
                typeof(Wacs.WASI.Preview2.Io.Error)).Allocate(err);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new FilesystemBindings(resources, errorCode: fec)
                    .BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });

            // None disc=0 + payload byte stays 0 → 0.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-errcode", (uint)handle)!);
        }

        [Fact]
        public void GetFlags_writes_flags_enum_as_u8_when_flag_count_le_8()
        {
            // Fixture: ask-flags(handle) calls get-flags and
            // reads the byte at retArea+1. Wire form for 6
            // declared flags is u8, align 1 — total retArea
            // 2 bytes. Stub returns Read | Write = 0b11 = 3.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-getflags-component", "fsgetflags.component.wasm"));
            var resources = new ResourceContext();
            var desc = new FlagsDescriptor(
                DescriptorFlags.Read | DescriptorFlags.Write);
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(3u, (uint)ci.Invoke(
                "ask-flags", (uint)handle)!);
        }

        private sealed class AccessAtDescriptor : Descriptor
        {
            public AccessType? LastType;
            public string? LastPath;
            public AccessAtDescriptor() : base("/synthetic") { }
            public override void AccessAt(PathFlags pathFlags,
                string path, AccessType type)
            {
                LastPath = path;
                LastType = type;
            }
        }

        [Fact]
        public void AccessAt_decodes_access_type_variant_param()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-accessat-component", "fsaccessat.component.wasm"));
            var resources = new ResourceContext();
            var desc = new AccessAtDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            // First call: access(readable | writable)
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-access-readable", (uint)handle)!);
            Assert.IsType<AccessTypeAccess>(desc.LastType);
            var ata = (AccessTypeAccess)desc.LastType!;
            Assert.Equal(AccessModes.Readable | AccessModes.Writable,
                ata.Modes);
            Assert.Equal("f", desc.LastPath);

            // Second call: exists
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-access-exists", (uint)handle)!);
            Assert.IsType<AccessTypeExists>(desc.LastType);
        }

        private sealed class SetTimesDescriptor : Descriptor
        {
            public NewTimestamp? LastAtime;
            public NewTimestamp? LastMtime;
            public SetTimesDescriptor() : base("/synthetic") { }
            public override void SetTimes(NewTimestamp atime,
                NewTimestamp mtime)
            {
                LastAtime = atime;
                LastMtime = mtime;
            }
        }

        [Fact]
        public void SetTimes_decodes_new_timestamp_variant_params()
        {
            // Fixture: ask-set-times(handle) calls
            //   set-times(NoChange, Timestamp(2000s, 0))
            // by passing 6 wire slots for the two variants
            // (3 each: disc + i64 sec + i32 nanos). Stub
            // captures both args; test asserts the case
            // dispatch + datetime payload.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-settimes-component", "fssettimes.component.wasm"));
            var resources = new ResourceContext();
            var desc = new SetTimesDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-times", (uint)handle)!);
            Assert.IsType<NewTimestampNoChange>(desc.LastAtime);
            Assert.IsType<NewTimestampTimestamp>(desc.LastMtime);
            var ts = (NewTimestampTimestamp)desc.LastMtime!;
            Assert.Equal(2000UL, ts.Value.Seconds);
            Assert.Equal(0u, ts.Value.Nanoseconds);
        }

        private sealed class StatDescriptor : Descriptor
        {
            public StatDescriptor() : base("/synthetic") { }
            public override DescriptorStat Stat()
                => new DescriptorStat(
                    DescriptorType.RegularFile,
                    linkCount: 1,
                    size: 4096,
                    dataAccessTimestamp: new Wacs.WASI.Preview2.Clocks
                        .Datetime { Seconds = 1700000000, Nanoseconds = 0 },
                    dataModificationTimestamp: new Wacs.WASI.Preview2.Clocks
                        .Datetime { Seconds = 1700100000, Nanoseconds = 500 },
                    statusChangeTimestamp: null);
        }

        [Fact]
        public void Stat_writes_descriptor_stat_record_with_option_datetime_fields()
        {
            // Fixture: ask-stat-{type, size, mtime-disc,
            // mtime} each call descriptor.stat and read
            // different fields out of the 96-byte record.
            // Stub returns RegularFile, size=4096,
            // mtime=Some(1700100000s), ctime=None.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-stat-component", "fsstat.component.wasm"));
            var resources = new ResourceContext();
            var desc = new StatDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal((uint)DescriptorType.RegularFile,
                (uint)ci.Invoke("ask-stat-type", (uint)handle)!);
            Assert.Equal(4096UL,
                (ulong)ci.Invoke("ask-stat-size", (uint)handle)!);
            Assert.Equal(1u,
                (uint)ci.Invoke("ask-stat-mtime-disc", (uint)handle)!);
            Assert.Equal(1700100000UL,
                (ulong)ci.Invoke("ask-stat-mtime", (uint)handle)!);
        }

        private sealed class FixedDirDescriptor : Descriptor
        {
            public FixedDirDescriptor() : base("/synthetic") { }
            public override DirectoryEntryStream ReadDirectory()
                => new DirectoryEntryStream(new[] {
                    new DirectoryEntry(DescriptorType.RegularFile,
                        "first.txt"),
                    new DirectoryEntry(DescriptorType.Directory,
                        "subdir"),
                });
        }

        [Fact]
        public void ReadDirectory_yields_entry_stream_with_first_record()
        {
            // Fixture: ask-readdir(handle) → read-directory →
            // read-directory-entry once. Packs (option-disc,
            // type, name-len, first-name-byte) into a u32.
            // Stub directory has 2 entries; first is a
            // regular-file named "first.txt".
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-readdir-component", "fsreaddir.component.wasm"));
            var resources = new ResourceContext();
            var desc = new FixedDirDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            uint result = (uint)ci.Invoke(
                "ask-readdir", (uint)handle)!;
            // option disc: 1 (Some)
            Assert.Equal(1u, result & 0xff);
            // type: 6 (RegularFile)
            Assert.Equal((uint)DescriptorType.RegularFile,
                (result >> 8) & 0xff);
            // name-len: 9 ("first.txt")
            Assert.Equal(9u, (result >> 16) & 0xff);
            // first byte: 'f' = 0x66
            Assert.Equal(0x66u, (result >> 24) & 0xff);
        }

        private sealed class FixedHashDescriptor : Descriptor
        {
            public FixedHashDescriptor() : base("/synthetic") { }
            public override MetadataHashValue MetadataHash()
                => new MetadataHashValue(0x1122334455667788UL,
                                         0x99AABBCCDDEEFF00UL);
        }

        [Fact]
        public void MetadataHash_writes_record_of_two_u64_at_aligned_offset()
        {
            // Fixture: ask-meta-lower(handle) +
            // ask-meta-upper(handle) call descriptor.metadata-hash
            // and read the two u64 fields at retArea+8 (lower)
            // and retArea+16 (upper). Stub returns fixed
            // 0x1122..7788 / 0x99AA..FF00; assert both lift back
            // through canon-lift unchanged.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-metahash-component", "fsmetahash.component.wasm"));
            var resources = new ResourceContext();
            var desc = new FixedHashDescriptor();
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0x1122334455667788UL,
                (ulong)ci.Invoke("ask-meta-lower", (uint)handle)!);
            Assert.Equal(0x99AABBCCDDEEFF00UL,
                (ulong)ci.Invoke("ask-meta-upper", (uint)handle)!);
        }

        private sealed class FixedReadDescriptor : Descriptor
        {
            private readonly byte[] _content;
            public FixedReadDescriptor(byte[] content)
                : base("/synthetic") { _content = content; }
            public override (byte[], bool) Read(ulong length, ulong offset)
            {
                int o = (int)System.Math.Min(offset, (ulong)_content.Length);
                int n = (int)System.Math.Min(
                    System.Math.Min(length, (ulong)int.MaxValue),
                    (ulong)(_content.Length - o));
                var slice = new byte[n];
                System.Array.Copy(_content, o, slice, 0, n);
                bool eof = (o + n) >= _content.Length;
                return (slice, eof);
            }
        }

        [Fact]
        public void Read_direct_returns_tuple_list_u8_bool_through_result_wrapper()
        {
            // Fixture: ask-read(handle) calls
            // descriptor.read(handle, 8, 0) and reads the
            // (list-len, eof) at retArea+8/+12 — packs them
            // as (len << 1) | eof.
            // Stub content is "ABCDE" (5 bytes); read(8, 0)
            // yields all 5 + EOF=1 → (5 << 1) | 1 = 11.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-readdirect-component", "fsreaddirect.component.wasm"));
            var resources = new ResourceContext();
            var desc = new FixedReadDescriptor(
                System.Text.Encoding.UTF8.GetBytes("ABCDE"));
            int handle = resources.TableFor(typeof(Descriptor))
                .Allocate(desc);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(11u, (uint)ci.Invoke("ask-read", (uint)handle)!);
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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke("ask-mutate", (uint)handle)!);
            Assert.Equal(new[] { "child" }, desc.Created);
            Assert.Equal(new[] { "child" }, desc.Removed);
            Assert.Equal(new[] { "child" }, desc.Unlinked);
        }

        private sealed class RenameDescriptor : Descriptor
        {
            public string LastOldPath = "";
            public string LastNewPath = "";
            public Descriptor? LastNewDescriptor;
            public RenameDescriptor() : base("/synthetic") { }
            public override void RenameAt(string oldPath,
                Descriptor newDescriptor, string newPath)
            {
                LastOldPath = oldPath;
                LastNewPath = newPath;
                LastNewDescriptor = newDescriptor;
            }
        }

        [Fact]
        public void RenameAt_resolves_borrow_descriptor_with_two_strings()
        {
            // Fixture: ask-rename(self, newdesc) →
            // descriptor.rename-at(self, "src", newdesc, "dst").
            // Stub records all three params; asserts paths +
            // identity of the new descriptor.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-fs-rename-component", "fsrename.component.wasm"));
            var resources = new ResourceContext();
            var self = new RenameDescriptor();
            var other = new Descriptor("/other");
            int hSelf = resources.TableFor(typeof(Descriptor))
                .Allocate(self);
            int hOther = resources.TableFor(typeof(Descriptor))
                .Allocate(other);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new FilesystemBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-rename", (uint)hSelf, (uint)hOther)!);
            Assert.Equal("src", self.LastOldPath);
            Assert.Equal("dst", self.LastNewPath);
            Assert.Same(other, self.LastNewDescriptor);
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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime));

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
                new FilesystemBindings(resources).BindToRuntime(runtime);
                new StreamBindings(resources).BindToRuntime(runtime);
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
