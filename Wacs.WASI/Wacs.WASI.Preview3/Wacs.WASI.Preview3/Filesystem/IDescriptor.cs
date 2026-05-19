// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;

namespace Wacs.WASI.Preview3.Filesystem
{
    /// <summary>
    /// Host interface for the WIT
    /// <c>resource descriptor</c> in
    /// <c>wasi:filesystem/types@0.3.0-rc-2026-03-15</c>. A
    /// descriptor refers to a filesystem object (file,
    /// directory, named pipe, …) on which filesystem calls may
    /// be made.
    ///
    /// <para><b>Async surface.</b> Most descriptor methods are
    /// <c>async func</c> in the WIT and surface here as
    /// <see cref="Task"/>-returning methods. The
    /// <c>read-via-stream</c> / <c>write-via-stream</c> /
    /// <c>append-via-stream</c> / <c>read-directory</c> shapes
    /// return stream + future handles via the canon-async
    /// dispatcher; those are the methods that integrate most
    /// directly with the dispatcher and are exposed here
    /// returning the (streamHandle, futureHandle, completion)
    /// tuple shape established by <c>IStdin.ReadViaStream</c>.</para>
    ///
    /// <para><b>Error reporting.</b> Methods returning
    /// <c>result&lt;X, error-code&gt;</c> in the WIT throw
    /// <see cref="FilesystemException"/> on the err path; the
    /// canon-async binding catches and lowers to the wire
    /// error-code value. Methods returning <c>result&lt;_,
    /// error-code&gt;</c> (no payload on success) likewise
    /// throw on err and return <see cref="Task.CompletedTask"/>
    /// on success.</para>
    /// </summary>
    public interface IDescriptor
    {
        /// <summary><c>read-via-stream: func(offset: filesize) -&gt;
        /// tuple&lt;stream&lt;u8&gt;, future&lt;result&lt;_, error-code&gt;&gt;&gt;</c>.
        /// Allocates a host stream + future via the dispatcher,
        /// kicks off the background read loop, returns both
        /// handles + a CLR <see cref="Task"/> that completes
        /// when the host-side read finishes.</summary>
        (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadViaStream(AsyncDispatcher dispatcher, FileSize offset);

        /// <summary><c>write-via-stream: func(data: stream&lt;u8&gt;,
        /// offset: filesize) -&gt; future&lt;result&lt;_,
        /// error-code&gt;&gt;</c>. Drains
        /// <paramref name="streamHandle"/> into the file at the
        /// given offset; returns the future + completion Task.</summary>
        (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle, FileSize offset);

        /// <summary><c>append-via-stream: func(data: stream&lt;u8&gt;)
        /// -&gt; future&lt;result&lt;_, error-code&gt;&gt;</c>.</summary>
        (int futureHandle, Task AppendCompletion) AppendViaStream(
            AsyncDispatcher dispatcher, int streamHandle);

        /// <summary><c>advise: async func(offset, length, advice)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task AdviseAsync(
            FileSize offset, FileSize length, Advice advice,
            CancellationToken cancellationToken = default);

        /// <summary><c>sync-data: async func() -&gt;
        /// result&lt;_, error-code&gt;</c>.</summary>
        Task SyncDataAsync(CancellationToken cancellationToken = default);

        /// <summary><c>get-flags: func() -&gt; descriptor-flags</c>
        /// — synchronous in the WIT.</summary>
        DescriptorFlags GetFlags();

        /// <summary><c>get-type: func() -&gt; descriptor-type</c>
        /// — synchronous.</summary>
        DescriptorType GetType_();

        /// <summary><c>set-size: async func(size: filesize)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task SetSizeAsync(
            FileSize size, CancellationToken cancellationToken = default);

        /// <summary><c>set-times: async func(data-access, data-mod)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task SetTimesAsync(
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp,
            CancellationToken cancellationToken = default);

        /// <summary><c>read-directory: func() -&gt;
        /// tuple&lt;stream&lt;directory-entry&gt;,
        /// future&lt;result&lt;_, error-code&gt;&gt;&gt;</c>.
        /// Note: the stream element type is
        /// <c>directory-entry</c>, not byte — a typed-stream lift
        /// that the canon-async dispatcher will need to surface
        /// once the typed-stream lift pathway is built. For
        /// Slice C the implementation may produce a stream of
        /// canon-ABI-lowered <c>directory-entry</c> records;
        /// the binding lands later.</summary>
        (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadDirectory(AsyncDispatcher dispatcher);

        /// <summary><c>sync: async func() -&gt;
        /// result&lt;_, error-code&gt;</c>.</summary>
        Task SyncAsync(CancellationToken cancellationToken = default);

        /// <summary><c>create-directory-at: async func(path)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task CreateDirectoryAtAsync(
            string path, CancellationToken cancellationToken = default);

        /// <summary><c>stat: async func() -&gt;
        /// result&lt;descriptor-stat, error-code&gt;</c>.</summary>
        Task<DescriptorStat> StatAsync(
            CancellationToken cancellationToken = default);

        /// <summary><c>stat-at: async func(path-flags, path)
        /// -&gt; result&lt;descriptor-stat, error-code&gt;</c>.</summary>
        Task<DescriptorStat> StatAtAsync(
            PathFlags pathFlags, string path,
            CancellationToken cancellationToken = default);

        /// <summary><c>set-times-at: async func(path-flags, path,
        /// access, mod) -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task SetTimesAtAsync(
            PathFlags pathFlags, string path,
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp,
            CancellationToken cancellationToken = default);

        /// <summary><c>link-at: async func(old-path-flags,
        /// old-path, new-descriptor, new-path) -&gt;
        /// result&lt;_, error-code&gt;</c>. The
        /// <c>borrow&lt;descriptor&gt;</c> is passed through here
        /// as the target descriptor instance — the canon-binding
        /// layer is responsible for resolving the handle.</summary>
        Task LinkAtAsync(
            PathFlags oldPathFlags, string oldPath,
            IDescriptor newDescriptor, string newPath,
            CancellationToken cancellationToken = default);

        /// <summary><c>open-at: async func(path-flags, path,
        /// open-flags, %flags) -&gt; result&lt;descriptor,
        /// error-code&gt;</c>. Returns a fresh descriptor
        /// instance — the canon-binding layer allocates the
        /// resource handle.</summary>
        Task<IDescriptor> OpenAtAsync(
            PathFlags pathFlags, string path,
            OpenFlags openFlags, DescriptorFlags flags,
            CancellationToken cancellationToken = default);

        /// <summary><c>readlink-at: async func(path)
        /// -&gt; result&lt;string, error-code&gt;</c>.</summary>
        Task<string> ReadlinkAtAsync(
            string path, CancellationToken cancellationToken = default);

        /// <summary><c>remove-directory-at: async func(path)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task RemoveDirectoryAtAsync(
            string path, CancellationToken cancellationToken = default);

        /// <summary><c>rename-at: async func(old-path,
        /// new-descriptor, new-path) -&gt;
        /// result&lt;_, error-code&gt;</c>.</summary>
        Task RenameAtAsync(
            string oldPath, IDescriptor newDescriptor, string newPath,
            CancellationToken cancellationToken = default);

        /// <summary><c>symlink-at: async func(old-path, new-path)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task SymlinkAtAsync(
            string oldPath, string newPath,
            CancellationToken cancellationToken = default);

        /// <summary><c>unlink-file-at: async func(path)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task UnlinkFileAtAsync(
            string path, CancellationToken cancellationToken = default);

        /// <summary><c>is-same-object: async func(other:
        /// borrow&lt;descriptor&gt;) -&gt; bool</c>.</summary>
        Task<bool> IsSameObjectAsync(
            IDescriptor other,
            CancellationToken cancellationToken = default);

        /// <summary><c>metadata-hash: async func() -&gt;
        /// result&lt;metadata-hash-value, error-code&gt;</c>.</summary>
        Task<MetadataHashValue> MetadataHashAsync(
            CancellationToken cancellationToken = default);

        /// <summary><c>metadata-hash-at: async func(path-flags,
        /// path) -&gt; result&lt;metadata-hash-value,
        /// error-code&gt;</c>.</summary>
        Task<MetadataHashValue> MetadataHashAtAsync(
            PathFlags pathFlags, string path,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Single preopen pair: a host-side
    /// <see cref="IDescriptor"/> + the path the guest sees it
    /// at.
    /// </summary>
    public sealed class DescriptorPreopen
    {
        public IDescriptor Descriptor { get; }
        public string Path { get; }

        public DescriptorPreopen(IDescriptor descriptor, string path)
        {
            Descriptor = descriptor;
            Path = path;
        }
    }

    /// <summary>
    /// Host interface for
    /// <c>wasi:filesystem/preopens@0.3.0-rc-2026-03-15</c>.
    /// Returns the static set of pre-opened
    /// directory descriptors the guest sees.
    /// </summary>
    public interface IPreopens
    {
        /// <summary><c>get-directories: func() -&gt;
        /// list&lt;tuple&lt;descriptor, string&gt;&gt;</c>.</summary>
        System.Collections.Generic.IReadOnlyList<DescriptorPreopen>
            GetDirectories();
    }
}
