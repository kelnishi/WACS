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
        public MemoryType[] Memories { get; internal set; } = null!;

        public List<MemoryType> Mems => Memories.ToList();
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.8 Memory Section
        /// </summary>
        private static MemoryType[] ParseMemorySection(BinaryReader reader) =>
            reader.ParseVector(MemoryType.Parse);
    }
}