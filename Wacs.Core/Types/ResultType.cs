using System.IO;
using System.Linq;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.5 Result Types
    /// </summary>
    public class ResultType
    {
        public ValType[] Types { get; set; } = null!;
        
        public uint Length => (uint)(Types?.Length?? 0);
    
        public string ToNotation() => $"[{string.Join(" ",Types)}]";
        
        private ResultType(BinaryReader reader) =>
            Types = reader.ParseVector(ValueTypeParser.Parse);

        public ResultType(ValType single) => Types = new[] { single };
        
        /// <summary>
        /// @Spec 5.3.5 Result Types
        /// </summary>
        public static ResultType Parse(BinaryReader reader) => new ResultType(reader);
    }
}