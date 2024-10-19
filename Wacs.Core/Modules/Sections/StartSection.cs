using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 2.5.9. Start Function
    /// </summary>
    public partial class Module
    {
        public FuncIdx StartIndex { get; internal set; } = FuncIdx.Default;
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.11 Start Section
        /// </summary>
        private static FuncIdx ParseStartSection(BinaryReader reader) =>
            (FuncIdx)reader.ReadLeb128_u32();
    }
}