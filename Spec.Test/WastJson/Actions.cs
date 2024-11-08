using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wacs.Core;
using Wacs.Core.Runtime;

namespace Spec.Test.WastJson
{
    public class InvokeAction : IAction
    {
        [JsonPropertyName("module")] public string Module { get; set; } = "";

        // [JsonPropertyName("expected")] public List<Argument> Expected { get; set; } = new();
        public ActionType Type => ActionType.Invoke;

        [JsonPropertyName("field")] public string Field { get; set; } = "";

        [JsonPropertyName("args")] public List<Argument> Args { get; set; } = new();

        public override string ToString() => $"{Field} - Args: {string.Join(", ", Args)}";

        public Value[] Invoke(ref WasmRuntime runtime, ref Module? module)
        {
            FuncAddr? addr = null;
            string moduleName = module?.Name ?? "";
            if (!string.IsNullOrEmpty(Module))
            {
                moduleName = Module;
                if (runtime.TryGetExportedFunction((Module, Field), out var a))
                    addr = a;
            }
            else
            {
                if (runtime.TryGetExportedFunction(Field, out var a))
                    addr = a;
            }
            if (addr == null)
                throw new InvalidDataException(
                    $"Could not get exported function {moduleName}.{Field}");    
                    
            //Compute type from action.Args and action.Expected
            var invoker = runtime.CreateStackInvoker(addr.Value);

            var pVals = Args.Select(arg => arg.AsValue).ToArray();
            return invoker(pVals);
        }
    }
    
    public class GetAction : IAction
    {
        [JsonPropertyName("expected")]
        public List<Argument> Expected {
            get => null;
            set => throw new NotSupportedException("GetAction does not support results.");
        }

        public ActionType Type => ActionType.Get;

        [JsonPropertyName("field")] public string Field { get; set; } = "";

        public List<Argument> Args
        {
            get => null;
            set => throw new NotSupportedException("GetAction does not support arguments.");
        }
    }
    
    public class ActionJsonConverter : JsonConverter<IAction>
    {
        public override IAction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions? options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            string? typeString = root.GetProperty("type").GetString();
            ActionType type = EnumHelper.GetEnumValueFromString<ActionType>(typeString);

            IAction? action = type switch
            {
                ActionType.Invoke => JsonSerializer.Deserialize<InvokeAction>(root.GetRawText(), options),
                ActionType.Get => JsonSerializer.Deserialize<GetAction>(root.GetRawText(), options),
                _ => throw new NotSupportedException($"Action type '{type}' is not supported.")
            };

            return action;
        }

        public override void Write(Utf8JsonWriter writer, IAction value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, (object)value, options);
        }
    }
}