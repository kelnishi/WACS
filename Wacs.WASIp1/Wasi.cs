using System;
using System.Collections;
using System.Linq;
using Wacs.Core.Runtime;

namespace Wacs.WASIp1
{
    public class Wasi
    {
        private readonly Clock _clock;

        private readonly Env _env;
        private readonly Proc _proc;
        private readonly Random _random;
        private readonly State _state;

        public Wasi()
        {
            _state = new State
            {
                Arguments = Environment.GetCommandLineArgs()
                    .ToList(),
                EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<DictionaryEntry>()
                    .ToDictionary(de => de.Key.ToString(), de => de.Value.ToString())
            };

            _proc = new Proc(_state);
            _env = new Env(_state);
            _clock = new Clock();
            _random = new Random();

        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            _proc.BindToRuntime(runtime);
            _env.BindToRuntime(runtime);
            _clock.BindToRuntime(runtime);
            _random.BindToRuntime(runtime);
        }
    }
}