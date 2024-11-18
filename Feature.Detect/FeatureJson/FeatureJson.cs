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

using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;

namespace Feature.Detect.FeatureJson;

public class FeatureJson
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }
    
    public string? Path { get; set; }
    
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("options")]
    public Options? Options { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("proposal")]
    public string? Proposal { get; set; }
    
    [JsonPropertyName("features")]
    public List<string>? Features { get; set; }  // New property for features list

    public override string ToString()
    {
        var feats = (Features ?? new List<string>()).Select(f => $"\"{f}\"");
        var sb = new StringBuilder();
        sb.Append("{")
            .Append("\"Name\": \"").Append(Name).Append("\",\n")
            .Append("\"Proposal\": \"").Append(Proposal).Append("\",\n")
            .Append("\"Features\": [").Append(string.Join(", ", feats)).Append("],\n")
            .Append("\"Id\": \"").Append(Id).Append("\"\n")
            .Append("}");
        return sb.ToString();
    }
    
}

public class Options
{
    [JsonPropertyName("builtins")]
    public List<string>? Builtins { get; set; }
}