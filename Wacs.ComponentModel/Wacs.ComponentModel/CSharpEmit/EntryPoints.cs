// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Builders for the Component Model entry-point strings used in
    /// <c>[DllImport]</c> and <c>[UnmanagedCallersOnly]</c>
    /// attributes. Each kind of entry point has a specific prefix
    /// the canonical ABI recognizes:
    /// <list type="table">
    /// <listheader>
    ///   <term>Prefix</term><description>Use</description>
    /// </listheader>
    /// <item>
    ///   <term>(none)</term>
    ///   <description>Free function inside an imported interface:
    ///     <c>pkg:ns/iface[@ver]</c> — function name goes in the
    ///     <c>EntryPoint</c> field of the <c>DllImport</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>#</c> infix</term>
    ///   <description>Export trampoline:
    ///     <c>pkg:ns/iface[@ver]#fn-name</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>[method]</c></term>
    ///   <description>Resource instance method:
    ///     <c>[method]resource-name.method-name</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>[static]</c></term>
    ///   <description>Resource static method:
    ///     <c>[static]resource-name.method-name</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>[constructor]</c></term>
    ///   <description>Resource constructor:
    ///     <c>[constructor]resource-name</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>[resource-drop]</c></term>
    ///   <description>Resource destructor / drop:
    ///     <c>[resource-drop]resource-name</c>.</description>
    /// </item>
    /// <item>
    ///   <term><c>cabi_post_</c></term>
    ///   <description>Post-return cleanup trampoline for an export:
    ///     <c>cabi_post_pkg:ns/iface[@ver]#fn-name</c>.</description>
    /// </item>
    /// </list>
    /// All builders return the string to go INSIDE the attribute's
    /// <c>EntryPoint</c> field — callers emit the surrounding
    /// <c>[DllImport("pkg:ns/iface[@ver]", EntryPoint = "…")]</c>
    /// or <c>[UnmanagedCallersOnly(EntryPoint = "…")]</c>.
    /// </summary>
    internal static class EntryPoints
    {
        // ---- Prefix constants --------------------------------------------

        public const string MethodPrefix = "[method]";
        public const string StaticPrefix = "[static]";
        public const string ConstructorPrefix = "[constructor]";
        public const string ResourceDropPrefix = "[resource-drop]";
        public const string CabiPostPrefix = "cabi_post_";

        // ---- Interface qualifier (shared between imports/exports) --------

        /// <summary>
        /// <c>pkg:ns/iface[@ver]</c> — the base the
        /// <c>DllImport</c>'s module name uses for every free
        /// function in an imported interface, and the prefix an
        /// export entry point adds <c>#fn</c> onto.
        /// </summary>
        public static string InterfaceBase(CtInterfaceType iface)
        {
            if (iface.Package == null)
                throw new System.ArgumentException(
                    "EntryPoints require a packaged interface.", nameof(iface));
            var sb = new StringBuilder();
            sb.Append(iface.Package.Namespace);
            foreach (var seg in iface.Package.Path)
            {
                sb.Append(':');
                sb.Append(seg);
            }
            sb.Append('/').Append(iface.Name);
            if (iface.Package.Version != null)
            {
                sb.Append('@').Append(iface.Package.Version);
            }
            return sb.ToString();
        }

        // ---- Free function entry points ----------------------------------

        /// <summary>
        /// Import-side free function: EntryPoint is just the function
        /// name — the module is the interface base.
        /// </summary>
        public static string ImportFreeFunction(string fnKebabName) => fnKebabName;

        /// <summary>Export-side free function trampoline.</summary>
        public static string ExportFreeFunction(string interfaceBase, string fnKebabName) =>
            interfaceBase + "#" + fnKebabName;

        /// <summary>Export-side cabi_post cleanup trampoline.</summary>
        public static string CabiPost(string interfaceBase, string fnKebabName) =>
            CabiPostPrefix + interfaceBase + "#" + fnKebabName;

        // ---- Resource entry points ---------------------------------------

        public static string ResourceDrop(string resourceKebabName) =>
            ResourceDropPrefix + resourceKebabName;

        public static string ResourceConstructor(string resourceKebabName) =>
            ConstructorPrefix + resourceKebabName;

        public static string ResourceMethod(string resourceKebabName, string methodKebabName) =>
            MethodPrefix + resourceKebabName + "." + methodKebabName;

        public static string ResourceStatic(string resourceKebabName, string methodKebabName) =>
            StaticPrefix + resourceKebabName + "." + methodKebabName;
    }
}
