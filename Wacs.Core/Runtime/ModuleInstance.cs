using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Represents an instantiated WebAssembly module, containing the runtime instances of functions, tables, memories, and globals.
    /// </summary>
    public class ModuleInstance
    {
        /// <summary>
        /// The list of function instances exported by the module.
        /// </summary>
        public List<FunctionInstance> Functions { get; }

        /// <summary>
        /// The list of table instances exported by the module.
        /// </summary>
        public List<TableInstance> Tables { get; }

        /// <summary>
        /// The list of memory instances exported by the module.
        /// </summary>
        public List<MemoryInstance> Memories { get; }

        /// <summary>
        /// The dictionary of global instances exported by the module.
        /// </summary>
        public Dictionary<uint, GlobalInstance> Globals { get; }

        /// <summary>
        /// The export map, mapping export names to their corresponding external values.
        /// </summary>
        public Dictionary<string, ExportInstance> Exports { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModuleInstance"/> class.
        /// </summary>
        public ModuleInstance()
        {
            Functions = new List<FunctionInstance>();
            Tables = new List<TableInstance>();
            Memories = new List<MemoryInstance>();
            Globals = new Dictionary<uint, GlobalInstance>();
            Exports = new Dictionary<string, ExportInstance>();
        }

        /// <summary>
        /// Retrieves an exported function by its name.
        /// </summary>
        /// <param name="name">The name of the exported function.</param>
        /// <returns>The <see cref="FunctionInstance"/> if found; otherwise, null.</returns>
        public FunctionInstance GetExportedFunction(string name)
        {
            if (Exports.TryGetValue(name, out var export) && export.Kind == ExternalKind.Function)
            {
                return Functions[(int)export.Index];
            }
            return null;
        }

        /// <summary>
        /// Retrieves an exported global by its name.
        /// </summary>
        /// <param name="name">The name of the exported global.</param>
        /// <returns>The <see cref="GlobalInstance"/> if found; otherwise, null.</returns>
        public GlobalInstance GetExportedGlobal(string name)
        {
            if (Exports.TryGetValue(name, out var export) && export.Kind == ExternalKind.Global)
            {
                return Globals[export.Index];
            }
            return null;
        }

        /// <summary>
        /// Retrieves an exported memory by its name.
        /// </summary>
        /// <param name="name">The name of the exported memory.</param>
        /// <returns>The <see cref="MemoryInstance"/> if found; otherwise, null.</returns>
        public MemoryInstance GetExportedMemory(string name)
        {
            if (Exports.TryGetValue(name, out var export) && export.Kind == ExternalKind.Memory)
            {
                return Memories[(int)export.Index];
            }
            return null;
        }

        /// <summary>
        /// Retrieves an exported table by its name.
        /// </summary>
        /// <param name="name">The name of the exported table.</param>
        /// <returns>The <see cref="TableInstance"/> if found; otherwise, null.</returns>
        public TableInstance GetExportedTable(string name)
        {
            if (Exports.TryGetValue(name, out var export) && export.Kind == ExternalKind.Table)
            {
                return Tables[(int)export.Index];
            }
            return null;
        }
    }

    /// <summary>
    /// Represents an exported value from a module instance.
    /// </summary>
    public class ExportInstance
    {
        /// <summary>
        /// The name under which the value is exported.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The kind of external value (function, table, memory, global).
        /// </summary>
        public ExternalKind Kind { get; }

        /// <summary>
        /// The index into the corresponding list in the module instance.
        /// </summary>
        public uint Index { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportInstance"/> class.
        /// </summary>
        /// <param name="name">The export name.</param>
        /// <param name="kind">The kind of external value.</param>
        /// <param name="index">The index into the module instance's list.</param>
        public ExportInstance(string name, ExternalKind kind, uint index)
        {
            Name = name;
            Kind = kind;
            Index = index;
        }
    }
}