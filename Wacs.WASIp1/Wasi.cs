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

        public Wasi(WasiConfiguration config)
        {
            _config = config;
            _state = new State();

            _proc = new Proc(_state);
            _env = new Env(config);
            _clock = new Clock(config);
            _random = new Random();

            _fs = new Filesystem(config, _state);
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            _proc.BindToRuntime(runtime);
            _env.BindToRuntime(runtime);
            _clock.BindToRuntime(runtime);
            _random.BindToRuntime(runtime);
            _fs.BindToRuntime(runtime);
        }
    }
}