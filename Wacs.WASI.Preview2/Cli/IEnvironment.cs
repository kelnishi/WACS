// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Host-side surface for <c>wasi:cli/environment@0.2.x</c>.
    /// <code>
    /// interface environment {
    ///     get-environment: func() -&gt; list&lt;tuple&lt;string, string&gt;&gt;;
    ///     get-arguments: func() -&gt; list&lt;string&gt;;
    ///     initial-cwd: func() -&gt; option&lt;string&gt;;
    /// }
    /// </code>
    ///
    /// <para>The environment is presented as a key/value pair
    /// list — the WIT type is <c>tuple&lt;string, string&gt;</c>
    /// per element. Hosts that want to filter what the guest
    /// sees should override <see cref="Environment"/>.</para>
    /// </summary>
    public interface IEnvironment
    {
        /// <summary>Environment variables visible to the guest,
        /// as (name, value) pairs.</summary>
        (string, string)[] GetEnvironment();

        /// <summary>Command-line arguments. Index 0 is
        /// conventionally the program name, but the spec leaves
        /// that to the host.</summary>
        string[] GetArguments();

        /// <summary>Initial working directory, if any.
        /// <c>None</c> if no cwd is exposed (sandbox/embedded
        /// scenarios).</summary>
        [WasiOptionalReturn]
        string? InitialCwd();
    }
}
