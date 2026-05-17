// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Orchestration for <c>wacs aot</c>: transpile → scaffold → publish.
    /// See <see cref="AotOptions"/> for scope and limitations.
    /// </summary>
    public static class AotHandler
    {
        public static int Execute(AotOptions opts)
        {
            if (string.IsNullOrEmpty(opts.Input) || !File.Exists(opts.Input))
            {
                System.Console.Error.WriteLine("error: input wasm not found: " + opts.Input);
                return 1;
            }

            string inputAbs = Path.GetFullPath(opts.Input);
            string baseName = Path.GetFileNameWithoutExtension(inputAbs);
            string rid = string.IsNullOrEmpty(opts.RuntimeIdentifier)
                ? RuntimeInformation.RuntimeIdentifier
                : opts.RuntimeIdentifier;

            string outputAbs = Path.GetFullPath(string.IsNullOrEmpty(opts.Output)
                ? baseName + (rid.StartsWith("win") ? ".exe" : "")
                : opts.Output);

            string tempDir = Path.Combine(Path.GetTempPath(), $"wacs-aot-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            void Log(string msg) { if (opts.Verbose) System.Console.WriteLine("  " + msg); }
            Log($"work dir: {tempDir}");

            try
            {
                // Step 1 — Transpile via the existing BuildHandler. With
                // --wasi or --wasip2, the transpile step binds the host
                // package during instantiation (otherwise unresolved
                // imports trip the parser); skip --emit-main because the
                // current emit-main rejects modules with imports
                // (core-mode) or runs through reflection-heavy
                // ComponentMainHost (component-mode) — we'll write our
                // own Program.cs in step 3 that wires up source-gen
                // adapters / DI bundles instead.
                var assemblyName = SanitizeAssemblyName(baseName);
                string emission = opts.AotLinked ? "aot-linked" : "standard";
                bool useEmitMain = !opts.Wasi && !opts.Wasip2;
                string emitMode = useEmitMain ? "--emit-main"
                    : (opts.Wasip2 ? "--wasip2" : "--wasi");
                Log($"transpile → {assemblyName}.dll (emission={emission}, {emitMode})");
                var dllPath = Path.Combine(tempDir, assemblyName + ".dll");
                var buildOpts = new BuildOptions
                {
                    Files = new[] { inputAbs },
                    Output = dllPath,
                    AssemblyName = assemblyName,
                    Emission = emission,
                    Namespace = opts.Namespace,
                    ModuleName = "Module",
                    Simd = opts.Simd,
                    EmitMain = useEmitMain,
                    EntryPoint = opts.EntryPoint,
                    MainClass = "Program",
                    DataStorage = "static",
                    Wasi = opts.Wasi,
                    Wasip2 = opts.Wasip2,
                    Harness = opts.Harness,
                    WitDir = opts.WitDir,
                };
                int buildRc = BuildHandler.Execute(buildOpts);
                if (buildRc != 0)
                {
                    System.Console.Error.WriteLine("error: transpile step failed (rc=" + buildRc + ")");
                    return buildRc;
                }
                if (!File.Exists(dllPath))
                {
                    System.Console.Error.WriteLine("error: transpile claimed success but " + dllPath + " is missing");
                    return 1;
                }

                // Step 2 — Locate the WACS runtime support assemblies that the
                // transpiled .dll will reference at runtime. Use the same
                // .dll's the wacs CLI is currently loading so the versions
                // match exactly, regardless of nuget vs source build.
                string wacsCoreDll = typeof(WasmRuntime).Assembly.Location;
                string wacsTranspilerLibDll = typeof(ModuleTranspiler).Assembly.Location;
                if (string.IsNullOrEmpty(wacsCoreDll) || !File.Exists(wacsCoreDll))
                {
                    System.Console.Error.WriteLine("error: cannot locate Wacs.Core.dll on disk (CLI is running from a non-file-backed assembly?)");
                    return 1;
                }
                Log($"runtime support: {wacsCoreDll}");
                Log($"runtime support: {wacsTranspilerLibDll}");

                string? wacsWasiDll = null;
                string? hostBindingsAbstractionsDll = null;
                string? hostBindingsSourceGenDll = null;
                string? wacsWasip2Dll = null;
                string? wacsWasip2DiDll = null;
                string? wacsComponentModelDll = null;
                if (opts.Wasi)
                {
                    // Wacs.WASI.Preview1 holds the [WacsImport] bindings.
                    wacsWasiDll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.WASI.Preview1");
                    hostBindingsAbstractionsDll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.HostBindings.Abstractions");
                    hostBindingsSourceGenDll = LocateAnalyzerDll();
                    if (wacsWasiDll == null || hostBindingsAbstractionsDll == null || hostBindingsSourceGenDll == null)
                    {
                        System.Console.Error.WriteLine(
                            "error: --wasi requires Wacs.WASI.Preview1.dll, Wacs.HostBindings.Abstractions.dll, "
                            + "and Wacs.HostBindings.SourceGen.dll alongside the CLI. Found: "
                            + $"WASI={wacsWasiDll}, Abs={hostBindingsAbstractionsDll}, SrcGen={hostBindingsSourceGenDll}");
                        return 1;
                    }
                    Log($"wasi bindings:   {wacsWasiDll}");
                    Log($"host bindings:   {hostBindingsAbstractionsDll}");
                    Log($"source generator: {hostBindingsSourceGenDll}");
                }
                if (opts.Wasip2)
                {
                    wacsWasip2Dll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.WASI.Preview2");
                    wacsWasip2DiDll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.WASI.Preview2.DependencyInjection");
                    wacsComponentModelDll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.ComponentModel");
                    // Source generator is needed for the (likely-empty) IImports adapter on the component.
                    hostBindingsAbstractionsDll = LocateAssemblyByName(typeof(WasmRuntime).Assembly, "Wacs.HostBindings.Abstractions");
                    hostBindingsSourceGenDll = LocateAnalyzerDll();
                    if (wacsWasip2Dll == null || wacsWasip2DiDll == null || wacsComponentModelDll == null
                        || hostBindingsAbstractionsDll == null || hostBindingsSourceGenDll == null)
                    {
                        System.Console.Error.WriteLine(
                            "error: --wasip2 requires Wacs.WASI.Preview2.dll, "
                            + "Wacs.WASI.Preview2.DependencyInjection.dll, Wacs.ComponentModel.dll, "
                            + "Wacs.HostBindings.Abstractions.dll, and Wacs.HostBindings.SourceGen.dll "
                            + "alongside the CLI.");
                        return 1;
                    }
                    Log($"wasip2 bindings: {wacsWasip2Dll}");
                    Log($"wasip2 DI:       {wacsWasip2DiDll}");
                    Log($"component model: {wacsComponentModelDll}");
                }

                // Step 3 — Scaffold the throwaway consumer csproj + Program.cs.
                string hostName = assemblyName + ".host";
                string csprojPath = Path.Combine(tempDir, hostName + ".csproj");
                string programCsPath = Path.Combine(tempDir, "Program.cs");
                string mainTypeFqn = $"{opts.Namespace}.Module.Program";

                if (opts.Wasi)
                {
                    File.WriteAllText(csprojPath,
                        GenerateWasiConsumerCsproj(assemblyName, dllPath,
                            wacsCoreDll, wacsTranspilerLibDll,
                            wacsWasiDll!, hostBindingsAbstractionsDll!, hostBindingsSourceGenDll!));
                    File.WriteAllText(programCsPath,
                        GenerateWasiConsumerProgramCs(opts.Namespace, opts.Preopen?.ToList()));
                }
                else if (opts.Wasip2)
                {
                    File.WriteAllText(csprojPath,
                        GenerateWasip2ConsumerCsproj(assemblyName, dllPath,
                            wacsCoreDll, wacsTranspilerLibDll,
                            wacsWasip2Dll!, wacsWasip2DiDll!, wacsComponentModelDll!,
                            hostBindingsAbstractionsDll!, hostBindingsSourceGenDll!));
                    File.WriteAllText(programCsPath,
                        GenerateWasip2ConsumerProgramCs(opts.Namespace, opts.EntryPoint));
                }
                else
                {
                    File.WriteAllText(csprojPath,
                        GenerateConsumerCsproj(assemblyName, dllPath, wacsCoreDll, wacsTranspilerLibDll));
                    File.WriteAllText(programCsPath,
                        GenerateConsumerProgramCs(mainTypeFqn));
                }
                Log($"scaffold {hostName}.csproj + Program.cs");

                // Step 4 — dotnet publish.
                string publishDir = Path.Combine(tempDir, "publish");
                Log($"dotnet publish -c Release -r {rid}");
                if (!RunDotnetPublish(csprojPath, rid, publishDir, opts.Verbose))
                    return 1;

                // Step 5 — Copy the native binary out to the user's chosen output.
                string nativeName = hostName + (rid.StartsWith("win") ? ".exe" : "");
                string nativePath = Path.Combine(publishDir, nativeName);
                if (!File.Exists(nativePath))
                {
                    System.Console.Error.WriteLine("error: NativeAOT publish did not produce expected binary at " + nativePath);
                    return 1;
                }
                var outputDir = Path.GetDirectoryName(outputAbs);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);
                File.Copy(nativePath, outputAbs, overwrite: true);
                // Skip if either the target RID is Windows (the
                // produced .exe doesn't honor Unix mode bits) or
                // the host we're running on is Windows
                // (File.SetUnixFileMode throws PlatformNotSupported
                // there — guard with OperatingSystem.IsWindows so
                // the CA1416 analyzer sees the platform check).
                if (!rid.StartsWith("win") && !OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(outputAbs,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute); } catch { /* best-effort */ }
                }

                var size = new FileInfo(outputAbs).Length;
                System.Console.WriteLine($"wrote {outputAbs} ({size:N0} bytes, native binary, rid={rid})");
                return 0;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("error: " + ex.Message);
                if (opts.Verbose) System.Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                if (!opts.KeepTemp)
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
                }
                else
                {
                    System.Console.WriteLine($"  (--keep-temp) build dir preserved at {tempDir}");
                }
            }
        }

        private static string SanitizeAssemblyName(string baseName)
        {
            var sb = new System.Text.StringBuilder(baseName.Length);
            foreach (var c in baseName)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        // Locate a .dll by simple name in the directory of the CLI's loaded
        // Wacs.Core assembly. Works for both dev (bin/Release/net8.0/) and
        // tool-installed (~/.nuget/packages/...) shapes — anything that puts
        // the WACS pieces side-by-side at load time.
        private static string? LocateAssemblyByName(Assembly anchorInDir, string simpleName)
        {
            var dir = Path.GetDirectoryName(anchorInDir.Location);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, simpleName + ".dll");
            return File.Exists(path) ? path : null;
        }

        // The source generator's analyzer .dll lives in a sibling location
        // when shipped as a NuGet package (analyzers/dotnet/cs) but we're
        // not running from the package — when running from the source
        // checkout it sits next to the dotnet exec. Probe both.
        //
        // Source-checkout path under the new hierarchical layout:
        //   <repo>/Wacs.HostBindings/Wacs.HostBindings.SourceGen/bin/Release/netstandard2.0/
        // CLI's load location moved one folder deeper too
        //   <repo>/Wacs.Console/Wacs.Console/bin/Release/netN.0/
        // — so we walk up until we find WACS.sln rather than counting
        // hardcoded `..` levels. Robust to future reorgs at any depth.
        private static string? LocateAnalyzerDll()
        {
            // CLI dir first (where the dll lands when packaged as an
            // analyzer at compile time, or when an embedder explicitly
            // copies it next to the apphost).
            var cliDir = Path.GetDirectoryName(typeof(AotHandler).Assembly.Location);
            if (!string.IsNullOrEmpty(cliDir))
            {
                var p = Path.Combine(cliDir!, "Wacs.HostBindings.SourceGen.dll");
                if (File.Exists(p)) return p;
            }
            // Source-checkout fallback.
            var repoRoot = WalkToRepoRoot(cliDir);
            if (repoRoot != null)
            {
                var p = Path.Combine(repoRoot.FullName,
                    "Wacs.HostBindings", "Wacs.HostBindings.SourceGen",
                    "bin", "Release", "netstandard2.0",
                    "Wacs.HostBindings.SourceGen.dll");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Walk up the directory tree from <paramref name="startDir"/>
        /// until we find a folder containing <c>WACS.sln</c>; null if we
        /// hit the filesystem root first. Used by the source-checkout
        /// fallback for the analyzer dll — the CLI's bin path depth
        /// changed when projects moved into family folders, and tying
        /// to a repo-root marker is more robust than counting `..`.
        /// </summary>
        private static DirectoryInfo? WalkToRepoRoot(string? startDir)
        {
            if (string.IsNullOrEmpty(startDir)) return null;
            var dir = new DirectoryInfo(startDir!);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                    return dir;
                dir = dir.Parent;
            }
            return null;
        }

        // ---- Compute-only consumer (no host bindings) ----

        private static string GenerateConsumerCsproj(
            string assemblyName, string dllPath,
            string wacsCoreDll, string wacsTranspilerLibDll) =>
$@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>9</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>{assemblyName}.Host</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Wacs.Core""><HintPath>{wacsCoreDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.Transpiler.Lib""><HintPath>{wacsTranspilerLibDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""{assemblyName}""><HintPath>{dllPath}</HintPath><Private>true</Private></Reference>
  </ItemGroup>
</Project>
";

        private static string GenerateConsumerProgramCs(string mainTypeFqn) =>
$@"// Auto-generated by `wacs aot`. Forwards to the transpiled module's
// emitted Main. Don't edit — this is regenerated each build.
return {mainTypeFqn}.Main(args);
";

        // ---- WASI consumer (host bindings + source gen) ----

        private static string GenerateWasiConsumerCsproj(
            string assemblyName, string dllPath,
            string wacsCoreDll, string wacsTranspilerLibDll,
            string wacsWasiDll, string hostBindingsAbstractionsDll,
            string hostBindingsSourceGenDll) =>
$@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>10</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>{assemblyName}.Host</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
    <PublishAot>true</PublishAot>
    <!-- The MemoryProvider lambda casts MemoryInstance.NativeBase
         (byte*) to IntPtr in NativePointer mode. -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Wacs.Core""><HintPath>{wacsCoreDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.Transpiler.Lib""><HintPath>{wacsTranspilerLibDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.WASI.Preview1""><HintPath>{wacsWasiDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.HostBindings.Abstractions""><HintPath>{hostBindingsAbstractionsDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""{assemblyName}""><HintPath>{dllPath}</HintPath><Private>true</Private></Reference>
    <Analyzer Include=""{hostBindingsSourceGenDll}"" />
  </ItemGroup>
</Project>
";

        private static string GenerateWasiConsumerProgramCs(
            string @namespace, List<string>? preopens)
        {
            var preopenLines = new System.Text.StringBuilder();
            if (preopens != null)
            {
                foreach (var pre in preopens)
                {
                    var idx = pre.IndexOf("::", StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var host = pre.Substring(0, idx).Trim();
                    var guest = pre.Substring(idx + 2).Trim();
                    preopenLines.Append("config.PreopenedDirectories.Add(new Wacs.WASI.Preview1.Types.PreopenedDirectory(config, ");
                    preopenLines.Append('"').Append(host).Append('"');
                    preopenLines.Append(", ").Append('"').Append(guest).Append('"').AppendLine("));");
                }
            }

            return
$@"// Auto-generated by `wacs aot --wasi`. Wires WASI Preview 1 bindings
// (via WACS.HostBindings.SourceGen) and invokes the wasm's _start.
using System;
using System.Collections.Generic;
using {@namespace}.Module;
using {@namespace}.Module.WacsGenerated;
using Wacs.HostBindings;
using Wacs.WASI.Preview1;

var config = new WasiConfiguration();
config.Arguments = new List<string>(args);
// Default to the working dir as the host root (BindPreopenedDirs validates
// this is non-empty even when PreopenHostRootDirectory is false).
config.HostRootDirectory = Environment.CurrentDirectory;
config.PreopenHostRootDirectory = false;
config.StandardInput  = Console.OpenStandardInput();
config.StandardOutput = Console.OpenStandardOutput();
config.StandardError  = Console.OpenStandardError();

{preopenLines}var state = new State();
_ = new Wacs.WASI.Preview1.FileSystem(config, state);

// Source-gen emits a parameterless ctor + a public property per state
// type (named after the type's short name). We assign every state
// object we have on hand; if this wasm's bindings don't pull on one of
// them the property simply stays unread. This avoids coupling the
// scaffold to whichever ctor arity the source-gen happens to emit for
// the particular set of bindings the consumer's wasm needs.
var imports = new GeneratedHostImports();
imports.State = state;
imports.WasiConfiguration = config;
Module module = null!;
// Module.Memory returns the live MemoryInstance; mode-aware
// dispatch into WacsHostMemory's two backings keeps the host
// binding compatible with both ManagedArray and NativePointer
// (>2 GiB) memory storage selectors.
imports.MemoryProvider = () => {{
    var m = module.Memory;
    unsafe
    {{
        if (m.StorageMode == Wacs.Core.Runtime.MemoryStorageMode.NativePointer)
        {{
            int nlen = (int)System.Math.Min(
                (ulong)m.NativeSize, (ulong)int.MaxValue);
            return new WacsHostMemory((System.IntPtr)m.NativeBase, nlen);
        }}
    }}
    return new WacsHostMemory(m.Data, m.Data.Length);
}};
module = new Module(imports);

try
{{
    module._start();
    return state.ExitCode;
}}
catch (Wacs.Core.WASIp1.SystemExitException ex)
{{
    return ex.Signal;
}}
";
        }

        // ---- WASI Preview 2 (component-mode) consumer ----

        private static string GenerateWasip2ConsumerCsproj(
            string assemblyName, string dllPath,
            string wacsCoreDll, string wacsTranspilerLibDll,
            string wacsWasip2Dll, string wacsWasip2DiDll, string wacsComponentModelDll,
            string hostBindingsAbstractionsDll, string hostBindingsSourceGenDll) =>
$@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>10</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>{assemblyName}.Host</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
    <PublishAot>true</PublishAot>
    <!-- Preview 2's DI plumbing trips a few generic-instantiation
         analyzer warnings under PublishAot; the bundle construction is
         all typed so they're benign at runtime. -->
    <NoWarn>$(NoWarn);IL2026;IL2070;IL2075;IL2087;IL3050</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Wacs.Core""><HintPath>{wacsCoreDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.Transpiler.Lib""><HintPath>{wacsTranspilerLibDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.ComponentModel""><HintPath>{wacsComponentModelDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.WASI.Preview2""><HintPath>{wacsWasip2Dll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.WASI.Preview2.DependencyInjection""><HintPath>{wacsWasip2DiDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""Wacs.HostBindings.Abstractions""><HintPath>{hostBindingsAbstractionsDll}</HintPath><Private>true</Private></Reference>
    <Reference Include=""{assemblyName}""><HintPath>{dllPath}</HintPath><Private>true</Private></Reference>
    <Analyzer Include=""{hostBindingsSourceGenDll}"" />
    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""8.0.0"" />
  </ItemGroup>
</Project>
";

        private static string GenerateWasip2ConsumerProgramCs(string @namespace, string entryPoint)
        {
            // Sanitize the wasm export name to a C# identifier. The
            // transpiler does the same in InterfaceGenerator.SanitizeName
            // (alphanumerics + '_' kept verbatim, hyphens → '_').
            var clrEntryPoint = SanitizeExportName(entryPoint);
            return
$@"// Auto-generated by `wacs aot --wasip2`. Constructs a
// WasiPreview2Bundle via Microsoft.Extensions.DependencyInjection,
// instantiates the transpiled component, and invokes its export.
using System;
using {@namespace}.Module;
using {@namespace}.Module.WacsGenerated;
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;

var services = new ServiceCollection();
services.AddWasiPreview2();
var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();
var bundle = scope.ServiceProvider.GetRequiredService<WasiPreview2Bundle>();
var resources = scope.ServiceProvider.GetRequiredService<WasiPreview2Resources>();

// Component-mode IImports is direct-linked: every wasi:* import was
// resolved at transpile time, so the source-generated GeneratedHostImports
// is essentially an empty adapter. Pass it as the IImports arg.
var imports = new GeneratedHostImports();

var module = new Module(imports, bundle, resources);
try
{{
    module.{clrEntryPoint}();
}}
catch (Wacs.WASI.Preview2.Cli.ExitException ex)
{{
    return ex.ExitCode;
}}
return 0;
";
        }

        // Mirror InterfaceGenerator.SanitizeName: keep alphanumerics +
        // '_', map any other byte to '_'. Avoids guest export names
        // like ""set-pixel"" surfacing as ""set_pixel"" in C# and
        // matches what the transpiler emitted.
        private static string SanitizeExportName(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        // ---- publish helper ----

        private static bool RunDotnetPublish(string csprojPath, string rid, string publishDir, bool verbose)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = !verbose,
                RedirectStandardError = !verbose,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add(csprojPath);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(rid);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(publishDir);
            psi.ArgumentList.Add("--nologo");

            string stdout = "", stderr = "";
            using var p = Process.Start(psi)!;
            if (!verbose)
            {
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
            }
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                System.Console.Error.WriteLine($"error: dotnet publish failed (rc={p.ExitCode})");
                if (!verbose && (stdout.Length > 0 || stderr.Length > 0))
                {
                    System.Console.Error.WriteLine("--- stdout ---");
                    System.Console.Error.Write(stdout);
                    System.Console.Error.WriteLine("--- stderr ---");
                    System.Console.Error.Write(stderr);
                }
                return false;
            }
            return true;
        }
    }
}
