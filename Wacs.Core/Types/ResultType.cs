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
        public static readonly ResultType Empty = new();

        private ResultType() => Types = Array.Empty<ValType>();
        public ResultType(ValType single) => Types = new[] { single };

        private ResultType(BinaryReader reader) =>
            Types = reader.ParseVector(ValueTypeParser.Parse);

        public ValType[] Types { get; }

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        public uint Length => (uint)(Types?.Length ?? 0);
        public int Arity => (int)Length;

        public string ToNotation() => $"[{string.Join(" ",Types)}]";

        public bool Matches(ResultType other)
        {
            if (Types.Length != other.Types.Length)
                return false;
            for (int i = 0, l = Types.Length; i < l; ++i)
            {
                if (Types[i] != other.Types[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// @Spec 5.3.5 Result Types
        /// </summary>
        public static ResultType Parse(BinaryReader reader) => new(reader);
    }
}