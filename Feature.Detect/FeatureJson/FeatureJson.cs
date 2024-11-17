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

using System.Text.Json.Serialization;

namespace Feature.Detect.FeatureJson;

public class FeatureJson
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }
    
    public string? Path { get; set; }
    
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
        return $"Name: {Name}\n Proposal: {Proposal}\n Features: {string.Join(", ", Features ?? new List<string>())}\n Module: {Module}\n Options: {Options}";
    }
    
}

public class Options
{
    [JsonPropertyName("builtins")]
    public List<string>? Builtins { get; set; }
}