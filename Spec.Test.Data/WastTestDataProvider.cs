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
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Spec.Test.WastJson;

namespace Spec.Test
{
    /// <summary>
    /// Provides test data from the WebAssembly spec test suite.
    /// Shared between Spec.Test (interpreter tests) and Wacs.Transpiler.Test (transpiler tests).
    ///
    /// Reads configuration from a testsettings.json file:
    ///   JsonDirectory: relative path to generated-json directory
    ///   Single: run only a single named test (null = all)
    ///   SkipWasts: list of test names to skip
    ///   TraceExecution: enable instruction-level logging
    /// </summary>
    public class WastTestDataProvider
    {
        private readonly IConfiguration _configuration;

        public WastTestDataProvider(string? settingsPath = null)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory);

            if (settingsPath != null)
                builder.AddJsonFile(settingsPath, optional: false, reloadOnChange: true);
            else
                builder.AddJsonFile("testsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public string JsonDirectory => Path.Combine(
            AppContext.BaseDirectory,
            _configuration["JsonDirectory"] ?? "");

        public string SingleTest => _configuration["Single"] ?? "";

        public HashSet<string> SkipWasts =>
            _configuration
                .GetSection("SkipWasts")
                .GetChildren()
                .Select(cfg => cfg.Value ?? "")
                .ToHashSet();

        public bool TraceExecution => _configuration["TraceExecution"] == "True";

        /// <summary>
        /// Load all test definitions, filtered by configuration.
        /// </summary>
        public IEnumerable<WastJson.WastJson> GetTestDefinitions()
        {
            if (!Directory.Exists(JsonDirectory))
                yield break;

            var files = Directory.GetFiles(JsonDirectory, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path);

            foreach (var file in files)
            {
                var testData = LoadTestDefinition(file);

                if (!string.IsNullOrEmpty(SingleTest) && SingleTest != testData.TestName)
                    continue;

                if (SkipWasts.Contains(testData.TestName))
                    continue;

                yield return testData;
            }
        }

        /// <summary>
        /// Load a single test definition from a JSON file.
        /// </summary>
        public WastJson.WastJson LoadTestDefinition(string jsonPath)
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

        /// <summary>
        /// Get all .wasm binary files from the test directory.
        /// Useful for transpiler tests that operate on binaries directly.
        /// </summary>
        public IEnumerable<string> GetWasmFiles()
        {
            if (!Directory.Exists(JsonDirectory))
                yield break;

            foreach (var file in Directory.GetFiles(JsonDirectory, "*.wasm", SearchOption.AllDirectories)
                         .OrderBy(f => f))
            {
                yield return file;
            }
        }
    }
}
