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
using FluentValidation;
using FluentValidation.Results;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 2.5. Modules
    /// Represents a WebAssembly module, including its sections and definitions.
    /// Sections are defined across the partial class
    /// </summary>
    public partial class Module
    {
        internal Module()
        {
        }

        public ValidationResult Validate() => new ModuleValidator().Validate(this);
        public void ValidateAndThrow() => new ModuleValidator().ValidateAndThrow(this);
    }

    /// <summary>
    /// @Spec 5.5. Modules
    /// </summary>
    public static partial class BinaryModuleParser
    {
        //Add Ids to objects while parsing
        public static bool AnnotateWhileParsing = true;
        public static bool SkipFinalization = false;
        public static bool ParseCustomNames = false;
        public static uint MaximumFunctionLocals = 2048;

        public static int InstructionsParsed = 0;

        public static readonly SectionId[] SectionOrder = new[]
        {
            SectionId.Type,
            SectionId.Import,
            SectionId.Function,
            SectionId.Table,
            SectionId.Memory,
            SectionId.Global,
            SectionId.Export,
            SectionId.Start,
            SectionId.Element,
            SectionId.DataCount,
            SectionId.Code,
            SectionId.Data
        };

        static readonly HashSet<ByteCode> MemoryInstructions = new HashSet<ByteCode> { ExtCode.MemoryInit, ExtCode.DataDrop };
        public static InstructionBaseFactory InstructionFactory { get; private set; } = SpecFactory.Factory;

        public static void UseInstructionFactory(InstructionBaseFactory factory) =>
            InstructionFactory = factory;

        /// <summary>
        /// @Spec 5.5.16. Modules
        /// Parses a WebAssembly module from a binary stream.
        /// </summary>
        public static Module ParseWasm(Stream stream)
        {
            var module = new Module();
            var reader = new BinaryReader(stream);

            InstructionsParsed = 0;
            
            // Read and validate the magic number and version
            try
            {
                uint magicNumber = reader.ReadUInt32(); //Exactly 4bytes, little endian
                if (magicNumber != 0x6D736100) // '\0asm'
                {
                    throw new FormatException("Invalid magic number for WebAssembly module.");
                }

                uint version = reader.ReadUInt32(); //Exactly 4bytes, little endian
                if (version != 1)
                {
                    throw new NotSupportedException($"Unsupported WebAssembly version: {version}");
                }

                int order = -1;
                // Parse sections
                while (stream.Position < stream.Length)
                {
                    var sectionId = ParseSection(reader, module);
                    if (sectionId != SectionId.Custom)
                    {
                        int sectionIndex = Array.IndexOf(SectionOrder, sectionId);
                        if (sectionIndex <= order)
                            throw new FormatException(
                                $"Module sections must occur at most once and in the prescribed order.");
                        order = sectionIndex;
                    }
                }
            }
            catch (EndOfStreamException e)
            {
                throw new FormatException($"Encountered premature end of stream {e.Message}");
            }

            FinalizeModule(module);

            return module;
        }

        /// <summary>
        /// @Spec 5.5.2. Sections
        /// </summary>
        private static SectionId ParseSection(BinaryReader reader, Module module)
        {
            var start = reader.BaseStream.Position;
            var sectionId = (SectionId)reader.ReadByte();
            var payloadLength = reader.ReadLeb128_u32();
            var payloadStart = reader.BaseStream.Position;
            var payloadEnd = (uint)(payloadStart + payloadLength);

            if (payloadEnd > reader.BaseStream.Length)
                throw new FormatException("Section end out of bounds");
            
            // Console.WriteLine($"Section: {(SectionId)sectionId}");
            // @Spec 2.5. Modules
            switch (sectionId)
            {
                case SectionId.Type:
                    module.Types = ParseTypeSection(reader);
                    break;
                case SectionId.Import:
                    module.Imports = ParseImportSection(reader);
                    foreach (var import in module.Imports)
                    {
                        if (import.Desc is Module.ImportDesc.FuncDesc fd)
                            if (module.Types.Count == 0)
                                throw new FormatException($"Module must have Types section before importing Functions");

                    }
                    break;
                case SectionId.Function:
                    module.Funcs = ParseFunctionSection(reader).ToList();
                    int fIdx = module.ImportedFunctions.Count;
                    foreach (var func in module.Funcs)
                    {
                        if (module.Types.Count == 0)
                            throw new FormatException($"Module must have Types section before declaring Functions");
                        
                        func.Index = (FuncIdx)fIdx++;
                    }
                    break;
                case SectionId.Table:
                    module.Tables = ParseTableSection(reader);
                    if (AnnotateWhileParsing)
                    {
                        int idx = module.ImportedTables.Count;
                        foreach (var table in module.Tables)
                        {
                            table.Id = $"{idx++}";
                        }
                    }
                    break;
                case SectionId.Memory:
                    module.Memories = ParseMemorySection(reader);
                    if (AnnotateWhileParsing)
                    {
                        int idx = module.ImportedMems.Count;
                        foreach (var mem in module.Memories)
                        {
                            mem.Id = $"{idx++}";
                        }
                    }
                    break;
                case SectionId.Global:
                    module.Globals = ParseGlobalSection(reader);
                    if (AnnotateWhileParsing)
                    {
                        int idx = module.ImportedGlobals.Count;
                        foreach (var glob in module.Globals)
                        {
                            glob.Id = $"{idx++}";
                        }
                    }
                    break;
                case SectionId.Export:
                    module.Exports = ParseExportSection(reader);
                    break;
                case SectionId.Start:
                    module.StartIndex = ParseStartSection(reader);
                    break;
                case SectionId.Element:
                    module.Elements = ParseElementSection(reader);
                    if (AnnotateWhileParsing)
                    {
                        int idx = 0;
                        foreach (var elem in module.Elements)
                        {
                            elem.Id = $"{idx++}";
                        }
                    }
                    break;
                case SectionId.DataCount:
                    module.DataCount = ParseDataCountSection(reader);
                    break;
                case SectionId.Code:
                    module.Codes = ParseCodeSection(reader);
                    break;
                case SectionId.Data:
                    module.Datas = ParseDataSection(reader);
                    
                    if (AnnotateWhileParsing)
                    {
                        int idx = 0;
                        foreach (var data in module.Datas)
                        {
                            data.Id = $"{idx++}";
                        }
                    }
                    break;
                case SectionId.Custom: break; //Handled below 
                default:
                    throw new FormatException($"Unknown section ID: {sectionId} at offset {start}.");
            }

            // Custom sections
            if (sectionId == SectionId.Custom)
            {
                var customSectionName = reader.ReadUtf8String();
                switch (customSectionName)
                {
                    case "name" when ParseCustomNames:
                        using (var subreader = reader.GetSubsectionTo((int)payloadEnd))
                        {
                            module.Names = ParseNameSection(subreader);
                        }
                        break;
                    default:
                        //Skip others
                        // Console.WriteLine($"   name: {customSectionName}");
                        // // Read the bytes from the current position to payloadEnd
                        // byte[] sectionData = reader.ReadBytes((int)(payloadEnd - reader.BaseStream.Position));
                        // // Console.WriteLine(BitConverter.ToString(sectionData).Replace("-", " "));
                        // // Convert section bytes to characters and print
                        // string sectionString = System.Text.Encoding.UTF8.GetString(sectionData);
                        // Console.WriteLine($"    {sectionString}");

                        if (reader.BaseStream.Position > payloadEnd)
                            throw new FormatException("Unexpected end to Custom section");
                        
                        //Discard and fast-forward to the end of the section payload
                        reader.BaseStream.Position = payloadEnd;
                        break;
                }
            }

            if (reader.BaseStream.Position != payloadEnd)
            {
                throw new FormatException(
                    $"Section size mismatch. Expected {payloadLength} bytes, but got {reader.BaseStream.Position - payloadStart}.");
            }

            return sectionId;
        }

        /// <summary>
        /// @Spec 5.4 Instructions
        /// Parse an instruction sequence, return null for End (0x0B)
        /// </summary>
        public static InstructionBase? ParseInstruction(BinaryReader reader)
        {
            
            //Splice another byte if the first byte is a prefix
            var opcode = (OpCode)reader.ReadByte() switch {
                OpCode.FB => new ByteCode((GcCode)reader.ReadLeb128_u32()), 
                OpCode.FC => new ByteCode((ExtCode)reader.ReadLeb128_u32()),
                OpCode.FD => new ByteCode((SimdCode)reader.ReadLeb128_u32()),
                OpCode.FE => new ByteCode((AtomCode)reader.ReadLeb128_u32()),
                var b => new ByteCode(b)
            };
            int traceIdx = InstructionsParsed;
            try
            {
                return InstructionFactory.CreateInstruction(opcode)?.Parse(reader);
            }
            catch (InvalidDataException exc)
            {
                throw new FormatException($"Bad Memory parameters {exc.Message}");
            }
        }

        private static void FinalizeModule(Module module)
        {
            PatchFuncSection(module);

            HashSet<FuncIdx> fullyDeclared = new();
            var funcDescs = module.Exports
                .Select(export => export.Desc)
                .OfType<Module.ExportDesc.FuncDesc>();
            foreach (var funcDesc in funcDescs) fullyDeclared.Add(funcDesc.FunctionIndex);

            var elementIni = module.Elements
                .Where(elem => elem.Type == ValType.FuncRef)
                .SelectMany(elem => elem.Initializers)
                .SelectMany(ini => ini.Instructions)
                .OfType<InstRefFunc>();
            foreach (var refFunc in elementIni) fullyDeclared.Add(refFunc.FunctionIndex);

            var elementDecl = module.Elements
                .Where(elem => elem.Mode is Module.ElementMode.DeclarativeMode)
                .SelectMany(elem => elem.Initializers)
                .SelectMany(ini => ini.Instructions)
                .OfType<InstRefFunc>();
            
            foreach (var refFunc in elementDecl) fullyDeclared.Add(refFunc.FunctionIndex);
            
            var globalIni = module.Globals
                .Where(global => global.Type.ContentType == ValType.FuncRef)
                .SelectMany(global => global.Initializer.Instructions)
                .OfType<InstRefFunc>();
            foreach (var refFunc in globalIni) fullyDeclared.Add(refFunc.FunctionIndex);
            
            foreach (var func in module.Funcs)
            {
                if (func.Body.ContainsInstructions(MemoryInstructions))
                {
                    if (module.DataCount == uint.MaxValue)
                        throw new FormatException($"memory.init instruction requires Data Count section");
                }
                if (fullyDeclared.Contains(func.Index))
                    func.ElementDeclared = true;
            }
            
            if (module.DataCount == uint.MaxValue)
                module.DataCount = (uint)module.Datas.Length;
            
            if (module.DataCount != module.Datas.Length)
                throw new FormatException($"Data count and data section have inconsistent lengths.");
            
            PatchNames(module);
        }
    }
}