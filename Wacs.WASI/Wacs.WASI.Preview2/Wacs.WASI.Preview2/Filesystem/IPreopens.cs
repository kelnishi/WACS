// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace Wacs.WASI.Preview2.Filesystem
{
    // The IPreopens interface is now emitted by the source
    // generator from wit/deps/filesystem/preopens.wit
    // (signature: (IDescriptor, string)[] GetDirectories()).
    // This file retains the default conservative impl + a
    // mount-pair convenience ctor — the generated interface is
    // authoritative.

    /// <summary>
    /// Default <see cref="IPreopens"/> impl. The parameterless ctor
    /// exposes nothing — the guest can't see any host filesystems
    /// (the conservative default for sandboxed embeddings). The
    /// mount-pair ctor wraps each <c>(hostPath, guestPath)</c> in a
    /// <see cref="Descriptor"/> so the guest's
    /// <c>wasi:filesystem/preopens.get-directories()</c> import
    /// returns a real list backed by host directories.
    ///
    /// <para>Mount-pair usage:</para>
    /// <code>
    ///   services.AddSingleton&lt;IPreopens&gt;(_ =>
    ///       new Preopens(("./models", "/models"),
    ///                    ("./assets", "/assets")));
    /// </code>
    ///
    /// <para>Hosts that want richer behavior (read-only mounts,
    /// virtual roots, etc.) substitute their own
    /// <see cref="IPreopens"/> implementation.</para>
    /// </summary>
    public sealed class Preopens : IPreopens
    {
        private readonly (IDescriptor, string)[] _entries;

        /// <summary>Empty — no host filesystems exposed.</summary>
        public Preopens()
        {
            _entries = Array.Empty<(IDescriptor, string)>();
        }

        /// <summary>
        /// Each <c>(hostPath, guestPath)</c> pair becomes one entry
        /// in the guest's preopen list. <c>guestPath</c> is what the
        /// guest sees (typically an absolute path like
        /// <c>"/models"</c>); <c>hostPath</c> is the directory on
        /// the host filesystem the descriptor wraps.
        /// </summary>
        public Preopens(params (string hostPath, string guestPath)[] mounts)
            : this((IEnumerable<(string, string)>)(mounts
                ?? Array.Empty<(string, string)>())) { }

        public Preopens(IEnumerable<(string hostPath, string guestPath)> mounts)
        {
            if (mounts == null) throw new ArgumentNullException(nameof(mounts));
            _entries = mounts.Select(m =>
                ((IDescriptor)new Descriptor(m.hostPath), m.guestPath))
                .ToArray();
        }

        public (IDescriptor, string)[] GetDirectories() => _entries;
    }
}
