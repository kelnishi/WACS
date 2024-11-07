using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Wacs.Core;
using Wacs.Core.Runtime;

namespace Spec.Test.WastJson
{
    public interface ICommand
    {
        [JsonPropertyName("type")]
        CommandType Type { get; }

        [JsonPropertyName("line")]
        int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module);
    }
}