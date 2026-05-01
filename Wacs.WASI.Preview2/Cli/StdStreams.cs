// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
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
    /// <para>Doesn't dispose the underlying stream — console
    /// streams are process-lived. Subclasses that own a
    /// Disposable stream should override <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public sealed class HostStream : OutputStream
    {
        private readonly Stream _underlying;

        public HostStream(Stream underlying)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public override Result<Unit, StreamError> Write(byte[] contents)
        {
            try { _underlying.Write(contents, 0, contents.Length); }
            catch (Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> BlockingWriteAndFlush(
            byte[] contents)
        {
            try
            {
                _underlying.Write(contents, 0, contents.Length);
                _underlying.Flush();
            }
            catch (Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> Flush()
        {
            try { _underlying.Flush(); }
            catch (Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> BlockingFlush() => Flush();

        public override void Dispose() { /* console streams are process-lived */ }
    }

    /// <summary>Default <see cref="IStdout"/> impl —
    /// returns a fresh OutputStream wrapping
    /// <see cref="Console.OpenStandardOutput"/> on each call.
    /// </summary>
    public sealed class Stdout : IStdout
    {
        public IOutputStream GetStdout() =>
            new HostStream(Console.OpenStandardOutput());
    }

    public sealed class Stderr : IStderr
    {
        public IOutputStream GetStderr() =>
            new HostStream(Console.OpenStandardError());
    }

    /// <summary>Default <see cref="IStdin"/> impl — wraps
    /// <see cref="Console.OpenStandardInput"/>.</summary>
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

        public override Result<byte[], StreamError> Read(ulong len)
        {
            int n = (int)Math.Min(len, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read;
            try { read = _underlying.Read(buf, 0, n); }
            catch (Exception e)
            {
                return Result<byte[], StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            if (read == 0)
                return Result<byte[], StreamError>.FromErr(
                    new StreamError.StreamErrorClosed());
            if (read == n)
                return Result<byte[], StreamError>.FromOk(buf);
            var slice = new byte[read];
            Array.Copy(buf, 0, slice, 0, read);
            return Result<byte[], StreamError>.FromOk(slice);
        }

        public override Result<byte[], StreamError> BlockingRead(ulong len)
            => Read(len);

        public override void Dispose() { }
    }
}
