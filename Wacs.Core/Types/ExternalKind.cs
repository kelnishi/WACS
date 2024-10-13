using System.IO;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 5.5.5 Import Section
    /// Represents the kinds of external values that can be exported or imported.
    /// </summary>
    public enum ExternalKind : byte
    {
        /// <summary>
        /// A function external kind.
        /// </summary>
        Function = 0x00,

        /// <summary>
        /// A table external kind.
        /// </summary>
        Table = 0x01,

        /// <summary>
        /// A memory external kind.
        /// </summary>
        Memory = 0x02,

        /// <summary>
        /// A global external kind.
        /// </summary>
        Global = 0x03
    }

    public static class ExternalKindParser
    {
        /// <summary>
        /// @Spec 5.5.5 Import Section
        /// </summary>
        public static ExternalKind Parse(BinaryReader reader) =>
            (ExternalKind)(byte)reader.ReadByte() switch
            {
                ExternalKind.Function => ExternalKind.Function,
                ExternalKind.Table => ExternalKind.Table,
                ExternalKind.Memory => ExternalKind.Memory,
                ExternalKind.Global => ExternalKind.Global,
                _ => throw new InvalidDataException($"Invalid Import kind type at offset {reader.BaseStream.Position}.")
            };
    }
}