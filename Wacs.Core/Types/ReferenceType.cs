using System;
using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.3 Reference Types
    /// Represents the reference types used in table elements.
    /// </summary>
    public enum ReferenceType : byte
    {
        /// <summary>
        /// Function reference.
        /// </summary>
        Funcref = 0x70,

        /// <summary>
        /// External reference.
        /// </summary>
        Externref = 0x6F,

        // Additional reference types from future proposals can be added here.
    }
    
    public static class ReferenceTypeParser
    {
        public static ReferenceType Parse(BinaryReader reader) =>
            (ReferenceType)(byte)reader.ReadSByte() switch
            {
                ReferenceType.Funcref => ReferenceType.Funcref,
                ReferenceType.Externref => ReferenceType.Externref,
                _ => throw new Exception($"Invalid reference type: {reader.ReadSByte():x8}"),
            };
    }
}