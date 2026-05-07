// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Filesystem
{
    // The IPreopens interface is now emitted by the source
    // generator from wit/deps/filesystem/preopens.wit
    // (signature: (IDescriptor, string)[] GetDirectories()).
    // This file retains only the default conservative impl —
    // the generated interface is authoritative.

    /// <summary>Default <see cref="IPreopens"/> impl exposes
    /// nothing — the guest can't see any host filesystems.
    /// The conservative default for sandboxed embeddings;
    /// hosts that want to expose specific roots should
    /// substitute their own implementation.</summary>
    public sealed class Preopens : IPreopens
    {
        public (IDescriptor, string)[] GetDirectories() =>
            System.Array.Empty<(IDescriptor, string)>();
    }
}
