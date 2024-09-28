using System;
using System.Collections.Generic;
using System.IO;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.11. Imports
        /// </summary>
        public Import[] Imports { get; internal set; } = null!;
        
        /// <summary>
        /// @Spec 2.5.11. Imports
        /// </summary>
        public class Import
        {
            public string ModuleName { get; internal set; }
            public string Name { get; internal set; }
            public ImportDesc Desc { get; internal set; }

            public Import(string moduleName, string name, ImportDesc desc) =>
                (ModuleName, Name, Desc) = (moduleName, name, desc);

            public class Validator : AbstractValidator<Import>
            {
                public Validator()
                {
                    RuleFor(i => i.Desc).SetInheritanceValidator(v =>
                    {
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
                public UInt32 Index { get; internal set; } = UInt32.MaxValue;
                public FuncDesc(UInt32 index) => Index = index;

                /// <summary>
                /// @Spec 3.2.7.1. func functype
                /// </summary>
                public class Validator : AbstractValidator<FuncDesc> {
                    public Validator() {
                        // Only checks that the FunctionType exists, validation happens on the section
                        RuleFor(desc => desc.Index)
                            .Must((desc, index, ctx) =>
                                index < ((List<FunctionType>)ctx.RootContextData[nameof(Module.Types)]).Count);
                    }
                }
            }

            public class TableDesc : ImportDesc
            {
                public TableType TableDef { get; internal set; }
                public TableDesc(TableType tableDef) => TableDef = tableDef;
                
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
                public MemoryType MemDef  { get; internal set; }
                public MemDesc(MemoryType memDef) => MemDef = memDef;
                
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
                public GlobalType GlobalDef { get; internal set; }
                public GlobalDesc(GlobalType globalDef) => GlobalDef = globalDef;
                
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
    
    public static partial class ModuleParser
    {
        private static Module.ImportDesc ParseImportDesc(BinaryReader reader) => 
            ExternalKindParser.Parse(reader) switch {
                ExternalKind.Function => new Module.ImportDesc.FuncDesc(reader.ReadLeb128_u32()),
                ExternalKind.Table => new Module.ImportDesc.TableDesc(TableType.Parse(reader)),
                ExternalKind.Memory => new Module.ImportDesc.MemDesc(MemoryType.Parse(reader)),
                ExternalKind.Global => new Module.ImportDesc.GlobalDesc(GlobalType.Parse(reader)),
                var kind => throw new InvalidDataException($"Malformed Module Import section {kind} at {reader.BaseStream.Position}")
            };
        
        private static Module.Import ParseImport(BinaryReader reader) => 
            new Module.Import (
                moduleName: reader.ReadString(),
                name: reader.ReadString(),
                desc: ParseImportDesc(reader)
            );

        /// <summary>
        /// @Spec 5.5.5 Import Section
        /// </summary>
        private static Module.Import[] ParseImportSection(BinaryReader reader) =>
            reader.ParseVector(ParseImport);
        
    }

}