// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.WASI.Preview3.Filesystem;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice C coverage: the
    /// <c>wasi:filesystem/types@0.3.0-rc-2026-03-15</c>
    /// + <c>wasi:filesystem/preopens</c> surface area — type
    /// definitions, <see cref="IDescriptor"/> + <see cref="IPreopens"/>
    /// interfaces, default System.IO-backed impls. The
    /// wire-level canon-async binding ships in the next slice
    /// once the descriptor-resource lifecycle wiring is settled.
    /// </summary>
    public class FilesystemTests : IDisposable
    {
        private readonly string _tempRoot;

        public FilesystemTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-fs-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }

        private Descriptor RootDescriptor() =>
            new Descriptor(_tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.Write
                    | DescriptorFlags.MutateDirectory);

        // ---- FilesystemTypes ------------------------------------------

        [Fact]
        public void DescriptorType_static_factories_set_kind_correctly()
        {
            Assert.Equal(DescriptorType.Tag.Directory,
                DescriptorType.Directory.Kind);
            Assert.Equal(DescriptorType.Tag.RegularFile,
                DescriptorType.RegularFile.Kind);
            Assert.Equal(DescriptorType.Tag.Other,
                DescriptorType.Other("custom").Kind);
            Assert.Equal("custom",
                DescriptorType.Other("custom").OtherPayload);
        }

        [Fact]
        public void DescriptorFlags_bit_positions_match_wit_declaration_order()
        {
            // The canon-ABI lowering writes flags as a bitfield
            // in WIT declaration order. Pin the values so a
            // reorder bug surfaces immediately.
            Assert.Equal(0x01u, (uint)DescriptorFlags.Read);
            Assert.Equal(0x02u, (uint)DescriptorFlags.Write);
            Assert.Equal(0x04u, (uint)DescriptorFlags.FileIntegritySync);
            Assert.Equal(0x08u, (uint)DescriptorFlags.DataIntegritySync);
            Assert.Equal(0x10u, (uint)DescriptorFlags.RequestedWriteSync);
            Assert.Equal(0x20u, (uint)DescriptorFlags.MutateDirectory);
        }

        [Fact]
        public void OpenFlags_bit_positions_match_wit_declaration_order()
        {
            Assert.Equal(0x01u, (uint)OpenFlags.Create);
            Assert.Equal(0x02u, (uint)OpenFlags.Directory);
            Assert.Equal(0x04u, (uint)OpenFlags.Exclusive);
            Assert.Equal(0x08u, (uint)OpenFlags.Truncate);
        }

        [Fact]
        public void NewTimestamp_factory_methods_preserve_kind()
        {
            Assert.Equal(NewTimestamp.Tag.NoChange,
                NewTimestamp.NoChange.Kind);
            Assert.Equal(NewTimestamp.Tag.Now,
                NewTimestamp.Now.Kind);
            var ts = NewTimestamp.Timestamp(
                new Clocks.Instant(42, 123));
            Assert.Equal(NewTimestamp.Tag.Timestamp, ts.Kind);
            Assert.Equal(42L, ts.TimestampValue.Seconds);
            Assert.Equal(123u, ts.TimestampValue.Nanoseconds);
        }

        [Fact]
        public void FilesystemException_carries_other_payload_when_kind_is_other()
        {
            var ex = new FilesystemException(
                ErrorCode.Other, "weird");
            Assert.Equal(ErrorCode.Other, ex.Code);
            Assert.Equal("weird", ex.OtherPayload);
        }

        [Fact]
        public void FilesystemException_other_payload_null_for_typed_codes()
        {
            var ex = new FilesystemException(
                ErrorCode.NoEntry, "missing file");
            Assert.Equal(ErrorCode.NoEntry, ex.Code);
            Assert.Null(ex.OtherPayload);
        }

        // ---- Descriptor: get-type / get-flags ------------------------

        [Fact]
        public void Descriptor_get_type_returns_directory_for_dir()
        {
            var d = RootDescriptor();
            Assert.Equal(DescriptorType.Tag.Directory, d.GetType_().Kind);
        }

        [Fact]
        public void Descriptor_get_type_returns_regular_file_for_file()
        {
            File.WriteAllText(Path.Combine(_tempRoot, "x.txt"), "hi");
            var d = new Descriptor(
                Path.Combine(_tempRoot, "x.txt"), _tempRoot,
                DescriptorFlags.Read);
            Assert.Equal(DescriptorType.Tag.RegularFile, d.GetType_().Kind);
        }

        [Fact]
        public void Descriptor_get_flags_returns_constructed_flags()
        {
            var d = new Descriptor(_tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.MutateDirectory);
            Assert.Equal(
                DescriptorFlags.Read | DescriptorFlags.MutateDirectory,
                d.GetFlags());
        }

        // ---- Descriptor: stat ----------------------------------------

        [Fact]
        public async Task Descriptor_stat_returns_directory_metadata_for_root()
        {
            var d = RootDescriptor();
            var stat = await d.StatAsync();
            Assert.Equal(DescriptorType.Tag.Directory, stat.Type.Kind);
            Assert.NotNull(stat.DataModificationTimestamp);
        }

        [Fact]
        public async Task Descriptor_stat_at_returns_regular_file_metadata()
        {
            var name = "stat-target.bin";
            File.WriteAllBytes(
                Path.Combine(_tempRoot, name), new byte[] { 1, 2, 3, 4, 5 });

            var d = RootDescriptor();
            var stat = await d.StatAtAsync(
                PathFlags.None, name);
            Assert.Equal(DescriptorType.Tag.RegularFile, stat.Type.Kind);
            Assert.Equal(5UL, stat.Size.Value);
        }

        [Fact]
        public async Task Descriptor_stat_at_no_entry_throws_no_entry()
        {
            var d = RootDescriptor();
            var ex = await Assert.ThrowsAsync<FilesystemException>(
                () => d.StatAtAsync(PathFlags.None, "absent.txt"));
            Assert.Equal(ErrorCode.NoEntry, ex.Code);
        }

        // ---- Descriptor: open-at + read/write via stream -------------

        [Fact]
        public async Task Descriptor_open_at_create_creates_a_new_file()
        {
            var d = RootDescriptor();
            await d.OpenAtAsync(PathFlags.None, "fresh.txt",
                OpenFlags.Create, DescriptorFlags.Read | DescriptorFlags.Write);
            Assert.True(File.Exists(Path.Combine(_tempRoot, "fresh.txt")));
        }

        [Fact]
        public async Task Descriptor_open_at_exclusive_existing_throws_exist()
        {
            File.WriteAllText(Path.Combine(_tempRoot, "x.txt"), "");
            var d = RootDescriptor();
            var ex = await Assert.ThrowsAsync<FilesystemException>(
                () => d.OpenAtAsync(PathFlags.None, "x.txt",
                    OpenFlags.Create | OpenFlags.Exclusive,
                    DescriptorFlags.Read));
            Assert.Equal(ErrorCode.Exist, ex.Code);
        }

        [Fact]
        public async Task Descriptor_read_via_stream_yields_file_bytes()
        {
            var name = "read.txt";
            var content = System.Text.Encoding.UTF8.GetBytes("hello-fs");
            await File.WriteAllBytesAsync(
                Path.Combine(_tempRoot, name), content);

            var dispatcher = new AsyncDispatcher();
            var d = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Read);

            var (streamHandle, futureHandle, completion) =
                d.ReadViaStream(dispatcher, new FileSize(0));
            await completion;

            // Drain the stream and confirm contents.
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buffer);
            var drained = new System.Collections.Generic.List<byte>();
            while (buffer.Reader.TryRead(out var b)) drained.Add(b);
            Assert.Equal(content, drained.ToArray());
        }

        [Fact]
        public async Task Descriptor_write_via_stream_persists_bytes_to_file()
        {
            var name = "write.txt";
            var dispatcher = new AsyncDispatcher();
            var d = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Write);

            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var (futureHandle, completion) =
                d.WriteViaStream(dispatcher, streamHandle, new FileSize(0));

            // Guest writes 5 bytes, closes the writer side.
            foreach (var b in new byte[] { 0x57, 0x52, 0x49, 0x54, 0x45 })
                dispatcher.StreamTryWrite(streamHandle, b);
            dispatcher.StreamDropWritable(streamHandle);
            dispatcher.StreamDropReadable(streamHandle);

            await completion;
            Assert.Equal(new byte[] { 0x57, 0x52, 0x49, 0x54, 0x45 },
                File.ReadAllBytes(Path.Combine(_tempRoot, name)));
        }

        // ---- Descriptor: path operations + sandbox -------------------

        [Fact]
        public async Task Descriptor_create_directory_at_creates_subdir()
        {
            var d = RootDescriptor();
            await d.CreateDirectoryAtAsync("sub");
            Assert.True(Directory.Exists(Path.Combine(_tempRoot, "sub")));
        }

        [Fact]
        public async Task Descriptor_unlink_file_at_deletes_file()
        {
            var name = "doomed.txt";
            File.WriteAllText(Path.Combine(_tempRoot, name), "");
            var d = RootDescriptor();
            await d.UnlinkFileAtAsync(name);
            Assert.False(File.Exists(Path.Combine(_tempRoot, name)));
        }

        [Fact]
        public async Task Descriptor_unlink_file_at_directory_throws_is_directory()
        {
            Directory.CreateDirectory(Path.Combine(_tempRoot, "dir"));
            var d = RootDescriptor();
            var ex = await Assert.ThrowsAsync<FilesystemException>(
                () => d.UnlinkFileAtAsync("dir"));
            Assert.Equal(ErrorCode.IsDirectory, ex.Code);
        }

        [Fact]
        public async Task Descriptor_path_escape_via_dotdot_throws_not_permitted()
        {
            var d = RootDescriptor();
            // Try to escape the sandbox with ../../etc/passwd.
            var ex = await Assert.ThrowsAsync<FilesystemException>(
                () => d.StatAtAsync(
                    PathFlags.None, "../../etc/passwd"));
            Assert.Equal(ErrorCode.NotPermitted, ex.Code);
        }

        // ---- Descriptor: metadata + identity -------------------------

        [Fact]
        public async Task Descriptor_is_same_object_is_true_for_same_path()
        {
            var d1 = RootDescriptor();
            var d2 = RootDescriptor();
            Assert.True(await d1.IsSameObjectAsync(d2));
        }

        [Fact]
        public async Task Descriptor_is_same_object_is_false_for_different_paths()
        {
            Directory.CreateDirectory(Path.Combine(_tempRoot, "a"));
            Directory.CreateDirectory(Path.Combine(_tempRoot, "b"));
            var d1 = new Descriptor(
                Path.Combine(_tempRoot, "a"), _tempRoot,
                DescriptorFlags.Read);
            var d2 = new Descriptor(
                Path.Combine(_tempRoot, "b"), _tempRoot,
                DescriptorFlags.Read);
            Assert.False(await d1.IsSameObjectAsync(d2));
        }

        [Fact]
        public async Task Descriptor_metadata_hash_stable_across_calls()
        {
            var name = "stable.bin";
            File.WriteAllBytes(
                Path.Combine(_tempRoot, name), new byte[] { 1, 2, 3 });
            var d = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Read);
            var h1 = await d.MetadataHashAsync();
            var h2 = await d.MetadataHashAsync();
            Assert.Equal(h1.Lower, h2.Lower);
            Assert.Equal(h1.Upper, h2.Upper);
        }

        // ---- DirectoryPreopens + IPreopens ----------------------------

        [Fact]
        public void DirectoryPreopens_from_host_paths_creates_descriptors()
        {
            var pre = DirectoryPreopens.FromHostPaths(
                (_tempRoot, "/sandbox"));
            var dirs = pre.GetDirectories();
            Assert.Single(dirs);
            Assert.Equal("/sandbox", dirs[0].Path);
            Assert.IsType<Descriptor>(dirs[0].Descriptor);
        }

        [Fact]
        public void DirectoryPreopens_empty_returns_empty_list()
        {
            var pre = DirectoryPreopens.FromHostPaths();
            Assert.Empty(pre.GetDirectories());
        }

        [Fact]
        public void Host_default_construct_provides_empty_preopens()
        {
            var host = new WasiPreview3Host();
            Assert.Empty(host.Preopens.GetDirectories());
        }

        [Fact]
        public void Host_builder_threads_preopens_override()
        {
            var custom = DirectoryPreopens.FromHostPaths(
                (_tempRoot, "/work"));
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder { Preopens = custom });
            Assert.Same(custom, host.Preopens);
            Assert.Single(host.Preopens.GetDirectories());
        }
    }
}
