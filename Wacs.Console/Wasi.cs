// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections;
using System.IO;
using System.Linq;
using Wacs.WASIp1;

namespace Wacs.Console
{
    public static class Wasi
    {
        public static WasiConfiguration DefaultConfiguration() =>
            new() {
                StandardInput = System.Console.OpenStandardInput(),
                StandardOutput = System.Console.OpenStandardOutput(),
                StandardError = System.Console.OpenStandardError(),
                
                Arguments = Environment.GetCommandLineArgs()
                    .Skip(1)
                    .ToList(),
                
                EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<DictionaryEntry>()
                    .ToDictionary(de => de.Key.ToString()!, de => de.Value?.ToString()??""),
                
                HostRootDirectory = Directory.GetCurrentDirectory(),
            };
    }

}