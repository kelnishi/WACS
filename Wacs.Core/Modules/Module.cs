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
            var sectionId = reader.ReadByte();
            var payloadLength = reader.ReadLeb128_u32();
            var payloadEnd = (uint)(reader.BaseStream.Position + payloadLength);

            // Console.WriteLine($"Section: {(SectionId)sectionId}");
            
            // Custom section - skip for now
            if (sectionId == 0)
            {
                //Discard and fast-forward to the end of the section payload
                var name = reader.ReadUTF8String(); // Name
                // Console.WriteLine($"   name: {name}");
                // // Read the bytes from the current position to payloadEnd
                // byte[] sectionData = reader.ReadBytes((int)(payloadEnd - reader.BaseStream.Position));
                // // Console.WriteLine(BitConverter.ToString(sectionData).Replace("-", " "));
                // // Convert section bytes to characters and print
                // string sectionString = System.Text.Encoding.UTF8.GetString(sectionData);
                // Console.WriteLine($"    {sectionString}");
                reader.BaseStream.Position = payloadEnd;
                return;
            }
            
            // @Spec 2.5. Modules
            switch ((SectionId)sectionId)
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
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId} at offset {reader.BaseStream.Position}.");
            }

            if (reader.BaseStream.Position != payloadEnd)
            {
                throw new InvalidDataException($"Section size mismatch. Expected {payloadLength + 4} bytes, but got {payloadEnd - reader.BaseStream.Position}.");
            }
        }

        //TODO Warn for missing sections?
        private static void FinalizeModule(Module module)
        {
            module.Types = module.Types ?? new FunctionType[0];
            module.Imports = module.Imports ?? new Module.Import[0];
            module.Funcs = module.Funcs ?? new List<Module.Function>();
            module.Tables = module.Tables ?? new TableType[0];
            module.Memories = module.Memories ?? new MemoryType[0];
            module.Globals = module.Globals ?? new Module.Global[0];
            module.Exports = module.Exports ?? new Module.Export[0];
            module.Elements = module.Elements ?? new Module.ElementSegment[0];
            module.Datas = module.Datas ?? new Module.Data[0];
        }
    }
}