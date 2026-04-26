// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using SysEnv = System.Environment;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Default <see cref="IEnvironment"/> implementation
    /// reading from <see cref="System.Environment"/>. Hosts
    /// that want to filter/sandbox what guests see should
    /// substitute their own implementation rather than
    /// override individual entries here.
    /// </summary>
    public sealed class Environment : IEnvironment
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
            // Drop the entry point (Environment.GetCommandLineArgs[0])
            // — guests typically expect just the user-supplied
            // args. Hosts that want full argv can override.
            var all = SysEnv.GetCommandLineArgs();
            if (all.Length <= 1) return System.Array.Empty<string>();
            var result = new string[all.Length - 1];
            System.Array.Copy(all, 1, result, 0, result.Length);
            return result;
        }

        public string? InitialCwd()
        {
            return SysEnv.CurrentDirectory;
        }
    }
}
