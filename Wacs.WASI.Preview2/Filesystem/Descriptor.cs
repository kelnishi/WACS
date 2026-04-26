// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>WIT enum
    /// <c>wasi:filesystem/types.descriptor-type</c>. The wire
    /// representation is a 1-byte discriminator; the host
    /// returns the C# enum and the binder reads its
    /// underlying byte.</summary>
    public enum DescriptorType : byte
    {
        Unknown = 0,
        BlockDevice = 1,
        CharacterDevice = 2,
        Directory = 3,
        Fifo = 4,
        SymbolicLink = 5,
        RegularFile = 6,
        Socket = 7,
    }

    /// <summary>
    /// Host representation of <c>wasi:filesystem/types@0.2.x</c>'s
    /// <c>descriptor</c> resource. Stands in for an opened file
    /// or directory.
    ///
    /// <para>v0 ships the bridge methods (read-via-stream,
    /// write-via-stream, append-via-stream) that turn a
    /// descriptor into an input/output-stream. The remaining
    /// ~25 descriptor methods (open-at, stat, get-type,
    /// readlink-at, etc.) ride incrementally as their wire
    /// shapes become tractable.</para>
    /// </summary>
    [WasiResource("descriptor")]
    public class Descriptor : IDisposable
    {
        /// <summary>Underlying filesystem path the descriptor
        /// represents — used by host implementations that
        /// proxy to <see cref="System.IO.File"/> /
        /// <see cref="System.IO.Directory"/>.</summary>
        public string Path { get; }

        public Descriptor(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>Report the descriptor's WIT type. Default
        /// inspects the host filesystem: directories yield
        /// <see cref="DescriptorType.Directory"/>, files
        /// <see cref="DescriptorType.RegularFile"/>, otherwise
        /// <see cref="DescriptorType.Unknown"/>. Subclasses
        /// override for virtual paths.
        ///
        /// <para>Method is named <c>GetDescriptorType</c> rather
        /// than <c>GetType</c> to avoid the
        /// <see cref="object.GetType"/> clash; the
        /// <see cref="WasiMethodNameAttribute"/> override
        /// restores the WIT name <c>get-type</c>.</para></summary>
        [WasiErrorResult]
        [WasiMethodName("get-type")]
        public virtual DescriptorType GetDescriptorType()
        {
            if (Directory.Exists(Path)) return DescriptorType.Directory;
            if (File.Exists(Path)) return DescriptorType.RegularFile;
            return DescriptorType.Unknown;
        }

        /// <summary>Open the file for reading from
        /// <paramref name="offset"/>. Returns an
        /// <see cref="InputStream"/> wrapping the underlying
        /// host file. Default impl uses
        /// <see cref="System.IO.File.OpenRead"/>; subclasses
        /// override for virtual file systems.</summary>
        [WasiErrorResult]
        public virtual InputStream ReadViaStream(ulong offset)
        {
            var stream = File.OpenRead(Path);
            if (offset > 0)
                stream.Seek((long)offset, SeekOrigin.Begin);
            return new HostFileInputStream(stream);
        }

        /// <summary>Open the file for writing at
        /// <paramref name="offset"/>. Returns an
        /// <see cref="OutputStream"/> wrapping the underlying
        /// host file.</summary>
        [WasiErrorResult]
        public virtual OutputStream WriteViaStream(ulong offset)
        {
            var stream = File.OpenWrite(Path);
            if (offset > 0)
                stream.Seek((long)offset, SeekOrigin.Begin);
            return new HostFileOutputStream(stream);
        }

        /// <summary>Open the file for appending. Returns an
        /// <see cref="OutputStream"/> positioned at EOF.</summary>
        [WasiErrorResult]
        public virtual OutputStream AppendViaStream()
        {
            var stream = new FileStream(Path, FileMode.Append,
                FileAccess.Write);
            return new HostFileOutputStream(stream);
        }

        public virtual void Dispose() { }
    }

    /// <summary>InputStream that wraps a host
    /// <see cref="Stream"/> and disposes it on
    /// <see cref="Dispose"/>. Used by Descriptor's
    /// read-via-stream so closing the guest stream actually
    /// releases the file.</summary>
    public sealed class HostFileInputStream : InputStream
    {
        private readonly Stream _stream;
        public HostFileInputStream(Stream stream) { _stream = stream; }

        public override byte[] Read(ulong len)
        {
            int n = (int)System.Math.Min(len, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read = _stream.Read(buf, 0, n);
            if (read == n) return buf;
            var slice = new byte[read];
            System.Array.Copy(buf, 0, slice, 0, read);
            return slice;
        }

        public override byte[] BlockingRead(ulong len) => Read(len);
        public override void Dispose() { _stream.Dispose(); }
    }

    /// <summary>OutputStream that wraps a host
    /// <see cref="Stream"/> and disposes it on
    /// <see cref="Dispose"/>.</summary>
    public sealed class HostFileOutputStream : OutputStream
    {
        private readonly Stream _stream;
        public HostFileOutputStream(Stream stream) { _stream = stream; }

        public override void Write(byte[] contents)
        {
            _stream.Write(contents, 0, contents.Length);
        }

        public override void BlockingWriteAndFlush(byte[] contents)
        {
            _stream.Write(contents, 0, contents.Length);
            _stream.Flush();
        }

        public override void Flush() { _stream.Flush(); }
        public override void BlockingFlush() { _stream.Flush(); }
        public override void Dispose() { _stream.Dispose(); }
    }
}
