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
    public class ExitTests
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
        public void Default_Exit_throws_ExitException_with_zero_for_ok()
        {
            // isErr=false → Ok → exit code 0.
            var exit = new ExitHandler();
            var ex = Assert.Throws<ExitException>(() => exit.Exit(false));
            Assert.Equal((byte)0, ex.ExitCode);
        }

        [Fact]
        public void Default_Exit_throws_ExitException_with_one_for_err()
        {
            // isErr=true → Err → exit code 1.
            var exit = new ExitHandler();
            var ex = Assert.Throws<ExitException>(() => exit.Exit(true));
            Assert.Equal((byte)1, ex.ExitCode);
        }

        [Fact]
        public void BindWasiInstance_propagates_exit_through_invoke()
        {
            // wasi-exit-component imports exit(status: result).
            // The result-no-payload form lowers to a single
            // i32 discriminator (0 = Ok, 1 = Err). The auto-
            // binder's primitive path picks IExit.Exit(bool) →
            // wasm i32 wire form; the host's ExitException
            // unwinds back through ProcessThreadAsync to the
            // Invoke call.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-exit-component", "exit.component.wasm"));
            var exit = new ExitHandler();
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:cli/exit@0.2.3", exit));

            var ex1 = Assert.ThrowsAny<System.Exception>(() =>
                ci.Invoke("call-exit-ok"));
            // The wasm runtime may wrap the host exception; pull
            // the ExitException out of the chain.
            var inner1 = ex1;
            while (inner1 != null && !(inner1 is ExitException))
                inner1 = inner1.InnerException;
            Assert.IsType<ExitException>(inner1);
            Assert.Equal((byte)0, ((ExitException)inner1!).ExitCode);

            var ex2 = Assert.ThrowsAny<System.Exception>(() =>
                ci.Invoke("call-exit-err"));
            var inner2 = ex2;
            while (inner2 != null && !(inner2 is ExitException))
                inner2 = inner2.InnerException;
            Assert.IsType<ExitException>(inner2);
            Assert.Equal((byte)1, ((ExitException)inner2!).ExitCode);
        }
    }
}
