// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    internal enum NameSubsectionId : byte
    {
        ModuleName = 0,
        FuncName = 1,
        LocalName = 2,
        LabelName = 3,
        TypeName = 4,
        TableName = 5,
        MemoryName = 6,
        GlobalName = 7,
        ElementSegName = 8,
        DataSegName = 9
    }

    public partial class Module
    {
        public NameSection? Names { get; internal set; }

        public string Name => Names?.ModuleName?.Name ?? string.Empty;
        public NameMap FuncMap => Names?.FunctionNames?.Names ?? NameMap.Empty;
        public IndirectNameMap LocalMap => Names?.LocalNames?.Names ?? IndirectNameMap.Empty;

        public void SetName(string name)
        {
            if (Names == null)
                Names = new NameSection();
            
            Names.ModuleName = new NameSubsection.ModuleNameSubsection { Name = name };
        }

        public class NameSection
        {
            public NameSubsection.ModuleNameSubsection? ModuleName { get; internal set; }
            public NameSubsection.FuncNameSubsection? FunctionNames { get; internal set; }
            public NameSubsection.LocalNameSubsection? LocalNames { get; internal set; }
            public NameSubsection.ElementSegNameSubsection? ElementSegNames { get; internal set; }
            public NameSubsection.DataSegNameSubsection? DataSegNames { get; internal set; }
            public NameSubsection.GlobalNameSubsection? GlobalNames { get; internal set; }
            public NameSubsection.TableNameSubsection? TableNames { get; internal set; }
            public NameSubsection.MemoryNameSubsection? MemoryNames { get; internal set; }
            public NameSubsection.LabelNameSubsection? LabelNames { get; internal set; }
            public NameSubsection.TypeNameSubsection? TypeNames { get; internal set; }

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
                        case NameSubsectionId.LabelName:
                            nameSection.LabelNames = NameSubsection.LabelNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.TypeName:
                            nameSection.TypeNames = NameSubsection.TypeNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.TableName:
                            nameSection.TableNames = NameSubsection.TableNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.MemoryName:
                            nameSection.MemoryNames = NameSubsection.MemoryNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.GlobalName:
                            nameSection.GlobalNames = NameSubsection.GlobalNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.ElementSegName:
                            nameSection.ElementSegNames = NameSubsection.ElementSegNameSubsection.Parse(reader);
                            break;
                        case NameSubsectionId.DataSegName:
                            nameSection.DataSegNames = NameSubsection.DataSegNameSubsection.Parse(reader);
                            break;
                        
                        default:
                            throw new FormatException($"Name Subsection had invalid id: {subsectionId}");
                    }

                    if (reader.BaseStream.Position != end)
                        throw new FormatException(
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
                public static ModuleNameSubsection Parse(BinaryReader reader) => new() {  Name = reader.ReadUtf8String() };
            }

            public class FuncNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static FuncNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class LocalNameSubsection : NameSubsection
            {
                public IndirectNameMap Names { get; internal set; } = null!;
                public static LocalNameSubsection Parse(BinaryReader reader) => new() { Names = IndirectNameMap.Parse(reader) };
            }

            public class LabelNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static LabelNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class TypeNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static TypeNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class TableNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static TableNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class MemoryNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static MemoryNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class GlobalNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static GlobalNameSubsection Parse(BinaryReader reader) => new()  { Names = NameMap.Parse(reader) };
            }

            public class ElementSegNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static ElementSegNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }

            public class DataSegNameSubsection : NameSubsection
            {
                public NameMap Names { get; internal set; } = null!;
                public static DataSegNameSubsection Parse(BinaryReader reader) => new() { Names = NameMap.Parse(reader) };
            }
        }

        public class NameMap
        {
            public static readonly NameMap Empty = new() { NameAssocMap = new Dictionary<uint, string>() };
            public Dictionary<uint, string> NameAssocMap { get; internal set; } = null!;

            public string this[uint key] => NameAssocMap.TryGetValue(key, out var value) ? value : string.Empty;

            private static (uint, string) ParseNameAssoc(BinaryReader reader) =>
                (reader.ReadLeb128_u32(), reader.ReadUtf8String());


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
            public static readonly IndirectNameMap Empty =
                new() { IndirectNameAssocMap = new Dictionary<uint, NameMap>() };

            public Dictionary<uint, NameMap> IndirectNameAssocMap { get; internal set; } = null!;

            public NameMap this[uint key] =>
                IndirectNameAssocMap.TryGetValue(key, out var value) ? value : NameMap.Empty;

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
            if (module.Names?.TypeNames != null)
                foreach (var kv in module.Names.TypeNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Types.Count)
                        module.Types[(int)kv.Key].Id = kv.Value;
                }

            
            if (module.Names?.FunctionNames != null)
            {
                uint idx = (uint)module.ImportedFunctions.Count;
                foreach (var func in module.Funcs)
                {
                    func.Id = module.Names.FunctionNames.Names.NameAssocMap.TryGetValue(idx, out var funcName) 
                        ? $"{funcName}|{idx}"
                        : $"{idx}";
                    idx++;
                }
            }

            if (module.Names?.LocalNames != null)
                foreach (var kv in module.Names.LocalNames.Names.IndirectNameAssocMap)
                {
                    //???
                }
            
            if (module.Names?.TableNames != null)
                foreach (var kv in module.Names.TableNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Tables.Count)
                        module.Tables[(int)kv.Key].Id = kv.Value;
                }
            
            if (module.Names?.MemoryNames != null)
                foreach (var kv in module.Names.MemoryNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Memories.Count)
                        module.Memories[(int)kv.Key].Id = kv.Value;
                }
            
            if (module.Names?.GlobalNames != null)
                foreach (var kv in module.Names.GlobalNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Globals.Count)
                        module.Globals[(int)kv.Key].Id = kv.Value;
                }
            
            if (module.Names?.ElementSegNames != null)
                foreach (var kv in module.Names.ElementSegNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Elements.Length)
                        module.Elements[(int)kv.Key].Id = kv.Value;
                }
            
            if (module.Names?.DataSegNames != null)
                foreach (var kv in module.Names.DataSegNames.Names.NameAssocMap)
                {
                    if (kv.Key > 0 && kv.Key < module.Datas.Length)
                        module.Datas[(int)kv.Key].Id = kv.Value;
                }
        }
    }
}