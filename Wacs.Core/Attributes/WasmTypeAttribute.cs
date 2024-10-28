using System;
using Wacs.Core.Types;

namespace Wacs.Core.Attributes
{
    /// <summary>
    /// Decorate your host function parameter types for automatic conversion
    /// Enum types will get converted as numerics.
    /// Structs must implement ITypeConvertable to provide marshaling conversion from numerics.
    /// </summary>
    public class WasmTypeAttribute : Attribute
    {
        public WasmTypeAttribute(string typename)
        {
            try
            {
                Type = Enum.Parse<ValType>(typename);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Invalid ValType: {typename}");
            }
        }

        public ValType Type { get; set; }
    }

    public static class WasmTypeExtension
    {
        public static ValType? GetWasmType(this Type type)
        {
            var attribute = (WasmTypeAttribute)Attribute.GetCustomAttribute(type, typeof(WasmTypeAttribute));
            return attribute?.Type;
        }
    }
}