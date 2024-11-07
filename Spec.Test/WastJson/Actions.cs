using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public class InvokeAction : IAction
    {
        // [JsonPropertyName("expected")] public List<Argument> Expected { get; set; } = new();
        public ActionType Type => ActionType.Invoke;

        [JsonPropertyName("field")] public string Field { get; set; } = "";

        [JsonPropertyName("args")] public List<Argument> Args { get; set; } = new();

        public override string ToString() => $"{Field} - Args: {string.Join(", ", Args)}";
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