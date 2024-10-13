using System;
using System.IO;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 2.5.9. Start Function
    /// </summary>
    public partial class Module
    {
        public UInt32 StartIndex { get; internal set; } = uint.MaxValue;
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.11 Start Section
        /// </summary>
        private static UInt32 ParseStartSection(BinaryReader reader) =>
            reader.ReadLeb128_u32();

    }
}