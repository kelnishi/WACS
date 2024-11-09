using System;
using System.IO;
using System.Linq;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        public CodeDesc[] Codes { get; internal set; } = Array.Empty<CodeDesc>();

        /// <summary>
        /// @Spec 2.5.3 Functions
        /// </summary>
        public class FuncLocalsBody
        {
            private FuncLocalsBody((uint Number, ValType Type)[] compressedLocals, Expression expr) => 
                (CompressedLocals, Body) = (compressedLocals, expr);

            private (uint Number, ValType Type)[] CompressedLocals { get; }
            public Expression Body { get; }

            public long NumberOfLocals => CompressedLocals.Sum(t => t.Number);

            public ValType[] Locals => 
                CompressedLocals
                    .SelectMany(local => Enumerable.Repeat(local.Type, (int)local.Number))
                    .ToArray();

            private static (uint Number, ValType Type) ParseCompressedLocal(BinaryReader reader)
            {
                uint count = reader.ReadLeb128_u32();
                var expr = ValTypeParser.Parse(reader);
                return (count, expr);
            }

            public static FuncLocalsBody Parse(BinaryReader reader) =>
                new(reader.ParseVector(ParseCompressedLocal), Expression.Parse(reader));
        }

        public class CodeDesc
        {
            private CodeDesc(uint size, FuncLocalsBody code) => (Size, Code) = (size, code);
            private uint Size { get; }
            public FuncLocalsBody Code { get; }

            public static CodeDesc Parse(BinaryReader reader)
            {
                uint size = reader.ReadLeb128_u32();
                var start = reader.BaseStream.Position;
                var code = new CodeDesc(size, FuncLocalsBody.Parse(reader));
                var end = start + size;
                if (reader.BaseStream.Position != end)
                    throw new FormatException($"Malformed code size: expected {code.Size:x} bytes, but got {(int)(reader.BaseStream.Position - start):x} at {reader.BaseStream.Position:x}");
                return code;
            }
        }
    }
    
    public static partial class BinaryModuleParser
    {
        public static uint MaximumFunctionLocals = 1024;

        /// <summary>
        /// @Spec 5.5.13. Code Section
        /// </summary>
        private static Module.CodeDesc[] ParseCodeSection(BinaryReader reader) =>
            reader.ParseVector(Module.CodeDesc.Parse);

        /// <summary>
        /// @Spec 5.5.13. Code Section
        /// </summary>
        private static void PatchFuncSection(Module module)
        {
            if (module.Funcs.Count != module.Codes.Length)
                throw new FormatException($"Module functions section count {module.Funcs.Count} must equal the code count {module.Codes.Length}");
            
            for (int i = 0, l = module.Codes.Length; i < l; ++i)
            {
                var localsbody = module.Codes[i].Code;
                module.Funcs[i].Locals = localsbody.Locals;
                module.Funcs[i].Body = localsbody.Body;
            }

            module.Codes = Array.Empty<Module.CodeDesc>();
        }
    }    
}