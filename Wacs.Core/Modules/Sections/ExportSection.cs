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
            public string Name { get; internal set; } = null!;

            public ExportDesc Desc { get; internal set; } = null!;
        }
        
        
        public abstract class ExportDesc
        {
            public class FuncDesc : ExportDesc
            {
                public FuncIdx FunctionIndex { get; internal set; }
            }

            public class TableDesc : ExportDesc
            {
                public TableIdx TableIndex { get; internal set; }
            }

            public class MemDesc : ExportDesc
            {
                public MemIdx MemoryIndex { get; internal set; }
            }

            public class GlobalDesc : ExportDesc
            {
                public GlobalIdx GlobalIndex { get; internal set; }
            }
            
        }
    }
    
    public static partial class ModuleParser
    {
        private static Module.ExportDesc ParseExportDesc(BinaryReader reader) =>
            ExternalKindParser.Parse(reader) switch {
                ExternalKind.Function => new Module.ExportDesc.FuncDesc { FunctionIndex = (FuncIdx)reader.ReadLeb128_u32()},
                ExternalKind.Table => new Module.ExportDesc.TableDesc { TableIndex = (TableIdx)reader.ReadLeb128_u32()},
                ExternalKind.Memory => new Module.ExportDesc.MemDesc { MemoryIndex = (MemIdx)reader.ReadLeb128_u32() },
                ExternalKind.Global => new Module.ExportDesc.GlobalDesc { GlobalIndex = (GlobalIdx)reader.ReadLeb128_u32()},
                var kind => throw new InvalidDataException($"Malformed Module Export section {kind} at {reader.BaseStream.Position - 1}")
            };
            
        private static Module.Export ParseExport(BinaryReader reader) =>
            new Module.Export {
                Name = reader.ReadUTF8String(),
                Desc = ParseExportDesc(reader)
            };
        
        /// <summary>
        /// @Spec 5.5.10 Export Section
        /// </summary>
        private static Module.Export[] ParseExportSection(BinaryReader reader) =>
            reader.ParseVector(ParseExport);

    }
}