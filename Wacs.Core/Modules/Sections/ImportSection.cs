using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FluentValidation;
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
        public Import[] Imports { get; internal set; } = null!;

        public ReadOnlyCollection<Function> ImportedFunctions =>
            Imports
                .Select(import => import.Desc as ImportDesc.FuncDesc)
                .Where(funcDesc => funcDesc != null)
                .Select(funcDesc => new Function { TypeIndex = funcDesc!.TypeIndex, IsImport = true })
                .ToList().AsReadOnly(); 
        
        public ReadOnlyCollection<TableType> ImportedTables =>
            Imports
                .Select(import => import.Desc as ImportDesc.TableDesc)
                .Where(tableDesc => tableDesc != null)
                .Select(tableDesc => tableDesc!.TableDef)
                .ToList().AsReadOnly(); 
        
        public ReadOnlyCollection<MemoryType> ImportedMems =>
            Imports
                .Select(import => import.Desc as ImportDesc.MemDesc)
                .Where(memDesc => memDesc != null)
                .Select(memDesc => memDesc!.MemDef)
                .ToList().AsReadOnly(); 
        
        public ReadOnlyCollection<Global> ImportedGlobals =>
            Imports
                .Select(import => import.Desc as ImportDesc.GlobalDesc)
                .Where(globDesc => globDesc != null)
                .Select(globDesc => new Global(globDesc!.GlobalDef))
                .ToList().AsReadOnly(); 
        
        /// <summary>
        /// @Spec 2.5.11. Imports
        /// </summary>
        public class Import
        {
            public string ModuleName { get; internal set; } = null!;
            public string Name { get; internal set; } = null!;
            public ImportDesc Desc { get; internal set; } = null!;

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
                            .Must((desc, index, ctx) => ctx.GetExecContext().Types.Contains(index));
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
                var kind => throw new InvalidDataException($"Malformed Module Import section {kind} at {reader.BaseStream.Position}")
            };
        
        private static Module.Import ParseImport(BinaryReader reader) => 
            new Module.Import {
                ModuleName = reader.ReadString(),
                Name = reader.ReadString(),
                Desc = ParseImportDesc(reader)
            };

        /// <summary>
        /// @Spec 5.5.5 Import Section
        /// </summary>
        private static Module.Import[] ParseImportSection(BinaryReader reader) =>
            reader.ParseVector(ParseImport);
        
    }

}