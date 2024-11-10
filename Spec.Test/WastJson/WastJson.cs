using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public class WastJson
    {
        [JsonPropertyName("source_filename")]
        public string SourceFilename { get; set; }

        [JsonPropertyName("commands")]
        public List<ICommand> Commands { get; set; }

        public string TestName => 
            System.IO.Path.GetFileName(SourceFilename);

        public string Path { get; set; }

        public override string ToString() => $"{SourceFilename}";
    }
}