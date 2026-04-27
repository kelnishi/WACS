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
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Integration test that exercises the full WASI stdio
    /// chain: a component imports wasi:cli/stdout +
    /// wasi:io/streams, calls get-stdout() then writes
    /// "hello\n" to the returned output-stream then drops the
    /// handle. Verifies cli/stdout factory + output-stream
    /// resource methods + drop wiring all interoperate.
    /// </summary>
    public class HelloWorldTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        /// <summary>Capturing IStdout — hands out a captured-
        /// bytes OutputStream so the test can assert on what
        /// the guest wrote.</summary>
        public sealed class CapturingStdout : IStdout
        {
            public readonly System.Collections.Generic.List<byte> All =
                new System.Collections.Generic.List<byte>();

            public OutputStream GetStdout() => new Capture(this);

            private sealed class Capture : OutputStream
            {
                private readonly CapturingStdout _owner;
                public Capture(CapturingStdout owner) { _owner = owner; }
                public override void Write(byte[] contents) =>
                    _owner.All.AddRange(contents);
                public override void BlockingWriteAndFlush(byte[] contents) =>
                    _owner.All.AddRange(contents);
            }
        }

        [Fact]
        public void Hello_world_writes_through_stdout_chain()
        {
            // Component:
            //   import wasi:cli/stdout@0.2.3;
            //   import wasi:io/streams@0.2.3;
            //   export greet: func();
            // greet() calls stdout.get-stdout() → handle,
            // streams.[method]output-stream.blocking-write-and-flush(handle, "hello\n"),
            // streams.[resource-drop]output-stream(handle).
            // Host wires:
            //   stdout via CapturingStdout (which itself
            //     allocates OutputStream handles in the
            //     resource table).
            //   streams via BindWasiResource<OutputStream>.
            // Both bindings share the same ResourceContext so
            // the handle stdout returns is the same handle the
            // streams.* methods can look up.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-hello-component", "hello.component.wasm"));
            var resources = new ResourceContext();
            var stdout = new CapturingStdout();

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new CliBindings(resources, stdout: stdout)
                    .BindToRuntime(runtime);
                runtime.BindWasiResource<OutputStream>(
                    "wasi:io/streams@0.2.3", resources);
            });

            ci.Invoke("greet");

            Assert.Equal("hello\n",
                System.Text.Encoding.UTF8.GetString(
                    stdout.All.ToArray()));
        }
    }
}
