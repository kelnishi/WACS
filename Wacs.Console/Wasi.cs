using System;
using System.Collections;
using System.IO;
using System.Linq;
using Wacs.WASIp1;

namespace Wacs.Console
{
    public static class Wasi
    {
        public static WasiConfiguration GetDefaultWasiConfiguration() =>
            new() {
                StandardInput = System.Console.OpenStandardInput(),
                StandardOutput = System.Console.OpenStandardOutput(),
                StandardError = System.Console.OpenStandardError(),
                
                Arguments = Environment.GetCommandLineArgs()
                    .ToList(),
                
                EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<DictionaryEntry>()
                    .ToDictionary(de => de.Key.ToString(), de => de.Value!.ToString()),
                
                HostRootDirectory = Directory.GetCurrentDirectory(),
            };
    }
}