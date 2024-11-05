using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActionType
    {
        [EnumMember(Value = "invoke")]
        Invoke,

        [EnumMember(Value = "get")]
        Get,
    }

}