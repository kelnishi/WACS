using System;

namespace Wacs.Core.Attributes
{
    /// <summary>
    /// Attribute to associate metadata with opcodes, such as WAT mnemonics.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OpCodeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpCodeAttribute"/> class.
        /// </summary>
        /// <param name="mnemonic">The WAT mnemonic associated with the opcode.</param>
        public OpCodeAttribute(string mnemonic)
        {
            Mnemonic = mnemonic;
        }

        /// <summary>
        /// The mnemonic used in the WebAssembly Text Format (WAT).
        /// </summary>
        public string Mnemonic { get; }
    }
}