using System;
using System.IO;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.5 Result Types
    /// </summary>
    public class ResultType
    {
        public ValType[] Types { get; set; }
        
        public uint Length => (uint)(Types?.Length?? 0);
    
        public string ToNotation() => $"[{string.Join(" ",Types)}]";

        private ResultType() => Types = Array.Empty<ValType>();
        public ResultType(ValType single) => Types = new[] { single };
        
        private ResultType(BinaryReader reader) =>
            Types = reader.ParseVector(ValueTypeParser.Parse);

        
        /// <summary>
        /// @Spec 5.3.5 Result Types
        /// </summary>
        public static ResultType Parse(BinaryReader reader) => new(reader);

        public static readonly ResultType Empty = new();
    }
}