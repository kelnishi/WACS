// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview3.Clocks;

namespace Wacs.WASI.Preview3.Filesystem
{
    /// <summary>
    /// WIT <c>type filesize = u64</c>. Spelled as a struct to
    /// keep the host C# API type-safe (a plain ulong would let
    /// callers mix file sizes with arbitrary u64 values like
    /// hashes).
    /// </summary>
    public readonly struct FileSize : IEquatable<FileSize>
    {
        public ulong Value { get; }
        public FileSize(ulong value) { Value = value; }
        public bool Equals(FileSize other) => Value == other.Value;
        public override bool Equals(object? obj) =>
            obj is FileSize fs && Equals(fs);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static implicit operator ulong(FileSize fs) => fs.Value;
        public static explicit operator FileSize(ulong v) => new FileSize(v);
    }

    /// <summary>
    /// WIT <c>type link-count = u64</c>.
    /// </summary>
    public readonly struct LinkCount : IEquatable<LinkCount>
    {
        public ulong Value { get; }
        public LinkCount(ulong value) { Value = value; }
        public bool Equals(LinkCount other) => Value == other.Value;
        public override bool Equals(object? obj) =>
            obj is LinkCount lc && Equals(lc);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static implicit operator ulong(LinkCount lc) => lc.Value;
        public static explicit operator LinkCount(ulong v) => new LinkCount(v);
    }

    /// <summary>
    /// WIT <c>variant descriptor-type</c>. Tag-only cases are
    /// represented as enum values; the <c>other(option&lt;string&gt;)</c>
    /// case carries an optional payload via
    /// <see cref="OtherPayload"/>.
    /// </summary>
    public readonly struct DescriptorType : IEquatable<DescriptorType>
    {
        public enum Tag
        {
            BlockDevice,
            CharacterDevice,
            Directory,
            Fifo,
            SymbolicLink,
            RegularFile,
            Socket,
            Other,
        }

        public Tag Kind { get; }
        public string? OtherPayload { get; }

        private DescriptorType(Tag kind, string? otherPayload = null)
        {
            Kind = kind;
            OtherPayload = otherPayload;
        }

        public static DescriptorType BlockDevice =>
            new DescriptorType(Tag.BlockDevice);
        public static DescriptorType CharacterDevice =>
            new DescriptorType(Tag.CharacterDevice);
        public static DescriptorType Directory =>
            new DescriptorType(Tag.Directory);
        public static DescriptorType Fifo =>
            new DescriptorType(Tag.Fifo);
        public static DescriptorType SymbolicLink =>
            new DescriptorType(Tag.SymbolicLink);
        public static DescriptorType RegularFile =>
            new DescriptorType(Tag.RegularFile);
        public static DescriptorType Socket =>
            new DescriptorType(Tag.Socket);
        public static DescriptorType Other(string? payload = null) =>
            new DescriptorType(Tag.Other, payload);

        public bool Equals(DescriptorType other) =>
            Kind == other.Kind &&
            string.Equals(OtherPayload, other.OtherPayload, StringComparison.Ordinal);
        public override bool Equals(object? obj) =>
            obj is DescriptorType d && Equals(d);
        public override int GetHashCode() =>
            ((int)Kind * 397) ^ (OtherPayload?.GetHashCode() ?? 0);
    }

    /// <summary>
    /// WIT <c>flags descriptor-flags</c>. CLR <c>[Flags]</c>
    /// enum with bit positions matching the WIT declaration
    /// order (canon-ABI flags lowering writes the bitfield in
    /// declaration order).
    /// </summary>
    [Flags]
    public enum DescriptorFlags : uint
    {
        None                 = 0,
        Read                 = 1u << 0,
        Write                = 1u << 1,
        FileIntegritySync    = 1u << 2,
        DataIntegritySync    = 1u << 3,
        RequestedWriteSync   = 1u << 4,
        MutateDirectory      = 1u << 5,
    }

    /// <summary>WIT <c>flags path-flags</c>.</summary>
    [Flags]
    public enum PathFlags : uint
    {
        None          = 0,
        SymlinkFollow = 1u << 0,
    }

    /// <summary>WIT <c>flags open-flags</c>.</summary>
    [Flags]
    public enum OpenFlags : uint
    {
        None      = 0,
        Create    = 1u << 0,
        Directory = 1u << 1,
        Exclusive = 1u << 2,
        Truncate  = 1u << 3,
    }

    /// <summary>WIT <c>record descriptor-stat</c>.</summary>
    public sealed record DescriptorStat
    {
        public DescriptorType Type { get; init; }
        public LinkCount LinkCount { get; init; }
        public FileSize Size { get; init; }
        public Instant? DataAccessTimestamp { get; init; }
        public Instant? DataModificationTimestamp { get; init; }
        public Instant? StatusChangeTimestamp { get; init; }
    }

    /// <summary>
    /// WIT <c>variant new-timestamp { no-change, now,
    /// timestamp(instant) }</c>.
    /// </summary>
    public readonly struct NewTimestamp : IEquatable<NewTimestamp>
    {
        public enum Tag { NoChange, Now, Timestamp }

        public Tag Kind { get; }
        public Instant TimestampValue { get; }

        private NewTimestamp(Tag kind, Instant value = default)
        {
            Kind = kind;
            TimestampValue = value;
        }

        public static NewTimestamp NoChange =>
            new NewTimestamp(Tag.NoChange);
        public static NewTimestamp Now =>
            new NewTimestamp(Tag.Now);
        public static NewTimestamp Timestamp(Instant value) =>
            new NewTimestamp(Tag.Timestamp, value);

        public bool Equals(NewTimestamp other)
        {
            if (Kind != other.Kind) return false;
            if (Kind != Tag.Timestamp) return true;
            return TimestampValue.Seconds == other.TimestampValue.Seconds
                && TimestampValue.Nanoseconds == other.TimestampValue.Nanoseconds;
        }
        public override bool Equals(object? obj) =>
            obj is NewTimestamp nt && Equals(nt);
        public override int GetHashCode()
        {
            if (Kind != Tag.Timestamp) return (int)Kind;
            return unchecked(
                ((int)Kind * 397)
                ^ TimestampValue.Seconds.GetHashCode()
                ^ TimestampValue.Nanoseconds.GetHashCode());
        }
    }

    /// <summary>WIT <c>record directory-entry</c>.</summary>
    public sealed record DirectoryEntry
    {
        public DescriptorType Type { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// WIT <c>variant error-code</c>. All cases bar
    /// <see cref="ErrorCode.Other"/> are tag-only — modeled as a
    /// flat enum. The <c>other(option&lt;string&gt;)</c>
    /// payload is carried by
    /// <see cref="FilesystemException.OtherPayload"/>.
    /// </summary>
    public enum ErrorCode
    {
        Access,
        Already,
        BadDescriptor,
        Busy,
        Deadlock,
        Quota,
        Exist,
        FileTooLarge,
        IllegalByteSequence,
        InProgress,
        Interrupted,
        Invalid,
        Io,
        IsDirectory,
        Loop,
        TooManyLinks,
        MessageSize,
        NameTooLong,
        NoDevice,
        NoEntry,
        NoLock,
        InsufficientMemory,
        InsufficientSpace,
        NotDirectory,
        NotEmpty,
        NotRecoverable,
        Unsupported,
        NoTty,
        NoSuchDevice,
        Overflow,
        NotPermitted,
        Pipe,
        ReadOnly,
        InvalidSeek,
        TextFileBusy,
        CrossDevice,
        Other,
    }

    /// <summary>
    /// Host-side exception that carries an
    /// <see cref="ErrorCode"/> for the canon-ABI binding to
    /// lower into a <c>result&lt;_, error-code&gt;</c> error
    /// case. The default <see cref="Descriptor"/> impl throws
    /// these from its method bodies; the binding catches and
    /// converts to the wire-level error-code value.
    /// </summary>
    public sealed class FilesystemException : Exception
    {
        public ErrorCode Code { get; }
        public string? OtherPayload { get; }

        public FilesystemException(ErrorCode code, string? message = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            OtherPayload = code == ErrorCode.Other ? message : null;
        }

        public FilesystemException(ErrorCode code, Exception inner)
            : base(inner.Message, inner)
        {
            Code = code;
            OtherPayload = code == ErrorCode.Other ? inner.Message : null;
        }
    }

    /// <summary>WIT <c>enum advice</c>.</summary>
    public enum Advice
    {
        Normal,
        Sequential,
        Random,
        WillNeed,
        DontNeed,
        NoReuse,
    }

    /// <summary>WIT <c>record metadata-hash-value</c>.</summary>
    public sealed record MetadataHashValue
    {
        public ulong Lower { get; init; }
        public ulong Upper { get; init; }
    }
}
