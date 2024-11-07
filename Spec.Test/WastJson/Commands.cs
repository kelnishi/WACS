using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;

namespace Spec.Test.WastJson
{
    public class DebuggerBreak : ICommand
    {
        public CommandType Type { get; }
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            Debugger.Break();
            return new();
        }
    }
    
    public class ModuleCommand : ICommand
    {
        private SpecTestEnv _env = new SpecTestEnv();

        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        public CommandType Type => CommandType.Module;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            //Make a clean runtime
            runtime = new WasmRuntime();
            _env.BindToRuntime(runtime);

            var filepath = Path.Combine(testDefinition.Path, Filename);
            using var fileStream = new FileStream(filepath, FileMode.Open);
            module = BinaryModuleParser.ParseWasm(fileStream);
            var modInst = runtime.InstantiateModule(module);
            var moduleName = $"{filepath}"; 
            module.SetName(moduleName);
            runtime.RegisterModule(module.Name, modInst);
            return errors;
        }

        public override string ToString() => $"ModuleCommand {{ Filename = {Filename}, Line = {Line} }}";
    }

    public class RegisterCommand : ICommand
    {
        private SpecTestEnv _env = new SpecTestEnv();

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("as")]
        public string As { get; set; }

        public CommandType Type => CommandType.Module;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();

            var modInst = runtime.GetModule(Name);
            runtime.RegisterModule(As, modInst);
            return errors;
        }

        public override string ToString() => $"RegisterCommand {{ Name = {Name}, As = {As}, Line = {Line} }}";
    }
    
    public class ActionCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.Action;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            var action = Action;
            switch (action.Type)
            {
                case ActionType.Invoke:
                    if (!runtime.TryGetExportedFunction((module.Name, action.Field), out var addr))
                        throw new InvalidDataException(
                            $"Could not get exported function {module.Name}.{action.Field}");
                    //Compute type from action.Args and action.Expected
                    var invoker = runtime.CreateStackInvoker(addr);

                    var pVals = action.Args.Select(arg => arg.AsValue).ToArray();
                    var result = invoker(pVals);
                    break;
            }
            return errors;
        }
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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            var action1 = Action;
            switch (action1.Type)
            {
                case ActionType.Invoke:
                    if (!runtime.TryGetExportedFunction((module.Name, action1.Field), out var addr))
                        throw new InvalidDataException(
                            $"Could not get exported function {module.Name}.{action1.Field}");
                    //Compute type from action.Args and action.Expected
                    var invoker = runtime.CreateStackInvoker(addr);

                    var pVals = action1.Args.Select(arg => arg.AsValue).ToArray();
                    var result = invoker(pVals);
                    if (!result.SequenceEqual(Expected.Select(e => e.AsValue)))
                        throw new TestException(
                            $"Test failed {this} \"{action1.Field}\": Expected [{string.Join(" ", Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");

                    break;
            }
            return errors;
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            var action2 = Action;
            switch (action2.Type)
            {
                case ActionType.Invoke:
                    if (!runtime.TryGetExportedFunction((module?.Name, action2.Field), out var addr))
                        throw new ArgumentException(
                            $"Could not get exported function {module?.Name}.{action2.Field}");
                    //Compute type from action.Args and action.Expected
                    var invoker = runtime.CreateStackInvoker(addr);

                    var pVals = action2.Args.Select(arg => arg.AsValue).ToArray();
                    bool didTrap = false;
                    string trapMessage = "";
                    try
                    {
                        var result = invoker(pVals);
                    }
                    catch (TrapException e)
                    {
                        didTrap = true;
                        trapMessage = e.Message;
                    }

                    if (!didTrap)
                        throw new TestException($"Test failed {this} \"{trapMessage}\"");
                    break;
            }

            return errors;
        }

        public override string ToString() => $"AssertTrapCommand {{ Action = {Action}, Text = {Text}, Line = {Line} }}";
    }

    public class AssertExhaustionCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        public CommandType Type => CommandType.AssertExhaustion;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            var action = Action;
            switch (action.Type)
            {
                case ActionType.Invoke:
                    if (!runtime.TryGetExportedFunction((module.Name, action.Field), out var addr))
                        throw new InvalidDataException(
                            $"Could not get exported function {module.Name}.{action.Field}");
                    //Compute type from action.Args and action.Expected
                    bool didThrow = false;
                    string throwMessage = "";
                    try
                    {
                        var invoker = runtime.CreateStackInvoker(addr);

                        var pVals = action.Args.Select(arg => arg.AsValue).ToArray();
                        var result = invoker(pVals);
                    }
                    catch (WasmRuntimeException exc)
                    {
                        didThrow = true;
                        throwMessage = Text;
                    }
                    if (!didThrow)
                        throw new TestException($"Test failed {this} \"{throwMessage}\"");
                    break;
            }
            return errors;
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            string assertionMessage = "";
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                module = BinaryModuleParser.ParseWasm(fileStream);
                module.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(module);
            }
            catch (ValidationException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (InvalidDataException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (FormatException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }

            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

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


        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            var errors = new List<Exception>();
            if (ModuleType == "text")
                errors.Add(new Exception(
                    $"Assert Malformed line {Line}: Skipping assert_malformed. No WAT parsing."));
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert1 = false;
            string assertionMessage = "";
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                module = BinaryModuleParser.ParseWasm(fileStream);
                module.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(module);
            }
            catch (FormatException exc)
            {
                didAssert1 = true;
                assertionMessage = exc.Message;
            }
            catch (NotSupportedException exc)
            {
                didAssert1 = true;
                assertionMessage = exc.Message;
            }

            if (!didAssert1)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"AssertMalformedCommand {{ Filename = {Filename}, Line = {Line}, Text = {Text}, ModuleType = {ModuleType} }}";
    }

    public class AssertUnlinkableCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertUnlinkable;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertUnlinkableCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertUninstantiableCommand : ICommand
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("module_type")]
        public string ModuleType { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        public CommandType Type => CommandType.AssertUninstantiable;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            string assertionMessage = "";
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                module = BinaryModuleParser.ParseWasm(fileStream);
                module.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(module);
            }
            catch (ValidationException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (InvalidDataException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (FormatException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (TrapException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }

            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"AssertUninstantiableCommand {{ Filename = {Filename}, ModuleType = {ModuleType}, Text = {Text}, Line = {Line} }}";
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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"SetCommand {{ Module = {Module}, Name = {Name}, Value = {Value}, Line = {Line} }}";
    }

    public class StartCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.Start;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

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

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertReturnArithmeticNansCommand {{ Action = {Action}, Expected = [{string.Join(", ", Expected)}], Line = {Line} }}";
    }

    public class AssertReturnDetachedCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.AssertReturnDetached;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertReturnDetachedCommand {{ Action = {Action}, Line = {Line} }}";
    }

    public class AssertTerminatedCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertTerminated;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertTerminatedCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertUndefinedCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertUndefined;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertUndefinedCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class AssertExcludeFromMustCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.AssertExcludeFromMust;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"AssertExcludeFromMustCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class ModuleInstanceCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.ModuleInstance;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"ModuleInstanceCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class ModuleExclusiveCommand : ICommand
    {
        [JsonPropertyName("module")]
        public string Module { get; set; }

        public CommandType Type => CommandType.ModuleExclusive;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"ModuleExclusiveCommand {{ Module = {Module}, Line = {Line} }}";
    }

    public class PumpCommand : ICommand
    {
        [JsonPropertyName("action")]
        public IAction Action { get; set; }

        public CommandType Type => CommandType.Pump;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"PumpCommand {{ Action = {Action}, Line = {Line} }}";
    }

    public class MaybeCommand : ICommand
    {
        [JsonPropertyName("command")]
        public ICommand Command { get; set; }

        public CommandType Type => CommandType.Maybe;

        [JsonPropertyName("line")]
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime? runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

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
                CommandType.DebuggerBreak =>JsonSerializer.Deserialize<DebuggerBreak>(root.GetRawText(), options), 
                CommandType.Module => JsonSerializer.Deserialize<ModuleCommand>(root.GetRawText(), options),
                CommandType.Register => JsonSerializer.Deserialize<RegisterCommand>(root.GetRawText(), options),
                CommandType.Action => JsonSerializer.Deserialize<ActionCommand>(root.GetRawText(), options),
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