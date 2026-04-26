// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>
    /// Host-side surface for
    /// <c>wasi:filesystem/preopens@0.2.x</c>.
    /// <code>
    /// interface preopens {
    ///     use wasi:filesystem/types@0.2.3.{descriptor};
    ///     get-directories: func() -&gt; list&lt;tuple&lt;descriptor, string&gt;&gt;;
    /// }
    /// </code>
    ///
    /// <para>Returns the host-mapped directories the guest is
    /// allowed to access — each as (descriptor handle,
    /// host-readable path string) pairs. Capability-based
    /// security: the guest can only operate on filesystems
    /// rooted in these descriptors.</para>
    /// </summary>
    public interface IPreopens
    {
        (Descriptor, string)[] GetDirectories();
    }

    /// <summary>Default <see cref="IPreopens"/> impl exposes
    /// nothing — the guest can't see any host filesystems.
    /// The conservative default for sandboxed embeddings;
    /// hosts that want to expose specific roots should
    /// substitute their own implementation.</summary>
    public sealed class Preopens : IPreopens
    {
        public (Descriptor, string)[] GetDirectories() =>
            System.Array.Empty<(Descriptor, string)>();
    }
}
