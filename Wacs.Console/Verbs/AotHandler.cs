// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Diagnostics;
using System.IO;
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
                // Step 1 — Transpile via the existing BuildHandler. AotLinked
                // emission, stable assembly name, --emit-main so the .dll has
                // its own Program.Main that we can call from the consumer.
                var assemblyName = SanitizeAssemblyName(baseName);
                Log($"transpile → {assemblyName}.dll (aot-linked, --emit-main)");
                var dllPath = Path.Combine(tempDir, assemblyName + ".dll");
                var buildOpts = new BuildOptions
                {
                    Files = new[] { inputAbs },
                    Output = dllPath,
                    AssemblyName = assemblyName,
                    Emission = "aot-linked",
                    Namespace = opts.Namespace,
                    ModuleName = "Module",
                    Simd = opts.Simd,
                    EmitMain = true,
                    EntryPoint = opts.EntryPoint,
                    MainClass = "Program",
                    DataStorage = "static",
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
                // transpiled .dll will reference at runtime (ThinContext lives
                // in Wacs.Transpiler.Lib, Value/Memory/etc. in Wacs.Core). Use
                // the same .dll's the wacs CLI is currently loading so the
                // versions match exactly, regardless of nuget vs source build.
                string wacsCoreDll = typeof(WasmRuntime).Assembly.Location;
                string wacsTranspilerLibDll = typeof(ModuleTranspiler).Assembly.Location;
                if (string.IsNullOrEmpty(wacsCoreDll) || !File.Exists(wacsCoreDll))
                {
                    System.Console.Error.WriteLine("error: cannot locate Wacs.Core.dll on disk (CLI is running from a non-file-backed assembly?)");
                    return 1;
                }
                Log($"runtime support: {wacsCoreDll}");
                Log($"runtime support: {wacsTranspilerLibDll}");

                // Step 3 — Scaffold the throwaway consumer csproj + Program.cs.
                // Name the consumer assembly distinctly from the transpiled
                // .dll so ILC's resolver doesn't conflate the two when both
                // are in the project's reference set (collision causes
                // "Failed to load type 'X.Module.Program' from assembly 'X'").
                string hostName = assemblyName + ".host";
                string csprojPath = Path.Combine(tempDir, hostName + ".csproj");
                string programCsPath = Path.Combine(tempDir, "Program.cs");
                // The transpiler nests its emitted types under
                // <Namespace>.<ModuleName> — Module class at
                // "<ns>.<mod>.Module", Functions at "<ns>.<mod>.Functions",
                // and (with --emit-main) Program at "<ns>.<mod>.Program".
                string mainTypeFqn = $"{opts.Namespace}.Module.Program";
                File.WriteAllText(csprojPath,
                    GenerateConsumerCsproj(assemblyName, dllPath, wacsCoreDll, wacsTranspilerLibDll));
                File.WriteAllText(programCsPath, GenerateConsumerProgramCs(mainTypeFqn));
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
                // Mirror unix executable bits so the user can run it directly.
                if (!rid.StartsWith("win"))
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

        // Keep alphanumerics + dot + underscore; map anything else to '_' so a
        // module name like "my-app.wasm" yields a valid C# identifier prefix.
        private static string SanitizeAssemblyName(string baseName)
        {
            var sb = new System.Text.StringBuilder(baseName.Length);
            foreach (var c in baseName)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');
            // Avoid leading digit (invalid CLR identifier).
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        // Consumer csproj. UndefineProperties keeps PublishAot from
        // propagating into Wacs.Core's netstandard2.1 build, which would
        // trip NETSDK1207. The Reference resolves the stable-named .dll via
        // HintPath; <Private>true</Private> ensures the resolver finds it
        // during ILC's compile pass.
        private static string GenerateConsumerCsproj(
            string assemblyName, string dllPath,
            string wacsCoreDll, string wacsTranspilerLibDll) =>
$@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>9</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>{assemblyName}.Host</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Wacs.Core"">
      <HintPath>{wacsCoreDll}</HintPath>
      <Private>true</Private>
    </Reference>
    <Reference Include=""Wacs.Transpiler.Lib"">
      <HintPath>{wacsTranspilerLibDll}</HintPath>
      <Private>true</Private>
    </Reference>
    <Reference Include=""{assemblyName}"">
      <HintPath>{dllPath}</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
";

        // Top-level Program.cs that delegates to the transpiled .dll's
        // emitted Main. The transpiler's --emit-main bakes
        // {Namespace}.{MainClass}.Main(string[]) into the saved assembly,
        // so the consumer just needs to forward argv.
        private static string GenerateConsumerProgramCs(string mainTypeFqn) =>
$@"// Auto-generated by `wacs aot`. Forwards to the transpiled module's
// emitted Main. Don't edit — this is regenerated each build.
return {mainTypeFqn}.Main(args);
";

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
