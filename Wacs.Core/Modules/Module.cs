using System;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
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

        private static IInstructionFactory _instructionFactory = ReferenceFactory.Factory;

        public static int InstructionsParsed = 0;
        public static IInstructionFactory InstructionFactory => _instructionFactory;

        public static void UseInstructionFactory(IInstructionFactory factory) =>
            _instructionFactory = factory;

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

                // Parse sections
                while (stream.Position < stream.Length)
                {
                    ParseSection(reader, module);
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
        private static void ParseSection(BinaryReader reader, Module module)
        {
            var start = reader.BaseStream.Position;
            var sectionId = (SectionId)reader.ReadByte();
            var payloadLength = reader.ReadLeb128_u32();
            var payloadStart = reader.BaseStream.Position;
            var payloadEnd = (uint)(payloadStart + payloadLength);

            // Console.WriteLine($"Section: {(SectionId)sectionId}");
            // @Spec 2.5. Modules
            switch (sectionId)
            {
                case SectionId.Type:
                    module.Types = ParseTypeSection(reader);
                    break;
                case SectionId.Import:
                    module.Imports = ParseImportSection(reader);
                    break;
                case SectionId.Function:
                    module.Funcs = ParseFunctionSection(reader).ToList();
                    int fIdx = module.ImportedFunctions.Count;
                    foreach (var func in module.Funcs)
                    {
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
                    PatchFuncSection(module.Funcs, ParseCodeSection(reader));
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
                    case "name":
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
        }

        /// <summary>
        /// @Spec 5.4 Instructions
        /// Parse an instruction sequence, return null for End (0x0B)
        /// </summary>
        public static IInstruction? ParseInstruction(BinaryReader reader)
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
            var inst = _instructionFactory.CreateInstruction(opcode)?.Parse(reader);
            
            //Raw tracking for debugging purposes
            // if (inst != null)
            //     InstructionsParsed += 1;

            return inst;
        }

        private static void FinalizeModule(Module module)
        {
            PatchNames(module);
        }
    }
}