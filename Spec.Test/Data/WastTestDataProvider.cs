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

        /// <summary>
        /// Root directory holding raw <c>.wast</c> files. When set,
        /// <see cref="GetTestDefinitions"/> uses
        /// <see cref="WastScriptAdapter"/> to parse each file with
        /// WACS's own WAT/WAST parser and produces in-memory
        /// <see cref="WastJson.WastJson"/> instances — no external
        /// <c>wasm-tools</c> / <c>wast2json</c> precompile step.
        /// When empty/missing, falls back to the legacy JSON-on-disk
        /// pipeline (<see cref="JsonDirectory"/>).
        /// </summary>
        public string WastDirectory => Path.Combine(
            AppContext.BaseDirectory,
            _configuration["WastDirectory"] ?? "");

        public string SingleTest => _configuration["Single"] ?? "";

        public HashSet<string> SkipWasts =>
            _configuration
                .GetSection("SkipWasts")
                .GetChildren()
                .Select(cfg => cfg.Value ?? "")
                .ToHashSet();

        public bool TraceExecution => _configuration["TraceExecution"] == "True";

        /// <summary>
        /// Load all test definitions, filtered by configuration. When
        /// <see cref="WastDirectory"/> is configured, parses .wast
        /// files in-process via <see cref="WastScriptAdapter"/>;
        /// otherwise falls back to the JSON-on-disk pipeline.
        /// </summary>
        public IEnumerable<WastJson.WastJson> GetTestDefinitions()
        {
            if (!string.IsNullOrEmpty(_configuration["WastDirectory"])
                && Directory.Exists(WastDirectory))
            {
                foreach (var td in GetWastTestDefinitions())
                    yield return td;
                yield break;
            }

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

        private IEnumerable<WastJson.WastJson> GetWastTestDefinitions()
        {
            var files = Directory.GetFiles(WastDirectory, "*.wast", SearchOption.AllDirectories)
                .OrderBy(path => path);

            foreach (var file in files)
            {
                WastJson.WastJson testData;
                try
                {
                    testData = WastScriptAdapter.FromWastFile(file, TraceExecution);
                }
                catch (Exception e)
                {
                    // No silent skips. Surface the parse failure as a
                    // visible test failure so the WAT/WAST parser gap
                    // shows up in CI rather than vanishing — every
                    // missing instruction or construct is tracked
                    // until the WAT parser reaches binary-parser parity.
                    var name = Path.GetFileName(file);
                    testData = new WastJson.WastJson
                    {
                        SourceFilename = file,
                        Path = Path.GetDirectoryName(file) ?? string.Empty,
                        Commands = new List<WastJson.ICommand>
                        {
                            new WastJson.ScriptParseFailureCommand
                            {
                                ParseError = $"{e.GetType().Name}: {e.Message}",
                            }
                        },
                        TraceExecution = TraceExecution,
                    };
                }

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
