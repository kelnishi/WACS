using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{

    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.6 Globals
        /// </summary>
        public Global[] Globals { get; internal set; } = null!;
        
        /// <summary>
        /// @Spec 2.5.6. Globals
        /// </summary>
        public class Global
        {
            public GlobalType Type;
            public Expression Initializer;
            
            private Global(BinaryReader reader) =>
                (Type, Initializer) = (GlobalType.Parse(reader), Expression.Parse(reader));

            public static Global Parse(BinaryReader reader) => new Global(reader);
        }
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.9 Global Section
        /// </summary>
        private static Module.Global[] ParseGlobalSection(BinaryReader reader) =>
            reader.ParseVector(Module.Global.Parse);
    }
}