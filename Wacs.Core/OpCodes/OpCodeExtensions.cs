using System.Collections.Concurrent;
using System.IO;
using Wacs.Core.Attributes;

namespace Wacs.Core.OpCodes
{
    public static class OpCodeExtensions
    {
        private static readonly ConcurrentDictionary<OpCode, string> MnemonicCache = new();

        /// <summary>
        /// Retrieves the WAT mnemonic associated with the given opcode.
        /// </summary>
        /// <param name="opcode">The opcode for which to retrieve the mnemonic.</param>
        /// <returns>The WAT mnemonic if available; otherwise, null.</returns>
        public static string GetMnemonic(this OpCode opcode)
        {
            return MnemonicCache.GetOrAdd(opcode, op =>
            {
                var type = typeof(OpCode);
                var memberInfo = type.GetMember(op.ToString());
                if (memberInfo.Length <= 0)
                    throw new InvalidDataException($"Cannot convert opcode {opcode} to mnemonic text");

                var attributes = memberInfo[0].GetCustomAttributes(typeof(OpCodeAttribute), false);
                if (attributes.Length > 0)
                {
                    return ((OpCodeAttribute)attributes[0]).Mnemonic;
                }

                throw new InvalidDataException($"Cannot convert opcode {opcode} to mnemonic text");
            });
        }
    }
}