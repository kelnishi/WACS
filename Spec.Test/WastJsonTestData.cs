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

        private static string JsonDirectory => Path.Combine(AppContext.BaseDirectory,Configuration["JsonDirectory"]);

        public IEnumerator<object[]> GetEnumerator()
        {
            var files = Directory.GetFiles(JsonDirectory, "*.json", SearchOption.AllDirectories).OrderBy(path => path);
            foreach (var file in files)
            {
                var testData = LoadTestDefinition(file);
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
            return testDefinition;
        }
    }
}