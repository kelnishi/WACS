using System;
using System.Runtime.Serialization;

namespace Spec.Test.WastJson
{

    public static class EnumHelper
    {
        public static TEnum GetEnumValueFromString<TEnum>(string? value) where TEnum : Enum
        {
            foreach (var field in typeof(TEnum).GetFields())
            {
                if (Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute)
                {
                    if (attribute.Value == value)
                        return (TEnum)field.GetValue(null);
                }
                else
                {
                    if (field.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (TEnum)field.GetValue(null);
                }
            }
            throw new ArgumentException($"Unknown value '{value}' for enum {typeof(TEnum).Name}");
        }
    }
}