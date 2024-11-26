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

using Wacs.Core.Runtime;

namespace Wacs.WASIp1
{
    public class Wasi
    {
        private readonly Clock _clock;
        private readonly WasiConfiguration _config;

        private readonly Env _env;
        private readonly Filesystem _fs;
        private readonly Proc _proc;
        private readonly Random _random;
        private readonly State _state;
        private readonly Poll _poll;
        private readonly Sock _sock;

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
            _fs = new Filesystem(config, _state);
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
    }
}