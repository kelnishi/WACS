using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public class Expected
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }
}