using System.IO;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.4 Value Types
    /// Represents the value types used in WebAssembly.
    /// </summary>
    public enum ValType : byte
    {
        // =========================
        // Numeric Types
        // =========================

        /// <summary>
        /// 32-bit integer
        /// </summary>
        I32 = 0x7F,

        /// <summary>
        /// 64-bit integer
        /// </summary>
        I64 = 0x7E,

        /// <summary>
        /// 32-bit floating point
        /// </summary>
        F32 = 0x7D,

        /// <summary>
        /// 64-bit floating point
        /// </summary>
        F64 = 0x7C,

        // =========================
        // Vector Types (SIMD)
        // =========================

        /// <summary>
        /// 128-bit vector
        /// </summary>
        V128 = 0x7B, // (SIMD extension)

        // =========================
        // Reference Types
        // =========================

        /// <summary>
        /// Function reference
        /// </summary>
        Funcref = 0x70,

        /// <summary>
        /// External reference
        /// </summary>
        Externref = 0x6F,

        // =========================
        // Future Types
        // =========================

        // Additional types from future extensions can be added here.
        
        Undefined = 0xFF,
    }

    public static class ValueTypeExtensions
    {
        public static bool IsNumeric(this ValType type) => type switch {
            ValType.I32 => true,
            ValType.I64 => true,
            ValType.F32 => true,
            ValType.F64 => true,
            _ => false
        };

        public static bool IsInteger(this ValType type) => type switch {
            ValType.I32 => true,
            ValType.I64 => true,
            _ => false
        };

        public static bool IsFloat(this ValType type) => type switch {
            ValType.F32 => true,
            ValType.F64 => true,
            _ => false
        };
        
        public static bool IsVector(this ValType type) => type == ValType.V128;

        public static bool IsReference(this ValType type) => type switch {
            ValType.Funcref => true,
            ValType.Externref => true,
            _ => false
        };

        public static ResultType SingleResult(this ValType type) => new(type);

    }
        

    public static class ValueTypeParser
    {
        public static ValType Parse(BinaryReader reader) =>
            (ValType)reader.ReadByte() switch {
                //Numeric Types
                ValType.I32 => ValType.I32,
                ValType.I64 => ValType.I64,
                ValType.F32 => ValType.F32,
                ValType.F64 => ValType.F64,
                //Vector Types (SIMD)
                ValType.V128 => ValType.V128,
                //Reference Types
                ValType.Funcref => ValType.Funcref,
                ValType.Externref => ValType.Externref,
                _ => throw new InvalidDataException($"Invalid value type at offset {reader.BaseStream.Position}.")
            };
    }
}