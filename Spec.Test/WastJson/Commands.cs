using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    public class ModuleCommand : ICommand
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        public CommandType Type => CommandType.Module;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"ModuleCommand {{ Filename = {Filename}, Line = {Line} }}";
    }

    public class AssertReturnCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        [JsonPropertyName("expected")]
        public List<Argument> Expected { get; set; }

        public CommandType Type => CommandType.AssertReturn;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertReturnCommand {{ Action = {Action}, Expected = [{string.Join(", ", Expected)}], Line = {Line} }}";
    }
    
    public class AssertTrapCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        public CommandType Type => CommandType.AssertTrap;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertTrapCommand {{ Action = {Action}, Text = {Text}, Line = {Line} }}";
    }

    public class AssertExhaustionCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.AssertExhaustion;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertExhaustionCommand {{ Action = {Action}, Line = {Line} }}";
    }

    public class AssertInvalidCommand : ICommand
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("module_type")]
        public string ModuleType { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        public CommandType Type => CommandType.AssertInvalid;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertInvalidCommand {{ Filename = {Filename}, ModuleType = {ModuleType}, Text = {Text}, Line = {Line} }}";
    }

    public class AssertMalformedCommand : ICommand
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("module_type")]
        public string ModuleType { get; set; }

        public CommandType Type => CommandType.AssertMalformed;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertMalformedCommand {{ Filename = {Filename}, Line = {Line}, Text = {Text}, ModuleType = {ModuleType} }}";
    }

    public class AssertUnlinkableCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertUnlinkable;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertUnlinkableCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertUninstantiableCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertUninstantiable;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertUninstantiableCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class InvokeCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("args")]
        public List<object> Args { get; set; } = new List<object>();

        public CommandType Type => CommandType.Invoke;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"InvokeCommand {{ Module = {Module}, Name = {Name}, Args = [{string.Join(", ", Args)}], Line = {Line} }}";
    }

    public class GetCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        public CommandType Type => CommandType.Get;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"GetCommand {{ Module = {Module}, Name = {Name}, Line = {Line} }}";
    }

    public class SetCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("value")]
        public object Value { get; set; }

        public CommandType Type => CommandType.Set;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"SetCommand {{ Module = {Module}, Name = {Name}, Value = {Value}, Line = {Line} }}";
    }

    public class StartCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.Start;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"StartCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertReturnCanonicalNansCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        [JsonPropertyName("expected")]
        public List<object> Expected { get; set; } = new List<object>();

        public CommandType Type => CommandType.AssertReturnCanonicalNans;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertReturnCanonicalNansCommand {{ Action = {Action}, Expected = [{string.Join(", ", Expected)}], Line = {Line} }}";
    }

    public class AssertReturnArithmeticNansCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        [JsonPropertyName("expected")]
        public List<object> Expected { get; set; } = new List<object>();

        public CommandType Type => CommandType.AssertReturnArithmeticNans;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertReturnArithmeticNansCommand {{ Action = {Action}, Expected = [{string.Join(", ", Expected)}], Line = {Line} }}";
    }

    public class AssertReturnDetachedCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.AssertReturnDetached;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertReturnDetachedCommand {{ Action = {Action}, Line = {Line} }}";
    }

    public class AssertTerminatedCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertTerminated;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertTerminatedCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertUndefinedCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertUndefined;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertUndefinedCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertExcludeFromMustCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertExcludeFromMust;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"AssertExcludeFromMustCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class ModuleInstanceCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.ModuleInstance;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"ModuleInstanceCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class ModuleExclusiveCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.ModuleExclusive;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"ModuleExclusiveCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class PumpCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.Pump;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"PumpCommand {{ Action = {Action}, Line = {Line} }}";
    }

    public class MaybeCommand : ICommand
    {
        [JsonPropertyName("command")]
        public ICommand Command { get; set; }

        public CommandType Type => CommandType.Maybe;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public override string ToString() => $"MaybeCommand {{ Command = {Command}, Line = {Line} }}";
    }
    
    public class CommandJsonConverter : JsonConverter<ICommand>
    {
        public override ICommand? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions? options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            string? typeString = root.GetProperty("type").GetString();
            CommandType type = EnumHelper.GetEnumValueFromString<CommandType>(typeString);

            ICommand? command = type switch {
                CommandType.Module => JsonSerializer.Deserialize<ModuleCommand>(root.GetRawText(), options),
                CommandType.AssertReturn => JsonSerializer.Deserialize<AssertReturnCommand>(root.GetRawText(), options),
                CommandType.AssertTrap => JsonSerializer.Deserialize<AssertTrapCommand>(root.GetRawText(), options),
                CommandType.AssertExhaustion => JsonSerializer.Deserialize<AssertExhaustionCommand>(root.GetRawText(), options),
                CommandType.AssertInvalid => JsonSerializer.Deserialize<AssertInvalidCommand>(root.GetRawText(), options),
                CommandType.AssertMalformed => JsonSerializer.Deserialize<AssertMalformedCommand>(root.GetRawText(), options),
                CommandType.AssertUnlinkable => JsonSerializer.Deserialize<AssertUnlinkableCommand>(root.GetRawText(), options),
                CommandType.AssertUninstantiable => JsonSerializer.Deserialize<AssertUninstantiableCommand>(root.GetRawText(), options),
                CommandType.Invoke => JsonSerializer.Deserialize<InvokeCommand>(root.GetRawText(), options),
                CommandType.Get => JsonSerializer.Deserialize<GetCommand>(root.GetRawText(), options),
                CommandType.Set => JsonSerializer.Deserialize<SetCommand>(root.GetRawText(), options),
                CommandType.Start => JsonSerializer.Deserialize<StartCommand>(root.GetRawText(), options),
                CommandType.AssertReturnCanonicalNans => JsonSerializer.Deserialize<AssertReturnCanonicalNansCommand>(root.GetRawText(), options),
                CommandType.AssertReturnArithmeticNans => JsonSerializer.Deserialize<AssertReturnArithmeticNansCommand>(root.GetRawText(), options),
                CommandType.AssertReturnDetached => JsonSerializer.Deserialize<AssertReturnDetachedCommand>(root.GetRawText(), options),
                CommandType.AssertTerminated => JsonSerializer.Deserialize<AssertTerminatedCommand>(root.GetRawText(), options),
                CommandType.AssertUndefined => JsonSerializer.Deserialize<AssertUndefinedCommand>(root.GetRawText(), options),
                CommandType.AssertExcludeFromMust => JsonSerializer.Deserialize<AssertExcludeFromMustCommand>(root.GetRawText(), options),
                CommandType.ModuleInstance => JsonSerializer.Deserialize<ModuleInstanceCommand>(root.GetRawText(), options),
                CommandType.ModuleExclusive => JsonSerializer.Deserialize<ModuleExclusiveCommand>(root.GetRawText(), options),
                CommandType.Pump => JsonSerializer.Deserialize<PumpCommand>(root.GetRawText(), options),
                CommandType.Maybe => JsonSerializer.Deserialize<MaybeCommand>(root.GetRawText(), options),
                
                _ => throw new NotSupportedException($"Command type '{type}' is not supported.")
            };

            return command;
        }

        public override void Write(Utf8JsonWriter writer, ICommand value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, (object)value, options);
        }
    }
}