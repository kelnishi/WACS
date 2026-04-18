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
using System.IO;
using System.Linq;
using Wacs.Core.Runtime;

namespace Wacs.WASIp1
{
    public class Wasi : IBindable, IDisposable
    {
        private readonly Clock _clock;
        private readonly WasiConfiguration _config;

        private readonly Env _env;
        private readonly FileSystem _fs;
        private readonly Poll _poll;
        private readonly Proc _proc;
        private readonly Random _random;
        private readonly Sock _sock;
        private readonly State _state;

        /// <summary>
        /// Standard-defaults ctor for host discovery via <see cref="IBindable"/>.
        /// Attaches stdio to the current process, inherits host environment
        /// variables, and treats the process's current directory as the WASI
        /// root with no preopens. Callers that need a tighter configuration
        /// should use <see cref="Wasi(WasiConfiguration)"/> directly.
        /// </summary>
        public Wasi() : this(DefaultConfiguration()) { }

        public Wasi(WasiConfiguration config)
        {
            _config = config;
            _state = new State();
            _proc = new Proc(_state);
            _poll = new Poll(_state);
            _env = new Env(config);
            _clock = new Clock(config);
            _random = new Random();
            _sock = new Sock(_state);
            _fs = new FileSystem(config, _state);
        }

        /// <summary>
        /// Standard-defaults WASI configuration — stdio attached to the
        /// current process, host environment variables inherited, and the
        /// process's current directory as the WASI root. No preopened
        /// directories; callers that need filesystem access should extend
        /// <see cref="WasiConfiguration.PreopenedDirectories"/>.
        /// </summary>
        public static WasiConfiguration DefaultConfiguration()
        {
            return new WasiConfiguration
            {
                StandardInput = Console.OpenStandardInput(),
                StandardOutput = Console.OpenStandardOutput(),
                StandardError = Console.OpenStandardError(),
                Arguments = Environment.GetCommandLineArgs().Skip(1).ToList(),
                EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .ToDictionary(de => de.Key.ToString()!,
                                  de => de.Value?.ToString() ?? string.Empty),
                HostRootDirectory = Directory.GetCurrentDirectory(),
            };
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            _proc.BindToRuntime(runtime);
            _poll.BindToRuntime(runtime);
            _env.BindToRuntime(runtime);
            _clock.BindToRuntime(runtime);
            _random.BindToRuntime(runtime);
            _sock.BindToRuntime(runtime);
            _fs.BindToRuntime(runtime);
        }

        public void Dispose()
        {
            _fs.Dispose();
        }
    }
}