using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CommandType
    {
        [EnumMember(Value = "module")]
        Module,

        [EnumMember(Value = "assert_return")]
        AssertReturn,

        [EnumMember(Value = "assert_trap")]
        AssertTrap,

        [EnumMember(Value = "assert_exhaustion")]
        AssertExhaustion,

        [EnumMember(Value = "assert_invalid")]
        AssertInvalid,

        [EnumMember(Value = "assert_malformed")]
        AssertMalformed,

        [EnumMember(Value = "assert_unlinkable")]
        AssertUnlinkable,

        [EnumMember(Value = "assert_uninstantiable")]
        AssertUninstantiable,

        [EnumMember(Value = "invoke")]
        Invoke,

        [EnumMember(Value = "get")]
        Get,

        [EnumMember(Value = "set")]
        Set,

        [EnumMember(Value = "start")]
        Start,

        [EnumMember(Value = "assert_return_canonical_nans")]
        AssertReturnCanonicalNans,

        [EnumMember(Value = "assert_return_arithmetic_nans")]
        AssertReturnArithmeticNans,

        [EnumMember(Value = "assert_return_detached")]
        AssertReturnDetached,

        [EnumMember(Value = "assert_terminated")]
        AssertTerminated,

        [EnumMember(Value = "assert_undefined")]
        AssertUndefined,

        [EnumMember(Value = "assert_exclude_from_must")]
        AssertExcludeFromMust,

        [EnumMember(Value = "module_instance")]
        ModuleInstance,

        [EnumMember(Value = "module_exclusive")]
        ModuleExclusive,

        [EnumMember(Value = "pump")]
        Pump,

        [EnumMember(Value = "maybe")]
        Maybe,
    }
}