// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// OutputStream backed by a .NET <see cref="Stream"/> —
    /// pipes the wasm guest's writes into Console.Out /
    /// Console.Error / etc. The default Stdout / Stderr
    /// implementations use this with the appropriate console
    /// stream.
    ///
    /// <para>The handle table calls Dispose on
    /// <see cref="OutputStream.Dispose"/> when the guest drops;
    /// for a console-backed stream we don't actually want to
    /// close the console — override is a no-op.</para>
    /// </summary>
    public sealed class HostStream : OutputStream
    {
        private readonly Stream _underlying;

        public HostStream(Stream underlying)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public override void Write(byte[] contents)
        {
            _underlying.Write(contents, 0, contents.Length);
        }

        public override void BlockingWriteAndFlush(byte[] contents)
        {
            _underlying.Write(contents, 0, contents.Length);
            _underlying.Flush();
        }

        public override void Flush()
        {
            _underlying.Flush();
        }

        public override void BlockingFlush()
        {
            _underlying.Flush();
        }

        public override void Dispose()
        {
            // Don't actually close — console streams are
            // process-lived. Subclasses that own a Disposable
            // stream should override.
        }
    }

    /// <summary>Default <see cref="IStdout"/> impl —
    /// returns a fresh OutputStream wrapping
    /// <see cref="Console.OpenStandardOutput"/> on each call.
    /// Each guest-imported handle is independent; sharing
    /// state between them is the caller's job.</summary>
    public sealed class Stdout : IStdout
    {
        // Returns IOutputStream (the generated faithful contract);
        // the concrete instance is still the hand-written
        // HostStream (which implements IOutputStream via
        // OutputStream's explicit-interface stubs).
        public IOutputStream GetStdout() =>
            new HostStream(Console.OpenStandardOutput());
    }

    public sealed class Stderr : IStderr
    {
        public IOutputStream GetStderr() =>
            new HostStream(Console.OpenStandardError());
    }

    /// <summary>Default <see cref="IStdin"/> impl — wraps
    /// <see cref="Console.OpenStandardInput"/>. The
    /// <see cref="HostInputStream"/> reads from a .NET stream
    /// in the same way <see cref="HostStream"/> writes to
    /// one.</summary>
    public sealed class Stdin : IStdin
    {
        public IInputStream GetStdin() =>
            new HostInputStream(Console.OpenStandardInput());
    }

    /// <summary>InputStream backed by a .NET
    /// <see cref="Stream"/>. Read pulls bytes from the
    /// underlying stream up to the requested length; partial
    /// reads are normal at EOF or in non-blocking modes.
    /// </summary>
    public sealed class HostInputStream : InputStream
    {
        private readonly Stream _underlying;

        public HostInputStream(Stream underlying)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public override byte[] Read(ulong len)
        {
            int n = (int)Math.Min(len, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read = _underlying.Read(buf, 0, n);
            if (read == n) return buf;
            // Partial read — slice to actual count.
            var slice = new byte[read];
            Array.Copy(buf, 0, slice, 0, read);
            return slice;
        }

        public override byte[] BlockingRead(ulong len) => Read(len);

        public override void Dispose() { }
    }
}
