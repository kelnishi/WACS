using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public interface ICommand
    {
        [JsonPropertyName("type")]
        CommandType Type { get; }

        [JsonPropertyName("line")]
        int Line { get; set; }
    }
}