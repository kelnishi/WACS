using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Modules.Sections;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 2.5. Modules
    /// Represents a WebAssembly module, including its sections and definitions.
    /// Sections are defined across the partial class
    /// </summary>
    public partial class Module
    {
        internal Module() {}
        
        /// <summary>
        /// @Spec 3.4. Modules
        /// </summary>
        public class Validator : AbstractValidator<Module>
        {
            public Validator()
            {
                RuleForEach(module => module.Types).SetValidator(new FunctionType.Validator());
                RuleForEach(module => module.Imports).SetValidator(new Import.Validator());
                RuleForEach(module => module.Funcs).SetValidator(new Module.Function.Validator());
                RuleForEach(module => module.Tables).SetValidator(new TableType.Validator());
                
                
                RuleForEach(module => module.Memories).SetValidator(new MemoryType.Validator());
                RuleForEach(module => module.Globals).SetValidator(new Global.Validator());
                // RuleForEach(module => module.Exports).SetValidator(new Export.Validator()); //= module.Exports ?? new Module.Export[0];
                // RuleForEach(module => module.Elements).SetValidator(new ElementSegment.Validator()); // = module.Elements ?? new Module.ElementSegment[0];
                // RuleForEach(module => module.Datas).SetValidator(new Data.Validator()); //module.Datas ?? new Module.Data[0];

            }
        }
    }
    
    /// <summary>
    /// @Spec 5.5. Modules
    /// </summary>
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.16. Modules
        /// Parses a WebAssembly module from a binary stream.
        /// </summary>
        public static Module Parse(Stream stream)
        {
            var module = new Module();
            var reader = new BinaryReader(stream);

            // Read and validate the magic number and version
            uint magicNumber = reader.ReadUInt32();
            if (magicNumber != 0x6D736100) // '\0asm'
            {
                throw new InvalidDataException("Invalid magic number for WebAssembly module.");
            }

            uint version = reader.ReadUInt32();
            if (version != 1)
            {
                throw new InvalidDataException($"Unsupported WebAssembly version: {version}");
            }

            // Parse sections
            while (stream.Position < stream.Length)
            {
                ParseSection(reader, module);
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
                case SectionId.Type: module.Types = ParseTypeSection(reader); break;
                case SectionId.Import: module.Imports = ParseImportSection(reader); break;
                case SectionId.Function: module.Funcs = ParseFunctionSection(reader).ToList(); break;
                case SectionId.Table: module.Tables = ParseTableSection(reader); break;
                case SectionId.Memory: module.Memories = ParseMemorySection(reader); break;
                case SectionId.Global: module.Globals = ParseGlobalSection(reader); break;
                case SectionId.Export: module.Exports = ParseExportSection(reader); break;
                case SectionId.Start: module.StartIndex = ParseStartSection(reader); break;
                case SectionId.Element: module.Elements = ParseElementSection(reader); break;
                case SectionId.DataCount: module.DataCount = ParseDataCountSection(reader); break;
                case SectionId.Code: PatchFuncSection(module.Funcs, ParseCodeSection(reader)); break;
                case SectionId.Data: module.Datas = ParseDataSection(reader); break;
                case SectionId.Custom: break; //Handled below 
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId} at offset {start}.");
            }
            
            // Custom sections
            if (sectionId == SectionId.Custom)
            {
                var customSectionName = reader.ReadUTF8String();
                switch (customSectionName)
                {
                    case "name":
                        using (var subreader = reader.GetSubsectionTo((int)payloadEnd)) {
                            module.Names = ParseNameSection(subreader);
                        }
                        break;
                    default: 
                        //Skip others
                        Console.WriteLine($"   name: {customSectionName}");
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
                throw new InvalidDataException($"Section size mismatch. Expected {payloadLength} bytes, but got {reader.BaseStream.Position - payloadStart}.");
            }
        }

        //TODO Warn for missing sections?
        private static void FinalizeModule(Module module)
        {
            module.Types = module.Types ?? new List<FunctionType>();
            module.Imports = module.Imports ?? Array.Empty<Module.Import>();
            module.Funcs = module.Funcs ?? new List<Module.Function>();
            module.Tables = module.Tables ?? Array.Empty<TableType>();
            module.Memories = module.Memories ?? Array.Empty<MemoryType>();
            module.Globals = module.Globals ?? Array.Empty<Module.Global>();
            module.Exports = module.Exports ?? Array.Empty<Module.Export>();
            module.Elements = module.Elements ?? Array.Empty<Module.ElementSegment>();
            module.Datas = module.Datas ?? Array.Empty<Module.Data>();
        }
    }
}