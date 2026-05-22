// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;

namespace WitHarnessSpike.Aot
{
    /// <summary>
    /// WIT-harness AOT spike entry point. Loads the
    /// hello.component.wasm bytes, hands them to the hand-written
    /// HelloHarness, and prints the result of harness.Greet("World").
    ///
    /// <para>Run under NativeAOT via:
    /// <code>
    /// dotnet publish -c Release -p:PublishAot=true
    /// ./bin/Release/net8.0/&lt;rid&gt;/publish/WitHarnessSpike.Aot
    /// </code>
    /// Expected output: <c>Hello, World!</c></para>
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var componentPath = ResolveComponentPath();
                var componentBytes = File.ReadAllBytes(componentPath);

                var harness = HelloHarness.LoadFrom(componentBytes);
                var greeting = harness.Greet("World");
                Console.WriteLine($"hand-written:   {greeting}");

                // Run the generator-emitted equivalent too.
                // Should produce identical output — proves the
                // generator's string codegen matches the
                // hand-written hello-spike pattern end-to-end
                // under NativeAOT.
                // Reuse the hand-written harness's WASI stubs
                // — the generator only emits export-side
                // marshaling, leaving import-side bindings to
                // the consumer. (A future generator slice
                // could emit these too once we settle on a
                // typed-imports surface.)
                var gen = new GeneratedHelloHarness(
                    componentBytes, HelloHarness.BindWasiStubs);
                var genGreeting = gen.Greet("World");
                Console.WriteLine($"generator-emit: {genGreeting}");

                bool ok = greeting == "Hello, World!"
                    && genGreeting == "Hello, World!"
                    && greeting == genGreeting;
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static string ResolveComponentPath()
        {
            // The csproj copies hello.component.wasm next to the
            // binary as PreserveNewest, so it sits in
            // AppContext.BaseDirectory.
            var sideBySide = Path.Combine(AppContext.BaseDirectory,
                "hello.component.wasm");
            if (File.Exists(sideBySide))
                return sideBySide;

            // Dev-tree fallback for `dotnet run` (the file lives
            // two directories up at ../wasm/hello.component.wasm).
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "wasm", "hello.component.wasm"));
            if (File.Exists(devTree))
                return devTree;

            throw new FileNotFoundException(
                $"hello.component.wasm not found (looked at "
                + $"{sideBySide} and {devTree})");
        }
    }
}
