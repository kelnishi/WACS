using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public interface IAction
    {
        [JsonPropertyName("type")]
        ActionType Type { get; }

        [JsonPropertyName("field")]
        string Field { get; set; }

        [JsonPropertyName("args")]
        List<Argument> Args { get; set; }
    }
}