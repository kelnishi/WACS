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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Spec.Test.WastJson;

namespace Spec.Test
{
    public class WastJsonTestData : IEnumerable<object[]>
    {
        private static readonly IConfiguration Configuration;

        static WastJsonTestData()
        {
            // Use ConfigurationFixture to get the JSON directory path
            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory) // Set to current directory
                .AddJsonFile("testsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }

        private static string JsonDirectory => Path.Combine(AppContext.BaseDirectory, Configuration["JsonDirectory"] ?? "");

        public static string SingleTest => Configuration["Single"] ?? "";
        public static HashSet<string> SkipWasts =>
            Configuration
                .GetSection("SkipWasts")
                .GetChildren()
                .Select(cfg => cfg.Value ?? "")
                .ToHashSet();

        public static bool TraceExecution => Configuration["TraceExecution"] == "True";
        
        public IEnumerator<object[]> GetEnumerator()
        {
            var files = Directory.GetFiles(JsonDirectory, "*.json", SearchOption.AllDirectories).OrderBy(path => path);
            foreach (var file in files)
            {
                var testData = LoadTestDefinition(file);

                if (!string.IsNullOrEmpty(SingleTest) && SingleTest != testData.TestName)
                    continue;
                
                if (SkipWasts.Contains(testData.TestName))
                    continue;
                        
                yield return new object[] { testData };
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        static WastJson.WastJson LoadTestDefinition(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);

            var options = new JsonSerializerOptions
            {
                Converters =
                {
                    new CommandJsonConverter(),
                    new ActionJsonConverter(),
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                },
                PropertyNameCaseInsensitive = true
            };

            var testDefinition = JsonSerializer.Deserialize<WastJson.WastJson>(json, options);
            if (testDefinition == null)
                throw new JsonException($"Error while parsing {jsonPath}");

            testDefinition.Path = Path.GetDirectoryName(jsonPath)!;
            testDefinition.TraceExecution = TraceExecution;
            return testDefinition;
        }
    }
}