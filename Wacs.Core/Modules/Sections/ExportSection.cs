using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.10. Exports
        /// </summary>
        public Export[] Exports { get; internal set; } = null!;

        /// <summary>
        /// @Spec 2.5.10. Exports
        /// </summary>
        public class Export
        {
            public string Name { get; internal set; } = null!;

            public ExportDesc Desc { get; internal set; } = null!;

            /// <summary>
            /// @Spec 3.4.8.1.
            /// </summary>
            public class Validator : AbstractValidator<Export>
            {
                public Validator()
                {
                    RuleFor(e => e.Desc).SetInheritanceValidator(v =>
                    {
                        v.Add(new ExportDesc.FuncDesc.Validator());
                        v.Add(new ExportDesc.TableDesc.Validator());
                        v.Add(new ExportDesc.MemDesc.Validator());
                    });
                }
            }
        }
        
        
        public abstract class ExportDesc
        {
            public class FuncDesc : ExportDesc
            {
                public FuncIdx FunctionIndex { get; internal set; }

                /// <summary>
                /// @Spec 3.4.8.2. func
                /// </summary>
                public class Validator : AbstractValidator<FuncDesc>
                {
                    public Validator()
                    {
                        RuleFor(fd => fd.FunctionIndex)
                            .Must((fd, index, ctx) => ctx.GetValidationContext().Funcs.Contains(index));
                    }
                }
            }

            public class TableDesc : ExportDesc
            {
                public TableIdx TableIndex { get; internal set; }
                
                /// <summary>
                /// @Spec 3.4.8.3. table
                /// </summary>
                public class Validator : AbstractValidator<TableDesc>
                {
                    public Validator()
                    {
                        RuleFor(td => td.TableIndex)
                            .Must((fd, index, ctx) => ctx.GetValidationContext().Tables.Contains(index));
                    }
                }
            }

            public class MemDesc : ExportDesc
            {
                public MemIdx MemoryIndex { get; internal set; }
                
                /// <summary>
                /// @Spec 3.4.8.4. mem
                /// </summary>
                public class Validator : AbstractValidator<MemDesc>
                {
                    public Validator()
                    {
                        RuleFor(md => md.MemoryIndex)
                            .Must((md, index, ctx) => ctx.GetValidationContext().Mems.Contains(index));
                    }
                }
            }

            public class GlobalDesc : ExportDesc
            {
                public GlobalIdx GlobalIndex { get; internal set; }
                
                /// <summary>
                /// @Spec 3.4.8.5. global
                /// </summary>
                public class Validator : AbstractValidator<GlobalDesc>
                {
                    public Validator()
                    {
                        RuleFor(gd => gd.GlobalIndex)
                            .Must((gd, index, ctx) => ctx.GetValidationContext().ExecContext.Globals.Contains(index));
                    }
                }
            }
            
        }
    }
    
    public static partial class BinaryModuleParser
    {
        private static Module.ExportDesc ParseExportDesc(BinaryReader reader) =>
            ExternalKindParser.Parse(reader) switch {
                ExternalKind.Function => new Module.ExportDesc.FuncDesc { FunctionIndex = (FuncIdx)reader.ReadLeb128_u32()},
                ExternalKind.Table => new Module.ExportDesc.TableDesc { TableIndex = (TableIdx)reader.ReadLeb128_u32()},
                ExternalKind.Memory => new Module.ExportDesc.MemDesc { MemoryIndex = (MemIdx)reader.ReadLeb128_u32() },
                ExternalKind.Global => new Module.ExportDesc.GlobalDesc { GlobalIndex = (GlobalIdx)reader.ReadLeb128_u32()},
                var kind => throw new InvalidDataException($"Malformed Module Export section {kind} at {reader.BaseStream.Position - 1}")
            };
            
        private static Module.Export ParseExport(BinaryReader reader) =>
            new Module.Export {
                Name = reader.ReadUTF8String(),
                Desc = ParseExportDesc(reader)
            };
        
        /// <summary>
        /// @Spec 5.5.10 Export Section
        /// </summary>
        private static Module.Export[] ParseExportSection(BinaryReader reader) =>
            reader.ParseVector(ParseExport);

    }
}