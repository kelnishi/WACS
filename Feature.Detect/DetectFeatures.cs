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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Xunit;

namespace Feature.Detect
{
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

                    var filepath = Path.Combine(file.Path!, file.Module);
                    using var fileStream = new FileStream(filepath, FileMode.Open);
                    var module = BinaryModuleParser.ParseWasm(fileStream);
                    var modInst = runtime.InstantiateModule(module);
                    var moduleName = !string.IsNullOrEmpty(file.Name) ? file.Name : $"{filepath}";
                    module.SetName(moduleName);
                }
                catch (Exception e)
                {
                    Assert.Fail($"{file.Name} support not detected.\n{e}");
                }

            }
            else
            {
                var supportedJsParadigms = new List<string>
                {
                    "jspi"
                };

                var supported = file.Features?.All(feature => supportedJsParadigms.Contains(feature)) ?? false;
                Assert.True(supported, $"{file.Name} not supported.");
            }
        }
    }
}