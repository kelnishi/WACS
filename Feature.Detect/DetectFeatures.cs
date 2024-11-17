using Spec.Test;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Xunit;

namespace Feature.Detect;

public class DetectFeatures
{
    [Theory]
    [ClassData(typeof(FeatureDetectTestData))]
    public void Detect(FeatureJson.FeatureJson file)
    {
        if (!string.IsNullOrEmpty(file.Module))
        {
            try
            {
                var runtime = new WasmRuntime();

                //Mutable globals
                var mutableGlobal = new GlobalType(ValType.I32, Mutability.Mutable);
                runtime.BindHostGlobal(("a", "b"), mutableGlobal, 1);
                
                var filepath = Path.Combine(file.Path, file.Module);
                using var fileStream = new FileStream(filepath, FileMode.Open);
                var module = BinaryModuleParser.ParseWasm(fileStream);
                var modInst = runtime.InstantiateModule(module);
                var moduleName = !string.IsNullOrEmpty(file.Name)?file.Name:$"{filepath}";
                module.SetName(moduleName);
            }
            catch (Exception e)
            {
                Assert.Fail($"{file.Name} support not detected.\n{e}");    
            }
            
        }
        else
        {
            Assert.Fail($"{file.Name} not supported.");
        }
    }
}