using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

/**
 * Original code for this was written by Bepis here - https://dev.sp-tarkov.com/bepis/SPT-AssemblyTool/src/branch/master/SPT-AssemblyTool/Deobfuscator.cs
 */
namespace SIT.Launcher.DeObfus
{
	internal static class Deobfuscator
	{
        public delegate void LogHandler(string text);
        public static event LogHandler OnLog;

        internal static void Log(string text)
        {
            if(OnLog != null)
            {
                OnLog(text);
            }
            else
            {
                Debug.WriteLine(text);
                Console.WriteLine(text);
            }
        }

        internal static bool DeobfuscateAssembly(string assemblyPath, string managedPath, bool createBackup = true, bool overwriteExisting = false, bool doRemapping = false)
        {
            var executablePath = App.ApplicationDirectory;
            var de4dotLocation = Path.Combine(Path.GetDirectoryName(executablePath), "DeObfus", "de4dot", "de4dot.exe");

            string token;

            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyPath))
            {
                var potentialStringDelegates = new List<MethodDefinition>();

                foreach (var type in assemblyDefinition.MainModule.Types)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.ReturnType.FullName != "System.String"
                            || method.Parameters.Count != 1
                            || method.Parameters[0].ParameterType.FullName != "System.Int32"
                            || method.Body == null
                            || !method.IsStatic)
                        {
                            continue;
                        }

                        if (!method.Body.Instructions.Any(x =>
                            x.OpCode.Code == Code.Callvirt &&
                            ((MethodReference)x.Operand).FullName == "System.Object System.AppDomain::GetData(System.String)"))
                        {
                            continue;
                        }

                        potentialStringDelegates.Add(method);
                    }
                }

                if (potentialStringDelegates.Count != 1)
                {
                    //Program.WriteError($"Expected to find 1 potential string delegate method; found {potentialStringDelegates.Count}. Candidates: {string.Join("\r\n", potentialStringDelegates.Select(x => x.FullName))}");
                }

                var deobfRid = potentialStringDelegates[0].MetadataToken;

                token = $"0x{((uint)deobfRid.TokenType | deobfRid.RID):x4}";

                Console.WriteLine($"Deobfuscation token: {token}");
            }

            var process = Process.Start(de4dotLocation,
                $"--un-name \"!^<>[a-z0-9]$&!^<>[a-z0-9]__.*$&![A-Z][A-Z]\\$<>.*$&^[a-zA-Z_<{{$][a-zA-Z_0-9<>{{}}$.`-]*$\" \"{assemblyPath}\" --strtyp delegate --strtok \"{token}\"");

            process.WaitForExit();


            // Fixes "ResolutionScope is null" by rewriting the assembly
            var cleanedDllPath = Path.Combine(Path.GetDirectoryName(assemblyPath), Path.GetFileNameWithoutExtension(assemblyPath) + "-cleaned.dll");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedPath);

            using (var memoryStream = new MemoryStream(File.ReadAllBytes(cleanedDllPath)))
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(memoryStream, new ReaderParameters()
            {
                AssemblyResolver = resolver
            }))
            {
                assemblyDefinition.Write(cleanedDllPath);
            }

            if (doRemapping)
                RemapKnownClasses(managedPath, cleanedDllPath);
            // Do final backup
            if (createBackup)
                BackupExistingAssembly(assemblyPath);
            if (overwriteExisting)
                OverwriteExistingAssembly(assemblyPath, cleanedDllPath);


            Debug.WriteLine($"DeObfuscation complete!");
            Console.WriteLine($"DeObfuscation complete!");

            return true;
        }

        internal static bool Deobfuscate(string exeLocation, bool createBackup = true, bool overwriteExisting = false, bool doRemapping = false)
        {
            var assemblyPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            var managedPath = Path.Combine(assemblyPath, "EscapeFromTarkov_Data", "Managed");
            assemblyPath = Path.Combine(managedPath, "Assembly-CSharp.dll");

            return DeobfuscateAssembly(assemblyPath, managedPath, createBackup, overwriteExisting, doRemapping);
        }

        private static void OverwriteExistingAssembly(string assemblyPath, string cleanedDllPath, bool deleteCleaned = false)
        {
            // Do final copy to Assembly
            File.Copy(cleanedDllPath, assemblyPath, true);
            // Delete -cleaned
            if(deleteCleaned)
                File.Delete(cleanedDllPath);
        }

        private static void BackupExistingAssembly(string assemblyPath)
        {
            if (!File.Exists(assemblyPath + ".backup"))
                File.Copy(assemblyPath, assemblyPath + ".backup", false);
        }

        /// <summary>
        /// A remapping of classes. An idea inspired by Bepis SPT-AssemblyTool to rename known classes from GClass to proper names
        /// </summary>
        /// <param name="managedPath"></param>
        /// <param name="assemblyPath"></param>
        private static void RemapKnownClasses(string managedPath, string assemblyPath)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedPath);

            File.Copy(assemblyPath, assemblyPath + ".backup", true);

            var readerParameters = new ReaderParameters { AssemblyResolver = resolver };
            using (var fsAssembly = new FileStream(assemblyPath, FileMode.Open))
            {
                using (var oldAssembly = AssemblyDefinition.ReadAssembly(fsAssembly, readerParameters))
                {
                    if (oldAssembly != null)
                    {
                        AutoRemapperConfig autoRemapperConfig = JsonConvert.DeserializeObject<AutoRemapperConfig>(File.ReadAllText(App.ApplicationDirectory + "//DeObfus/AutoRemapperConfig.json"));
                        RemapByAutoConfiguration(oldAssembly, autoRemapperConfig);
                        RemapByDefinedConfiguration(oldAssembly, autoRemapperConfig);

                        oldAssembly.Write(assemblyPath.Replace(".dll", "-remapped.dll"));
                    }
                }
            }
            File.Copy(assemblyPath.Replace(".dll", "-remapped.dll"), assemblyPath, true);

        }

        /// <summary>
        /// Attempts to remap all GClass/GInterface/GStruct to a readable name
        /// </summary>
        /// <param name="oldAssembly"></param>
        /// <param name="autoRemapperConfig"></param>
        private static void RemapByAutoConfiguration(AssemblyDefinition oldAssembly, AutoRemapperConfig autoRemapperConfig)
        {
            if (autoRemapperConfig.EnableAttemptToRenameAllClasses)
            {
                var gclasses = oldAssembly.MainModule.GetTypes().Where(x => 
                    x.Name.StartsWith("GClass"));
                var gclassToNameCounts = new Dictionary<string, int>();

                //foreach (var t in oldAssembly.MainModule.GetTypes().Where(x => !x.Name.StartsWith("GClass") && !x.Name.StartsWith("Class")))
                foreach (var t in oldAssembly.MainModule.GetTypes())
                {
                    // --------------------------------------------------------
                    // Renaming by the classes being used as Parameters in methods
                    foreach (var m in t.Methods.Where(x => x.HasParameters
                        && x.Parameters.Any(p =>
                        p.ParameterType.Name.StartsWith("GClass")
                        || p.ParameterType.Name.StartsWith("GStruct")
                        || p.ParameterType.Name.StartsWith("GInterface")

                        )))
                    {
                        foreach (var p in m.Parameters
                            .Where(x => x.ParameterType.Name.StartsWith("GClass")
                            || x.ParameterType.Name.StartsWith("GStruct")
                            || x.ParameterType.Name.StartsWith("GInterface")
                            ))
                        {
                            var n = p.ParameterType.Name
                                .Replace("[]", "")
                                .Replace("`1", "")
                                .Replace("&", "")
                                .Replace(" ", "")
                                + "." + p.Name;
                            if (!gclassToNameCounts.ContainsKey(n))
                                gclassToNameCounts.Add(n, 0);

                            gclassToNameCounts[n]++;
                        }
                    }

                    // --------------------------------------------------------
                    // Renaming by the classes being used as Members/Properties/Fields in other classes
                    foreach (var prop in t.Properties.Where(p =>
                        p.PropertyType.Name.StartsWith("GClass")
                        || p.PropertyType.Name.StartsWith("GStruct")
                        || p.PropertyType.Name.StartsWith("GInterface")
                        ))
                    {
                        if (prop.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.StartsWith("GStruct", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.StartsWith("GInterface", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var n = prop.PropertyType.Name
                            .Replace("[]", "")
                            .Replace("`1", "")
                            .Replace("&", "")
                            .Replace(" ", "")
                            + "." + prop.Name;
                        if (!gclassToNameCounts.ContainsKey(n))
                            gclassToNameCounts.Add(n, 0);

                        gclassToNameCounts[n]++;
                        // this is shit and needs fixing
                        if (gclassToNameCounts[n] > 1)
                        {
                            gclassToNameCounts[n] = 0;
                        }
                    }

                    //    foreach (var prop in t.Fields.Where(p =>
                    //        p.FieldType.Name.StartsWith("GClass")
                    //        || p.FieldType.Name.StartsWith("GStruct")
                    //        || p.FieldType.Name.StartsWith("GInterface")
                    //        ))
                    //    {
                    //        if (prop.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                    //        || prop.Name.StartsWith("GStruct", StringComparison.OrdinalIgnoreCase)
                    //        || prop.Name.StartsWith("GInterface", StringComparison.OrdinalIgnoreCase))
                    //            continue;

                    //        var n = prop.FieldType.Name
                    //            .Replace("[]", "")
                    //            .Replace("`1", "")
                    //            .Replace("&", "")
                    //            .Replace(" ", "")
                    //            + "." + prop.Name;
                    //        if (!gclassToNameCounts.ContainsKey(n))
                    //            gclassToNameCounts.Add(n, 0);

                    //        gclassToNameCounts[n]++;
                    //        // this is shit and needs fixing
                    //        if (gclassToNameCounts[n] > 1)
                    //        {
                    //            gclassToNameCounts[n] = 0;
                    //        }
                    //    }


                }

                var autoRemappedClassCount = 0;
                //var orderedGClassCounts = gclassToNameCounts.Where(x => x.Value > 5 && !x.Key.Contains("`")).OrderByDescending(x => x.Value);
                var orderedGClassCounts = gclassToNameCounts.Where(x => x.Value > 0 && !x.Key.Contains("`")).OrderByDescending(x => x.Value);
                var usedNamesCount = new Dictionary<string, int>();
                var renamedClasses = new Dictionary<string, string>();
                foreach (var g in orderedGClassCounts)
                {
                    var keySplit = g.Key.Split('.');
                    var gclassName = keySplit[0];
                    var gclassNameNew = keySplit[1];
                    if (gclassNameNew.Length <= 3
                        || gclassNameNew.StartsWith("Value", StringComparison.OrdinalIgnoreCase)
                        || gclassNameNew.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)
                        || gclassNameNew.StartsWith("Instance", StringComparison.OrdinalIgnoreCase)
                        || Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(gclassNameNew, StringComparison.OrdinalIgnoreCase))
                        )
                        continue;

                    var t = oldAssembly.MainModule.GetTypes().FirstOrDefault(x => x.Name == gclassName);
                    if (t == null)
                        continue;

                    var newClassName = char.ToUpper(gclassNameNew[0]) + gclassNameNew.Substring(1);
                    if (t.IsInterface)
                        newClassName = "I" + newClassName;

                    if (!usedNamesCount.ContainsKey(newClassName))
                        usedNamesCount.Add(newClassName, 0);

                    usedNamesCount[newClassName]++;

                    if (usedNamesCount[newClassName] > 1)
                        newClassName += usedNamesCount[newClassName];

                    if (!oldAssembly.MainModule.GetTypes().Any(x => x.Name == newClassName)
                        && !Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(newClassName, StringComparison.OrdinalIgnoreCase))
                        )
                    {
                        var oldClassName = t.Name;
                        t.Name = newClassName;
                        renamedClasses.Add(oldClassName, newClassName);
                        Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                    }
                }

                foreach (var t in gclasses.Where(x =>
                    x.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                    && !x.Name.Contains("`")
                    && !x.BaseType.FullName.Contains("GClass")
                    && x.BaseType != null
                    && x.BaseType.FullName != "System.Object"
                    )
                    )
                {
                    if (t.BaseType.IsDefinition)
                    {
                        var oldClassName = t.Name;
                        var oldNamespace = t.Namespace;
                        var newClassName = t.BaseType.Name
                            .Replace("/", "_")
                            + "Sub";
                        if (!usedNamesCount.ContainsKey(newClassName))
                            usedNamesCount.Add(newClassName, 0);

                        usedNamesCount[newClassName]++;

                        if (usedNamesCount[newClassName] > 1)
                            newClassName += usedNamesCount[newClassName];

                        t.Name = newClassName;
                        t.Namespace = t.BaseType.Namespace;
                        renamedClasses.Add(oldClassName, newClassName);
                        Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                    }
                }


                // FIXME: This is breaking something that causes the CustomPlayerLoopSystem to constantly NULL reference. Stupid.
                // Rename all the properties that have {parametername}_0
                //foreach (var renamed in renamedClasses)
                //{
                //    foreach (var t in oldAssembly.MainModule.GetTypes())
                //    {
                //        foreach (var p in t.Properties)
                //        {
                //            p.Name = p.Name.Replace(renamed.Key, renamed.Value, StringComparison.OrdinalIgnoreCase);
                //        }

                //        foreach (var f in t.Fields)
                //        {
                //            f.Name = f.Name.Replace(renamed.Key, renamed.Value.ToLower(), StringComparison.OrdinalIgnoreCase);
                //        }
                //    }
                //}

                Log($"Remapper: Ensuring EFT classes are public");
                foreach (var t in oldAssembly.MainModule.GetTypes())
                {
                    if (t.IsClass && t.IsDefinition && t.BaseType != null && t.BaseType.FullName != "System.Object")
                    {
                        if (!Assembly.GetAssembly(typeof(Attribute))
                            .GetTypes()
                            .Any(x => x.Name.StartsWith(t.Name, StringComparison.OrdinalIgnoreCase)))
                            t.IsPublic = true;
                    }
                }

                autoRemappedClassCount = renamedClasses.Count;
                Log($"Remapper: Auto Remapped {autoRemappedClassCount} classes");
            }
        }

        private static void RemapByDefinedConfiguration(AssemblyDefinition oldAssembly, AutoRemapperConfig autoRemapperConfig)
        {
            foreach (var config in autoRemapperConfig.DefinedRemapping.Where(x => !string.IsNullOrEmpty(x.RenameClassNameTo)))
            {
                try
                {
                    var findTypes
                        = oldAssembly.MainModule.GetTypes()
                        .Where(x
                            =>
                                (config.HasMethods == null || config.HasMethods.Length == 0 || (x.Methods.Count(y => config.HasMethods.Contains(y.Name)) == config.HasMethods.Length))
                                &&
                                (
                                    (config.HasFields == null || config.HasFields.Length == 0 || (x.Fields.Count(y => config.HasFields.Contains(y.Name)) == config.HasFields.Length))
                                    ||
                                    (config.HasFields == null || config.HasFields.Length == 0 || (x.Properties.Count(y => config.HasFields.Contains(y.Name)) == config.HasFields.Length))
                                )
                            ).ToList();
                    if (findTypes.Any())
                    {
                        if (findTypes.Count() > 1)
                        {
                            var numberOfChangedIndexes = 0;
                            for (var index = 0; index < findTypes.Count(); index++)
                            {
                                var newClassName = config.RenameClassNameTo;
                                var t = findTypes[index];
                                var oldClassName = t.Name;
                                if (t.IsInterface && !newClassName.StartsWith("I"))
                                {
                                    newClassName = newClassName.Insert(0, "I");
                                }
                                newClassName = newClassName + (numberOfChangedIndexes > 0 ? numberOfChangedIndexes.ToString() : "");

                                if (!config.OnlyTargetInterface || (t.IsInterface && config.OnlyTargetInterface))
                                {
                                    t.Name = newClassName;
                                    numberOfChangedIndexes++;

                                    Debug.WriteLine($"Remapper: Remapped {oldClassName} to {newClassName}");
                                    Console.WriteLine($"Remapper: Remapped {oldClassName} to {newClassName}");
                                }

                            }
                        }
                        else
                        {
                            var newClassName = config.RenameClassNameTo;
                            var t = findTypes.SingleOrDefault();
                            var oldClassName = t.Name;
                            if (t.IsInterface && !newClassName.StartsWith("I"))
                                newClassName = newClassName.Insert(0, "I");

                            if (!config.OnlyTargetInterface || (t.IsInterface && config.OnlyTargetInterface))
                            {
                                t.Name = newClassName;

                                Debug.WriteLine($"Remapper: Remapped {oldClassName} to {newClassName}");
                                Console.WriteLine($"Remapper: Remapped {oldClassName} to {newClassName}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Remapper: Failed to remap {config.RenameClassNameTo}");
                        Console.WriteLine($"Remapper: Failed to remap {config.RenameClassNameTo}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        public static string[] SplitCamelCase(string input)
        {
            return System.Text.RegularExpressions.Regex
                .Replace(input, "(?<=[a-z])([A-Z])", ",", System.Text.RegularExpressions.RegexOptions.Compiled)
                .Trim().Split(',');
        }

        internal static async Task<bool> DeobfuscateAsync(string exeLocation, bool createBackup = true, bool overwriteExisting = false, bool doRemapping = false)
		{
			return await Task.Run(() => { return Deobfuscate(exeLocation, createBackup, overwriteExisting, doRemapping); });

		}
	}
}
