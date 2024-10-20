using System;
using System.Collections.Concurrent;
using Wacs.Core.Attributes;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.OpCodes
{
    public static class OpCodeExtensions
    {
        private static readonly ConcurrentDictionary<OpCode, string> MnemonicCache00 = new();
        private static readonly ConcurrentDictionary<GcCode, string> MnemonicCacheFB = new();
        private static readonly ConcurrentDictionary<ExtCode, string> MnemonicCacheFC = new();
        private static readonly ConcurrentDictionary<SimdCode, string> MnemonicCacheFD = new();
        private static readonly ConcurrentDictionary<AtomCode, string> MnemonicCacheFE = new();

        /// <summary>
        /// Retrieves the WAT mnemonic associated with the given opcode.
        /// </summary>
        /// <param name="opcode">The opcode for which to retrieve the mnemonic.</param>
        /// <returns>The WAT mnemonic if available; otherwise, null.</returns>
        public static string GetMnemonic(this ByteCode opcode) =>
            opcode.x00 switch {
                OpCode.FB => opcode.xFB.GetMnemonic(),
                OpCode.FC => opcode.xFC.GetMnemonic(),
                OpCode.FD => opcode.xFD.GetMnemonic(),
                OpCode.FE => opcode.xFE.GetMnemonic(),
                _ => opcode.x00.GetMnemonic()
            };

        public static string LookUp<T>(T op)
        where T : Enum
        {
            var type = typeof(T);
            var memberInfo = type.GetMember(op.ToString());
            if (memberInfo.Length <= 0)
                return $"undefined {op}";

            var attributes = memberInfo[0].GetCustomAttributes(typeof(OpCodeAttribute), false);
            if (attributes.Length > 0)
            {
                return ((OpCodeAttribute)attributes[0]).Mnemonic;
            }

            return $"undefined {op}";
        }

        public static string GetMnemonic(this OpCode opcode) => MnemonicCache00.GetOrAdd(opcode, LookUp);
        public static string GetMnemonic(this GcCode opcode) => MnemonicCacheFB.GetOrAdd(opcode, LookUp);
        public static string GetMnemonic(this ExtCode opcode) => MnemonicCacheFC.GetOrAdd(opcode, LookUp);
        public static string GetMnemonic(this SimdCode opcode) => MnemonicCacheFD.GetOrAdd(opcode, LookUp);
        public static string GetMnemonic(this AtomCode opcode) => MnemonicCacheFE.GetOrAdd(opcode, LookUp);
    }
}