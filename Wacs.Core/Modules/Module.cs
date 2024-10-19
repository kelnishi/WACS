using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
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
        /// <summary>
        /// @Spec 5.5.16. Modules
        /// Parses a WebAssembly module from a binary stream.
        /// </summary>
        public static Module ParseWasm(Stream stream)
        {
            var module = new Module();
            var reader = new BinaryReader(stream);

            // Read and validate the magic number and version
            uint magicNumber = reader.ReadUInt32(); //Exactly 4bytes, little endian
            if (magicNumber != 0x6D736100) // '\0asm'
            {
                throw new InvalidDataException("Invalid magic number for WebAssembly module.");
            }

            uint version = reader.ReadUInt32(); //Exactly 4bytes, little endian
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
                case SectionId.Type:
                    module.Types = ParseTypeSection(reader);
                    break;
                case SectionId.Import:
                    module.Imports = ParseImportSection(reader);
                    break;
                case SectionId.Function:
                    module.Funcs = ParseFunctionSection(reader).ToList();
                    break;
                case SectionId.Table:
                    module.Tables = ParseTableSection(reader);
                    break;
                case SectionId.Memory:
                    module.Memories = ParseMemorySection(reader);
                    break;
                case SectionId.Global:
                    module.Globals = ParseGlobalSection(reader);
                    break;
                case SectionId.Export:
                    module.Exports = ParseExportSection(reader);
                    break;
                case SectionId.Start:
                    module.StartIndex = ParseStartSection(reader);
                    break;
                case SectionId.Element:
                    module.Elements = ParseElementSection(reader);
                    break;
                case SectionId.DataCount:
                    module.DataCount = ParseDataCountSection(reader);
                    break;
                case SectionId.Code:
                    PatchFuncSection(module.Funcs, ParseCodeSection(reader));
                    break;
                case SectionId.Data:
                    module.Datas = ParseDataSection(reader);
                    break;
                case SectionId.Custom: break; //Handled below 
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId} at offset {start}.");
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
                throw new InvalidDataException(
                    $"Section size mismatch. Expected {payloadLength} bytes, but got {reader.BaseStream.Position - payloadStart}.");
            }
        }

        //TODO Warn for missing sections?
        private static void FinalizeModule(Module module)
        {
            // ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            module.Types ??= new List<FunctionType>();
            module.Imports ??= Array.Empty<Module.Import>();
            module.Funcs ??= new List<Module.Function>();
            module.Tables ??= new List<TableType>();
            module.Memories ??= new List<MemoryType>();
            module.Globals ??= new List<Module.Global>();
            module.Exports ??= Array.Empty<Module.Export>();
            module.Elements ??= Array.Empty<Module.ElementSegment>();
            module.Datas ??= Array.Empty<Module.Data>();
            // ReSharper restore All
            PatchNames(module);
        }
    }
}