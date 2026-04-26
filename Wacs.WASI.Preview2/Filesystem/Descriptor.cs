// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>
    /// Host representation of <c>wasi:filesystem/types@0.2.x</c>'s
    /// <c>descriptor</c> resource. Stands in for an opened file
    /// or directory.
    ///
    /// <para>v0 ships the resource as a marker — no methods.
    /// Guests can receive descriptor handles from
    /// <c>wasi:filesystem/preopens.get-directories</c> but
    /// can't yet operate on them. The full method surface
    /// (read-via-stream, write-via-stream, stat, etc.) is a
    /// follow-up; this commit lands the type so preopens has
    /// something to return.</para>
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

        public virtual void Dispose() { }
    }
}
