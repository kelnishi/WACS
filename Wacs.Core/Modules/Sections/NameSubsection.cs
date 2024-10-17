using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    enum NameSubsectionId : byte
    {
        ModuleName = 0,
        FuncName = 1,
        LocalName = 2,
    }
    
    public partial class Module
    {
        public NameSection? Names { get; internal set; } = null;
    
        public string Name => Names?.ModuleName?.Name ?? String.Empty;
        public NameMap FuncMap => Names?.FunctionNames?.Names ?? NameMap.Empty;
        public IndirectNameMap LocalMap => Names?.LocalNames?.Names ?? IndirectNameMap.Empty;
        
        public class NameSection
        {
            public NameSubsection.ModuleNameSubsection? ModuleName { get; internal set; } = null;
            public NameSubsection.FuncNameSubsection? FunctionNames { get; internal set; } = null;
            public NameSubsection.LocalNameSubsection? LocalNames { get; internal set; } = null;

            
            public static NameSection Parse(BinaryReader reader)
            {
                var nameSection = new NameSection();
                while (reader.HasMoreBytes())
                {
                    var subsectionId = (NameSubsectionId)reader.ReadByte();
                    uint size = reader.ReadLeb128_u32();
                    var start = reader.BaseStream.Position;
                    var end = start + size;
                    switch (subsectionId)
                    {
                        case NameSubsectionId.ModuleName:
                            nameSection.ModuleName = NameSubsection.ModuleNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.FuncName:
                            nameSection.FunctionNames = NameSubsection.FuncNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.LocalName:
                            nameSection.LocalNames = NameSubsection.LocalNameSubsection.Parse(reader);
                            break;
                        default:
                            throw new InvalidDataException($"Name Subsection had invalid id: {subsectionId}");
                    }

                    if (reader.BaseStream.Position != end)
                        throw new InvalidDataException(
                            $"Name Subsection size mismatch. Expected {size} bytes, but got {reader.BaseStream.Position - start}");
                }

                return nameSection;
            }
        }
        
        public abstract class NameSubsection
        {
            public class ModuleNameSubsection : NameSubsection
            {
                public string Name { get; internal set; } = null!;

                public static ModuleNameSubsection Parse(BinaryReader reader) =>
                    new()
                    {
                        Name = reader.ReadUTF8String()
                    };
            }

            public class FuncNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;

                public static FuncNameSubsection Parse(BinaryReader reader) =>
                    new()
                    {
                        Names = NameMap.Parse(reader)
                    };
            }

            public class LocalNameSubsection : NameSubsection
            {
                public IndirectNameMap Names { get; internal set; } = null!;

                public static LocalNameSubsection Parse(BinaryReader reader) =>
                    new()
                    {
                        Names = IndirectNameMap.Parse(reader)
                    };
            }
        }
        
        public class NameMap
        {
            public Dictionary<uint, string> NameAssocMap { get; internal set; } = null!;

            public string this[uint key] => NameAssocMap.TryGetValue(key, out var value) ? value : String.Empty;
            
            public static readonly NameMap Empty = new() { NameAssocMap = new Dictionary<uint, string>() };
            
            private static (uint, string) ParseNameAssoc(BinaryReader reader) => 
                (reader.ReadLeb128_u32(), reader.ReadUTF8String());

            
            public static NameMap Parse(BinaryReader reader) =>
                new()
                {
                    NameAssocMap = 
                        reader
                        .ParseVector(ParseNameAssoc)
                        .ToDictionary(pair => pair.Item1, pair => pair.Item2)
                };
        }

        public class IndirectNameMap
        {
            public Dictionary<uint, NameMap> IndirectNameAssocMap { get; internal set; } = null!;

            public NameMap this[uint key] => 
                IndirectNameAssocMap.TryGetValue(key, out var value) ? value : NameMap.Empty;
            
            public static readonly IndirectNameMap Empty = 
                new() { IndirectNameAssocMap = new Dictionary<uint, NameMap>() };
            
            private static (uint, NameMap) ParseIndirectNameAssoc(BinaryReader reader) =>
                (reader.ReadLeb128_u32(), NameMap.Parse(reader));
            
            public static IndirectNameMap Parse(BinaryReader reader) =>
                new()
                {
                    IndirectNameAssocMap =
                        reader
                            .ParseVector(ParseIndirectNameAssoc)
                            .ToDictionary(pair => pair.Item1, pair => pair.Item2)
                };
        }
    }

    public static partial class BinaryModuleParser
    {
        public static Module.NameSection ParseNameSection(BinaryReader reader) =>
            Module.NameSection.Parse(reader);

        public static void PatchNames(Module module)
        {
            //throw new NotImplementedException();
            // if (module.Names != null)
            // {
            //     if (module.Names.FunctionNames != null)
            //     {
            //         foreach (var kv in module.Names.FunctionNames.Names.NameAssocMap)
            //         {
            //             
            //             // module.Funcs[]
            //         }
            //     }
            // }
        }
    }
}