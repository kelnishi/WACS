// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class EnvironmentTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void BindWasiInstance_round_trips_get_arguments_list_string()
        {
            // wasi-environment-component imports get-arguments
            // → list<string>. Stub with a fixed array; assert
            // round-trip.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-environment-component", "env.component.wasm"));
            var stub = new StubEnv(
                args: new[] { "alpha", "beta", "γ" },
                cwd: "/home/test",
                env: new[] { ("FOO", "1"), ("BAR", "x") });
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:cli/environment@0.2.3", stub));

            var args = (string[])ci.Invoke("get-args")!;
            Assert.Equal(new[] { "alpha", "beta", "γ" }, args);
        }

        [Fact]
        public void BindWasiInstance_round_trips_initial_cwd_some()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-environment-component", "env.component.wasm"));
            var stub = new StubEnv(
                args: System.Array.Empty<string>(),
                cwd: "/some/path",
                env: System.Array.Empty<(string, string)>());
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:cli/environment@0.2.3", stub));
            Assert.Equal("/some/path", ci.Invoke("get-cwd"));
        }

        [Fact]
        public void BindWasiInstance_round_trips_initial_cwd_none()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-environment-component", "env.component.wasm"));
            var stub = new StubEnv(
                args: System.Array.Empty<string>(),
                cwd: null,
                env: System.Array.Empty<(string, string)>());
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:cli/environment@0.2.3", stub));
            Assert.Null(ci.Invoke("get-cwd"));
        }

        [Fact]
        public void BindWasiInstance_round_trips_get_environment_pairs()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-environment-component", "env.component.wasm"));
            var stub = new StubEnv(
                args: System.Array.Empty<string>(),
                cwd: null,
                env: new[] { ("HOME", "/r"), ("PATH", "/usr/bin") });
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:cli/environment@0.2.3", stub));

            var pairs = ((string, string)[])ci.Invoke("get-env")!;
            Assert.Equal(2, pairs.Length);
            Assert.Equal(("HOME", "/r"), pairs[0]);
            Assert.Equal(("PATH", "/usr/bin"), pairs[1]);
        }

        private sealed class StubEnv : IEnvironment
        {
            private readonly string[] _args;
            private readonly string? _cwd;
            private readonly (string, string)[] _env;
            public StubEnv(string[] args, string? cwd,
                (string, string)[] env)
            { _args = args; _cwd = cwd; _env = env; }

            public (string, string)[] GetEnvironment() => _env;
            public string[] GetArguments() => _args;
            [WasiOptionalReturn]
            public string? InitialCwd() => _cwd;
        }
    }
}
