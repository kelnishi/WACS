// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using SysEnv = System.Environment;

namespace Wacs.WASI.Preview3.Cli
{
    /// <summary>
    /// Host interface for <c>wasi:cli/environment@0.3.0-rc-2026-03-15</c>.
    /// Three methods, all returning canonical-ABI-lowered values
    /// at a host-provided retptr.
    /// </summary>
    public interface IEnvironment
    {
        /// <summary><c>get-environment: func() -> list&lt;tuple&lt;string, string&gt;&gt;</c>
        /// — POSIX-style env var pairs. Spec note: until value
        /// imports land in the component model, this should
        /// return the same values across calls.</summary>
        (string, string)[] GetEnvironment();

        /// <summary><c>get-arguments: func() -> list&lt;string&gt;</c>
        /// — POSIX-style program arguments. Convention varies on
        /// whether argv[0] (program name) is included; the
        /// wasip3 testsuite expects it to be the first entry.</summary>
        string[] GetArguments();

        /// <summary><c>get-initial-cwd: func() -> option&lt;string&gt;</c>
        /// — path the program should use as its initial cwd.
        /// Return null for None (the wasip3 testsuite currently
        /// only tests the None case, pending upstream
        /// resolution: wasi-testsuite#216).</summary>
        string? GetInitialCwd();
    }

    /// <summary>
    /// Default <see cref="IEnvironment"/> implementation
    /// reading from <see cref="SysEnv"/>. Hosts that want to
    /// filter or sandbox what guests see should substitute a
    /// custom <see cref="IEnvironment"/> rather than override
    /// individual entries. Class name is
    /// <c>EnvironmentHandler</c> to avoid the C#
    /// constructor-name clash with the
    /// <see cref="GetEnvironment"/> method.
    /// </summary>
    public sealed class EnvironmentHandler : IEnvironment
    {
        public (string, string)[] GetEnvironment()
        {
            var dict = SysEnv.GetEnvironmentVariables();
            var pairs = new List<(string, string)>(dict.Count);
            foreach (System.Collections.DictionaryEntry e in dict)
            {
                var k = (string)e.Key;
                var v = (string)(e.Value ?? "");
                pairs.Add((k, v));
            }
            return pairs.ToArray();
        }

        public string[] GetArguments()
        {
            // Drop argv[0] (the host's runtime entry-point path)
            // and forward the user-supplied args. Embedders that
            // need full argv override IEnvironment.
            var all = SysEnv.GetCommandLineArgs();
            if (all.Length <= 1) return Array.Empty<string>();
            var result = new string[all.Length - 1];
            Array.Copy(all, 1, result, 0, result.Length);
            return result;
        }

        public string? GetInitialCwd()
        {
            try { return SysEnv.CurrentDirectory; }
            catch { return null; }
        }
    }
}
