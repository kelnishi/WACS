using Wacs.Core;
using Wacs.Core.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Spec.Test
{
    public class WastTests
    {
        private readonly ITestOutputHelper _output;

        public WastTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [ClassData(typeof(WastJsonTestData))]
        public void RunWast(WastJson.WastJson file)
        {
            _output.WriteLine($"Running test:{file.TestName}");
            SpecTestEnv env = new SpecTestEnv();
            WasmRuntime runtime = new();
            env.BindToRuntime(runtime);
            
            Module? module = null;
            foreach (var command in file.Commands)
            {
                try
                {
                    _output.WriteLine($"    {command}");
                    var warnings = command.RunTest(file, ref runtime, ref module);
                    foreach (var error in warnings)
                    {
                        _output.WriteLine($"Warning: {error}");
                    }
                }
                catch (TestException exc)
                {
                    Assert.Fail(exc.Message);
                }
            }
        }
    }
}