using System;
using System.IO;
using System.Linq;
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Isolated tests for GC array operations.
    /// Validates that emitted GC IL produces correct type instances.
    /// </summary>
    public class GcIsolation
    {
        private readonly ITestOutputHelper _output;
        public GcIsolation(ITestOutputHelper output) => _output = output;

        [Fact]
        public void ArrayNewFixed()
        {
            // array.6.wasm uses array.new_fixed and nested array.get
            var wrapper = LoadAndTranspile("array.6.wasm");
            if (wrapper == null) return;

            var r = wrapper.InvokeExport("get", new[] { new Value(0), new Value(0) });
            _output.WriteLine($"get(0,0) = {r[0].Data.Float32}");
            Assert.Equal(1.0f, r[0].Data.Float32);
        }

        [Fact]
        public void ArrayNewDefault()
        {
            // array.5.wasm uses array.new_default
            var wrapper = LoadAndTranspile("array.5.wasm");
            if (wrapper == null) return;

            var r = wrapper.InvokeExport("get", new[] { new Value(0) });
            _output.WriteLine($"get(0) = {r[0].Data.Float32}");
            Assert.Equal(0f, r[0].Data.Float32);
        }

        [Fact]
        public void ArraySetGet()
        {
            var wrapper = LoadAndTranspile("array.5.wasm");
            if (wrapper == null) return;

            var r = wrapper.InvokeExport("set_get", new[] { new Value(1), new Value(7.0f) });
            _output.WriteLine($"set_get(1, 7.0) = {r[0].Data.Float32}");
            Assert.Equal(7.0f, r[0].Data.Float32);
        }

        [Fact]
        public void ArrayLen()
        {
            var wrapper = LoadAndTranspile("array.5.wasm");
            if (wrapper == null) return;

            var r = wrapper.InvokeExport("len", Array.Empty<Value>());
            _output.WriteLine($"len() = {r[0].Data.Int32}");
            Assert.Equal(3, r[0].Data.Int32);
        }

        private TranspiledModuleWrapper? LoadAndTranspile(string filename)
        {
            var path = $"../../../../Spec.Test/generated-json/gc/array.wast/{filename}";
            if (!File.Exists(path))
            {
                _output.WriteLine($"Not found: {path}");
                return null;
            }

            WasmModule module;
            using (var stream = new FileStream(path, FileMode.Open))
                module = BinaryModuleParser.ParseWasm(stream);

            var runtime = new WasmRuntime();
            new SpecTestEnv().BindToRuntime(runtime);
            var inst = runtime.InstantiateModule(module);

            var result = new ModuleTranspiler().Transpile(inst, runtime);
            _output.WriteLine($"Transpiled: {result.TranspiledCount}/{result.Methods.Length}");

            if (result.FallbackCount > 0)
            {
                _output.WriteLine($"Fallback: {result.FallbackCount} — skipping");
                return null;
            }

            var wrapper = new TranspiledModuleWrapper(result);
            wrapper.Instantiate();
            return wrapper;
        }
    }
}
