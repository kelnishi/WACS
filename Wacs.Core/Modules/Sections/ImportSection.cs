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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Attributes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.11. Imports
        /// </summary>
        public Import[] Imports { get; internal set; } = Array.Empty<Import>();

        public ReadOnlyCollection<Function> ImportedFunctions =>
            Imports
                .Select(import => import.Desc)
                .OfType<ImportDesc.FuncDesc>()
                .Select(funcDesc => new Function { TypeIndex = funcDesc.TypeIndex, IsImport = true, IsFullyDeclared = true })
                .ToList().AsReadOnly();

        public ReadOnlyCollection<TableType> ImportedTables =>
            Imports
                .Select(import => import.Desc)
                .OfType<ImportDesc.TableDesc>()
                .Select(tableDesc => tableDesc.TableDef)
                .ToList().AsReadOnly();

        public ReadOnlyCollection<MemoryType> ImportedMems =>
            Imports
                .Select(import => import.Desc)
                .OfType<ImportDesc.MemDesc>()
                .Select(memDesc => memDesc.MemDef)
                .ToList().AsReadOnly();

        public ReadOnlyCollection<Global> ImportedGlobals =>
            Imports
                .Select(import => import.Desc)
                .OfType<ImportDesc.GlobalDesc>()
                .Select(globDesc => new Global(globDesc.GlobalDef){IsImport = true})
                .ToList().AsReadOnly();

        /// <summary>
        /// @Spec 2.5.11. Imports
        /// </summary>
        public class Import : IRenderable
        {
            public string ModuleName { get; internal set; } = null!;
            public string Name { get; internal set; } = null!;
            public ImportDesc Desc { get; internal set; } = null!;

            public void RenderText(StreamWriter writer, Module module, string indent)
            {
                var import = " ";
                var id = string.IsNullOrWhiteSpace(Desc.Id) ? "" : $" (;{Desc.Id};)";
                switch (Desc)
                {
                    case ImportDesc.FuncDesc fd:
                        var funcType = $" (type {fd.TypeIndex.Value})";
                        import = $" (func{id}{funcType})";
                        break;
                    case ImportDesc.TableDesc td:
                        var tableLimits = $" {td.TableDef.Limits.ToWat()}";
                        var tableRefType = $" {td.TableDef.ElementType.ToWat()}";
                        import = $" (table{id}{tableLimits}{tableRefType})";
                        break;
                    case ImportDesc.MemDesc md:
                        var memLimits = $" {md.MemDef.Limits.ToWat()}";
                        import = $" (mem{id}{memLimits})";
                        break;
                    case ImportDesc.GlobalDesc gd:
                        var globalType = gd.GlobalDef.Mutability == Mutability.Mutable
                            ? $" (mut {gd.GlobalDef.ContentType.ToWat()})"
                            : $" {gd.GlobalDef.ContentType.ToWat()}";
                        import = $" (global{id}{globalType})";
                        break;
                }
                writer.WriteLine($"{indent}(import \"{ModuleName}\" \"{Name}\"{import})"); 
            }

            public class Validator : AbstractValidator<Import>
            {
                public Validator()
                {
                    RuleFor(i => i.Desc).SetInheritanceValidator(v => {
                        v.Add(new ImportDesc.FuncDesc.Validator());
                        v.Add(new ImportDesc.TableDesc.Validator());
                        v.Add(new ImportDesc.MemDesc.Validator());
                        v.Add(new ImportDesc.GlobalDesc.Validator());
                    });
                }
            }
        }

        public abstract class ImportDesc
        {
            public string Id { get; set; } = "";

            public class FuncDesc : ImportDesc
            {
                public TypeIdx TypeIndex { get; internal set; }

                /// <summary>
                /// @Spec 3.2.7.1. func functype
                /// </summary>
                public class Validator : AbstractValidator<FuncDesc> {
                    public Validator() {
                        // Only checks that the FunctionType exists, validation happens on the section
                        RuleFor(desc => desc.TypeIndex)
                            .Must((_, index, ctx) => ctx.GetValidationContext().Types.Contains(index));
                    }
                }
            }

            public class TableDesc : ImportDesc
            {
                public TableType TableDef { get; internal set; } = null!;

                /// <summary>
                /// @Spec 3.2.7.2. table tabletype
                /// </summary>
                public class Validator : AbstractValidator<TableDesc> {
                    public Validator() {
                        RuleFor(desc => desc.TableDef)
                            .SetValidator(new TableType.Validator());
                    }
                }
            }

            public class MemDesc : ImportDesc
            {
                public MemoryType MemDef { get; internal set; } = null!;

                /// <summary>
                /// @Spec 3.2.7.3. mem memtype
                /// </summary>
                public class Validator : AbstractValidator<MemDesc> {
                    public Validator() {
                        RuleFor(desc => desc.MemDef)
                            .SetValidator(new MemoryType.Validator());
                    }
                }
            }

            public class GlobalDesc : ImportDesc
            {
                public GlobalType GlobalDef { get; internal set; } = null!;

                /// <summary>
                /// @Spec 3.2.7.4. global globaltype
                /// </summary>
                public class Validator : AbstractValidator<GlobalDesc> {
                    public Validator() {
                        RuleFor(desc => desc.GlobalDef)
                            .SetValidator(new GlobalType.Validator());
                    }
                }
            }
        }
    }
    
    public static partial class BinaryModuleParser
    {
        private static Module.ImportDesc ParseImportDesc(BinaryReader reader) => 
            ExternalKindParser.Parse(reader) switch {
                ExternalKind.Function => new Module.ImportDesc.FuncDesc { TypeIndex = (TypeIdx)reader.ReadLeb128_u32() },
                ExternalKind.Table => new Module.ImportDesc.TableDesc { TableDef = TableType.Parse(reader) },
                ExternalKind.Memory => new Module.ImportDesc.MemDesc { MemDef = MemoryType.Parse(reader) },
                ExternalKind.Global => new Module.ImportDesc.GlobalDesc { GlobalDef = GlobalType.Parse(reader) },
                var kind => throw new FormatException($"Malformed Module Import section {kind} at {reader.BaseStream.Position}")
            };

        private static Module.Import ParseImport(BinaryReader reader) => 
            new()
            {
                ModuleName = reader.ReadString(),
                Name = reader.ReadString(),
                Desc = ParseImportDesc(reader)
            };

        /// <summary>
        /// @Spec 5.5.5 Import Section
        /// </summary>
        private static Module.Import[] ParseImportSection(BinaryReader reader)
        {
            var imports = reader.ParseVector(ParseImport);
            if (!AnnotateWhileParsing) return imports;
            
            int fIdx = 0, tIdx = 0, mIdx = 0, gIdx = 0;
            foreach (var import in imports)
            {
                switch (import.Desc)
                {
                    case Module.ImportDesc.FuncDesc fd:
                        fd.Id = $"{fIdx++}";
                        break;
                    case Module.ImportDesc.TableDesc td:
                        td.Id = $"{tIdx++}";
                        break;
                    case Module.ImportDesc.MemDesc md:
                        md.Id = $"{mIdx++}";
                        break;
                    case Module.ImportDesc.GlobalDesc gd:
                        gd.Id = $"{gIdx++}";
                        break;
                }    
            }
            return imports;
        }
    }

}