using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.2. Types
        /// </summary>
        public List<FunctionType> Types { get; internal set; }  = null!;
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.4 Type Section
        /// </summary>
        private static List<FunctionType> ParseTypeSection(BinaryReader reader) => 
            reader.ParseList(FunctionType.Parse,
                postProcess: AnnotateWhileParsing ? (i, newType) => newType.Id = $"{i}" : null);
    }
}