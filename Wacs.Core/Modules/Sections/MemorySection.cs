using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.5. Memories
        /// </summary>
        public List<MemoryType> Memories { get; internal set; } = null!;

    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.8 Memory Section
        /// </summary>
        private static List<MemoryType> ParseMemorySection(BinaryReader reader) =>
            reader.ParseList(MemoryType.Parse);
    }
}