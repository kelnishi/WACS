using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.3 Functions
        /// </summary>
        public class FuncLocalsBody
        {
            private (uint Number, ValType Type)[] CompressedLocals { get; }
            public Expression Body { get; }
            
            public ValType[] Locals => 
                CompressedLocals
                    .SelectMany(local => Enumerable.Repeat(local.Type, (int)local.Number))
                    .ToArray();
            
            
            private FuncLocalsBody((uint Number, ValType Type)[] compressedLocals, Expression expr) => 
                (CompressedLocals, Body) = (compressedLocals, expr);

            private static (uint Number, ValType Type) ParseCompressedLocal(BinaryReader reader) =>
                (reader.ReadLeb128_u32(), ValueTypeParser.Parse(reader));
            
            public static FuncLocalsBody Parse(BinaryReader reader) =>
                new FuncLocalsBody(reader.ParseVector(ParseCompressedLocal), Expression.Parse(reader));
        }
        
        public class CodeDesc
        {
            public UInt32 Size { get; internal set; }
            public FuncLocalsBody Code { get; internal set; }
            private CodeDesc(UInt32 size, FuncLocalsBody code) => (Size, Code) = (size, code);
            public static CodeDesc Parse(BinaryReader reader)
            {
                uint size = reader.ReadLeb128_u32();
                var start = reader.BaseStream.Position;
                var code = new CodeDesc(size, FuncLocalsBody.Parse(reader));
                var end = start + size;
                if (reader.BaseStream.Position != end)
                    throw new InvalidDataException($"Malformed code size: expected {code.Size:x} bytes, but got {(int)(reader.BaseStream.Position - start):x} at {reader.BaseStream.Position:x}");
                return code;
            }
        }
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.13. Code Section
        /// </summary>
        private static Module.CodeDesc[] ParseCodeSection(BinaryReader reader) =>
            reader.ParseVector(Module.CodeDesc.Parse);

        /// <summary>
        /// @Spec 5.5.13. Code Section
        /// </summary>
        private static void PatchFuncSection(List<Module.Function> functions, Module.CodeDesc[] code)
        {
            if (functions.Count != code.Length)
                throw new InvalidDataException($"Module functions section count {functions.Count} must equal the code count {code.Length}");
            
            for (int i = 0, l = code.Length; i < l; ++i)
            {
                var localsbody = code[i].Code;
                functions[i].Locals = localsbody.Locals;
                functions[i].Body = localsbody.Body;
            }
        }
    }    
}