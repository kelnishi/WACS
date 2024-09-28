using System;
using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.10. Exports
        /// </summary>
        public Export[] Exports { get; internal set; } = null!;

        /// <summary>
        /// @Spec 2.5.10. Exports
        /// </summary>
        public class Export
        {
            public string Name { get; internal set; }
            public ExternalKind Desc { get; internal set; }

            public UInt32 Index { get; internal set; }

            private Export(BinaryReader reader) =>
                (Name, Desc, Index) = (
                    reader.ReadUTF8String(),
                    (ExternalKind)reader.ReadByte(),
                    reader.ReadLeb128_u32()
                );

            public static Export Parse(BinaryReader reader) => new Export(reader);
        }
        
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.10 Export Section
        /// </summary>
        private static Module.Export[] ParseExportSection(BinaryReader reader) =>
            reader.ParseVector(Module.Export.Parse);

    }
}