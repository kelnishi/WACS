using System;
using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.4. Tables
        /// </summary>
        public TableType[] Tables { get; internal set; } = null!;
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.7 Table Section
        /// </summary>
        private static TableType[] ParseTableSection(BinaryReader reader) =>
            reader.ParseVector(TableType.Parse);
    }
}