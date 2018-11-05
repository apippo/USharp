﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnrealEngine.Runtime.Native;
using UnrealEngine.Engine;
using System.IO;

namespace UnrealEngine.Runtime
{
    public partial class CodeGenerator
    {
        private static CodeGenerator timeSlicedCodeGenerator = null;

        internal static void OnNativeFunctionsRegistered()
        {
            IConsoleManager.Get().RegisterConsoleCommand("USharpGen", "USharp generate C# code", GenerateCode);
            //IConsoleManager.Get().RegisterConsoleCommand("USharpGenSliced", "USharp generate C# code", GenerateCodeTimeSliced);

            // Move these commands somewhere else?
            IConsoleManager.Get().RegisterConsoleCommand("USharpRuntime", "Sets the .NET runtime that USharp will use (Mono/CLR)", SetDotNetRuntime);
            IConsoleManager.Get().RegisterConsoleCommand("USharpMinHotReload", "USharp hotreload will skip reintancing / CDO checks", SetMinimalHotReload);
        }

        private static unsafe void SetDotNetRuntime(string[] args)
        {
            const string enableDualRuntimesStr = 
                "Dual runtimes are not enabled. Add 'dual' to /USharp/Binaries/Mono/DotNetRuntime.txt and reopen the editor.";

            if (args != null && args.Length > 0)
            {
                if (SharedRuntimeState.IsRuntimeLoaded(EDotNetRuntime.Dual))
                {
                    EDotNetRuntime runtime = EDotNetRuntime.None;
                    switch (args[0].ToLower())
                    {
                        case "mono":
                            runtime = EDotNetRuntime.Mono;
                            break;
                        case "clr":
                            runtime = EDotNetRuntime.CLR;
                            break;
                        case "swap":
                            if (SharedRuntimeState.CurrentRuntime == EDotNetRuntime.CLR)
                            {
                                runtime = EDotNetRuntime.Mono;
                            }
                            else
                            {
                                runtime = EDotNetRuntime.CLR;
                            }
                            break;
                    }
                    if (runtime == EDotNetRuntime.None)
                    {
                        FMessage.Log("Unknown runtime '" + args[0] + "'. Available runtimes: Mono, CLR");
                    }
                    else if (SharedRuntimeState.Instance->NextRuntime != EDotNetRuntime.None)
                    {
                        FMessage.Log("Runtime change already queued (" + SharedRuntimeState.Instance->NextRuntime.ToString() + ")");
                    }
                    else if (SharedRuntimeState.Instance->ActiveRuntime == runtime)
                    {
                        FMessage.Log(runtime.ToString() + " is already the active runtime");
                    }
                    else
                    {
                        SharedRuntimeState.Instance->RuntimeCounter++;
                        SharedRuntimeState.Instance->NextRuntime = runtime;
                        FMessage.Log("Changing runtime to " + runtime.ToString() + "...");
                    }
                }
                else
                {
                    FMessage.Log(enableDualRuntimesStr);
                    FMessage.Log("Runtime: " + SharedRuntimeState.GetRuntimeInfo());
                }
            }
            else
            {
                if (!SharedRuntimeState.IsRuntimeLoaded(EDotNetRuntime.Dual))
                {
                    FMessage.Log(enableDualRuntimesStr);
                }
                FMessage.Log("Runtime: " + SharedRuntimeState.GetRuntimeInfo());
            }
        }

        private static void SetMinimalHotReload(string[] args)
        {
            bool value;
            if (args != null && args.Length >= 1 && bool.TryParse(args[0], out value))
            {
            }
            else
            {
                value = !Native_SharpHotReloadUtils.Get_MinimalHotReload();
            }
            Native_SharpHotReloadUtils.Set_MinimalHotReload(value);
            FMessage.Log("USharpMinHotReload: " + (value ? "Enabled" : "Disabled"));
        }

        private static void GenerateCode(string[] args)
        {
            GenerateCode(false, args);
        }

        private static void CompileGeneratedCode()
        {
            CodeGeneratorSettings _settings = new CodeGeneratorSettings();
            string _slnPath = Path.GetFullPath(Path.Combine(_settings.GetManagedModulesDir(), "UnrealEngine.sln"));
            string _projPath = Path.GetFullPath(Path.Combine(_settings.GetManagedModulesDir(), "UnrealEngine.csproj"));
            string _pluginInstallerPath = Path.GetFullPath(Path.Combine(_settings.GetManagedModulesDir(), "../", "../", "../", "Binaries", "Managed", "PluginInstaller", "PluginInstaller.exe"));
            
            if (!File.Exists(_slnPath))
            {
                Log(ELogVerbosity.Error, "Can't Compile: The Solution " + _slnPath + " doesn't exist");
                return;
            }
            if (!File.Exists(_projPath))
            {
                Log(ELogVerbosity.Error, "Can't Compile: The Project " + _projPath + " doesn't exist");
                return;
            }
            if (!File.Exists(_pluginInstallerPath))
            {
                Log(ELogVerbosity.Error, "Can't Compile: Can't Find Plugin Installer At Path: " + _pluginInstallerPath);
                return;
            }

            Log(ELogVerbosity.Log, "Attempting To Build Generated Solution at " + _slnPath);

            int timeout = 60000;
            bool built = false;
            int _exitCode = 0;
            string _arguments = "buildcustomsln" + @" """ + _slnPath + @""" """ + _projPath + @""" """ + "command" + @"""";

            using (System.Diagnostics.Process process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo()
                {
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                    FileName = _pluginInstallerPath,
                    Arguments = _arguments,
                    UseShellExecute = false
                };
                process.Start();

                built = process.WaitForExit(timeout) && process.ExitCode == 0;
                _exitCode = process.ExitCode;
            }

            if (built)
            {
                Log(ELogVerbosity.Log, "Solution Was Compiled Successfully.");
            }
            else if(_exitCode == 1)
            {
                Log(ELogVerbosity.Error, "There was an error building the Solution, Please Try Compiling Manually At " + _slnPath);

            }
            else if(_exitCode == 2)
            {
                Log(ELogVerbosity.Error, "Couldn't Build Custom Solution Because Files Provided were Invalid. Arguments: " + _arguments);
            }
            else if(_exitCode == 3)
            {
                Log(ELogVerbosity.Error, "Didn't provide the correct number of arguments for buildcustomsln command. Arguments: " + _arguments);
            }
            else
            {
                Log(ELogVerbosity.Error, "Couldn't Compile Solution, Please Try Compiling Manually At " + _slnPath);
            }
        }

        private static void GenerateCodeTimeSliced(string[] args)
        {
            GenerateCode(true, args);
        }

        private static void GenerateCode(bool timeSliced, string[] args)
        {
            try
            {
                bool invalidArgs = false;

                if (args.Length > 0)
                {
                    if (args[0] == "cancel")
                    {
                        if (timeSlicedCodeGenerator != null && !timeSlicedCodeGenerator.Complete)
                        {
                            timeSlicedCodeGenerator.EndGenerateModules();
                        }
                        timeSlicedCodeGenerator = null;
                        return;
                    }

                    if (timeSlicedCodeGenerator != null)
                    {
                        FMessage.Log("Already generating code");
                        return;
                    }

                    CodeGenerator codeGenerator = null;

                    switch (args[0])
                    {
                        case "game":
                            AssetLoadMode loadMode = AssetLoadMode.Game;
                            bool clearAssetCache = false;
                            bool skipLevels = false;
                            if (args.Length > 1)
                            {
                                switch (args[1])
                                {
                                    case "game":
                                        loadMode = CodeGenerator.AssetLoadMode.Game;
                                        break;
                                    case "engine":
                                        loadMode = CodeGenerator.AssetLoadMode.Engine;
                                        break;
                                    case "all":
                                        loadMode = CodeGenerator.AssetLoadMode.All;
                                        break;
                                }
                            }
                            if (args.Length > 2)
                            {
                                bool.TryParse(args[2], out clearAssetCache);
                            }
                            if (args.Length > 3)
                            {
                                bool.TryParse(args[3], out skipLevels);
                            }
                            codeGenerator = new CodeGenerator(timeSliced);
                            codeGenerator.GenerateCodeForGame(loadMode, clearAssetCache, skipLevels);
                            break;

                        case "gameplugins":
                            codeGenerator = new CodeGenerator(timeSliced);
                            codeGenerator.GenerateCodeForModules(new UnrealModuleType[] { UnrealModuleType.GamePlugin });
                            break;

                        case "modules":
                            codeGenerator = new CodeGenerator(timeSliced);
                            //codeGenerator.Settings.ExportMode = CodeGeneratorSettings.CodeExportMode.All;
                            //codeGenerator.Settings.ExportAllFunctions = true;
                            //codeGenerator.Settings.ExportAllProperties = true;
                            codeGenerator.GenerateCodeForAllModules();
                            break;

                        case "module":
                            if (args.Length > 1)
                            {
                                codeGenerator = new CodeGenerator(timeSliced);
                                // Tests / using these for types for use in this lib
                                //codeGenerator.Settings.CheckUObjectDestroyed = false;
                                //codeGenerator.Settings.GenerateIsValidSafeguards = false;
                                //codeGenerator.Settings.ExportMode = CodeGeneratorSettings.CodeExportMode.All;
                                //codeGenerator.Settings.ExportAllFunctions = true;
                                //codeGenerator.Settings.ExportAllProperties = true;
                                //codeGenerator.Settings.MergeEnumFiles = false;
                                codeGenerator.GenerateCodeForModule(args[1], true);
                            }
                            else
                            {
                                invalidArgs = true;
                            }
                            break;

                        case "compile":
                            CompileGeneratedCode();
                            break;

                        default:
                            invalidArgs = true;
                            break;
                    }

                    if (!invalidArgs && timeSliced && codeGenerator != null)
                    {
                        timeSlicedCodeGenerator = codeGenerator;
                        Coroutine.StartCoroutine(null, ProcessTimeSlice());
                    }
                }
                else
                {
                    invalidArgs = true;
                }

                if (invalidArgs)
                {
                    FMessage.Log(ELogVerbosity.Warning, "Invalid input. Provide one of the following: game, gameplugins, modules, module [ModuleName], compile");
                }
            }
            catch(Exception e)
            {
                FMessage.Log(ELogVerbosity.Error, "Generate code failed. Error: \n" + e);
            }
        }

        private static System.Collections.IEnumerator ProcessTimeSlice()
        {
            if (timeSlicedCodeGenerator != null && !timeSlicedCodeGenerator.Complete && timeSlicedCodeGenerator.TimeSliced)
            {
                while (timeSlicedCodeGenerator != null && !timeSlicedCodeGenerator.Complete)
                {
                    timeSlicedCodeGenerator.Process();
                    yield return null;
                }
                if (timeSlicedCodeGenerator != null && timeSlicedCodeGenerator.Complete)
                {
                    timeSlicedCodeGenerator = null;
                }
                yield break;
            }
            else
            {
                timeSlicedCodeGenerator = null;
                yield break;
            }
        }

        private static void Log(string value, params object[] args)
        {
            Log(ELogVerbosity.Log, value, args);
        }

        private static void Log(ELogVerbosity verbosity, string value, params object[] args)
        {
            FMessage.Log("CodeGenerator.Commands", verbosity, string.Format(value, args));
        }
    }
}
