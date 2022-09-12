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
        public static List<string> Logged = new List<string>();

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
                Logged.Add(text);
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


            Log($"DeObfuscation complete!");

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
            if (!autoRemapperConfig.EnableAutomaticRemapping)
                return;

            var gclasses = oldAssembly.MainModule.GetTypes().Where(x =>
                x.Name.StartsWith("GClass"));
            var gclassToNameCounts = new Dictionary<string, int>();

            //foreach (var t in oldAssembly.MainModule.GetTypes().Where(x => !x.Name.StartsWith("GClass") && !x.Name.StartsWith("Class")))
            foreach (var t in oldAssembly.MainModule.GetTypes())
            {
                // --------------------------------------------------------
                // Renaming by the classes being in methods
                foreach (var m in t.Methods.Where(x => x.HasParameters
                    && x.Parameters.Any(p =>
                    p.ParameterType.Name.StartsWith("GClass")
                    || p.ParameterType.Name.StartsWith("GStruct")
                    || p.ParameterType.Name.StartsWith("GInterface")
                    //|| p.ParameterType.Name.StartsWith("Class")

                    )))
                {
                    // --------------------------------------------------------
                    // Renaming by the classes being used as Parameters in methods
                    foreach (var p in m.Parameters
                        .Where(x =>
                        x.ParameterType.Name.StartsWith("GClass")
                        || x.ParameterType.Name.StartsWith("GStruct")
                        || x.ParameterType.Name.StartsWith("GInterface")
                        //|| x.ParameterType.Name.StartsWith("Class")
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


                //foreach (var m in t.Methods)
                //{ 
                //    // --------------------------------------------------------
                //    // Renaming by the classes by the return typed methods
                //    if (m.Name.StartsWith("Read") && !m.ReturnType.Name.Contains("void", StringComparison.OrdinalIgnoreCase))
                //    {
                //        var n = m.ReturnType.Name
                //            .Replace("[]", "")
                //            .Replace("`1", "")
                //            .Replace("&", "")
                //            .Replace(" ", "")
                //            + "."
                //            + m.Name.Replace("Read", "", StringComparison.OrdinalIgnoreCase);
                //        if (!gclassToNameCounts.ContainsKey(n))
                //            gclassToNameCounts.Add(n, 0);

                //        gclassToNameCounts[n]++;
                //    }
                //}

                // --------------------------------------------------------
                // Renaming by the classes being used as Members/Properties/Fields in other classes
                foreach (var prop in t.Properties.Where(p =>
                    p.PropertyType.Name.StartsWith("GClass")
                    || p.PropertyType.Name.StartsWith("GStruct")
                    || p.PropertyType.Name.StartsWith("GInterface")
                    ))
                {
                    // if the property name includes "gclass" or whatever, then ignore it as its useless to us
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
                    //if (gclassToNameCounts[n] > 1)
                    //{
                    //    gclassToNameCounts[n] = 0;
                    //}
                }

                foreach (var prop in t.Fields.Where(p =>
                    p.FieldType.Name.StartsWith("GClass")
                    || p.FieldType.Name.StartsWith("GStruct")
                    || p.FieldType.Name.StartsWith("GInterface")
                    ))
                {
                    if (prop.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.StartsWith("GStruct", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.StartsWith("GInterface", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.StartsWith("_")
                    || prop.Name.Contains("_")
                    || prop.Name.Contains("/")
                    )
                        continue;

                    //if(prop.Name == "AirplaneDataPacket")
                    //{

                    //}

                    var n = prop.FieldType.Name
                        .Replace("[]", "")
                        .Replace("`1", "")
                        .Replace("&", "")
                        .Replace(" ", "")
                        + "." + prop.Name;
                    if (!gclassToNameCounts.ContainsKey(n))
                        gclassToNameCounts.Add(n, 0);

                    gclassToNameCounts[n]++;
                    //if (gclassToNameCounts[n] > 1)
                    //{
                    //    gclassToNameCounts[n] = 0;
                    //}
                }


            }
            
            var autoRemappedClassCount = 0;
            
            // ----------------------------------------------------------------------------------------
            // Rename classes based on discovery above
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
                    || gclassNameNew.StartsWith("_", StringComparison.OrdinalIgnoreCase)
                    || gclassNameNew.StartsWith("<", StringComparison.OrdinalIgnoreCase)
                    || Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(gclassNameNew, StringComparison.OrdinalIgnoreCase))
                    || oldAssembly.MainModule.GetTypes().Any(x => x.Name.Equals(gclassNameNew, StringComparison.OrdinalIgnoreCase))
                    )
                    continue;

                var t = oldAssembly.MainModule.GetTypes().FirstOrDefault(x => x.Name == gclassName);
                if (t == null)
                    continue;

                // Follow standard naming convention, PascalCase all class names
                var newClassName = char.ToUpper(gclassNameNew[0]) + gclassNameNew.Substring(1);
                
                // Following BSG naming convention, begin Abstract classes names with "Abstract"
                if (t.IsAbstract && !t.IsInterface)
                    newClassName = "Abstract" + newClassName;
                // Follow standard naming convention, Interface names begin with "I"
                else if (t.IsInterface)
                    newClassName = "I" + newClassName;

                if (!usedNamesCount.ContainsKey(newClassName))
                    usedNamesCount.Add(newClassName, 0);

                usedNamesCount[newClassName]++;

                if (usedNamesCount[newClassName] > 1)
                    newClassName += usedNamesCount[newClassName];

                if (!oldAssembly.MainModule.GetTypes().Any(x => x.Name == newClassName)
                    && !Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(newClassName, StringComparison.OrdinalIgnoreCase))
                    && !oldAssembly.MainModule.GetTypes().Any(x => x.Name.Equals(newClassName, StringComparison.OrdinalIgnoreCase))
                    )
                {
                    var oldClassName = t.Name;
                    t.Name = newClassName;
                    renamedClasses.Add(oldClassName, newClassName);
                    Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                }
            }
            // end of renaming based on discovery
            // ---------------------------------------------------------------------------------------

            // ------------------------------------------------
            // Auto rename FirearmController sub classes
            foreach (var t in oldAssembly.MainModule.GetTypes().Where(x 
                => 
                    x.FullName.StartsWith("EFT.Player.FirearmController")
                    && x.Name.StartsWith("GClass")
                
                ))
            {
                t.Name.Replace("GClass", "FirearmController");
            }

            // ------------------------------------------------
            // Auto rename descriptors
            foreach (var t in oldAssembly.MainModule.GetTypes())
            {
                foreach (var m in t.Methods.Where(x=>x.Name.StartsWith("ReadEFT")))
                {
                    if (m.ReturnType.Name.StartsWith("GClass"))
                    {
                        var rT = oldAssembly.MainModule.GetTypes().FirstOrDefault(x => x == m.ReturnType);
                        if (rT != null)
                        {
                            var oldTypeName = rT.Name;
                            rT.Name = m.Name.Replace("ReadEFT", "");
                            Log($"Remapper: Auto Remapped {oldTypeName} to {rT.Name}");

                        }
                    }
                }
            }

            // Testing stuff here.
            // Quick hack to name properties properly in EFT.Player
            foreach(var playerProp in oldAssembly.MainModule.GetTypes().FirstOrDefault(x=>x.FullName == "EFT.Player").Properties)
            {
                if(playerProp.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase))
                {
                    playerProp.Name = playerProp.PropertyType.Name.Replace("Abstract", "");
                }
            }

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

            Log($"Remapper: Setting EFT methods to public");
            foreach (var ctf in autoRemapperConfig.TypesToForceAllPublicMethods)
            {
                var foundTypes = oldAssembly.MainModule.GetTypes()
                    .Where(x => x.Namespace.Contains("EFT", StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.Name.Contains(ctf, StringComparison.OrdinalIgnoreCase));
                foreach (var t in foundTypes)
                {
                    foreach (var m in t.Methods)
                    {
                        if (!m.IsPublic)
                            m.IsPublic = true;
                    }
                }
            }

            Log($"Remapper: Setting EFT fields/properties to public");
            foreach (var ctf in autoRemapperConfig.TypesToForceAllPublicFieldsAndProperties)
            {
                var foundTypes = oldAssembly.MainModule.GetTypes()
                    .Where(x => x.Namespace.Contains("EFT", StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.Name.Contains(ctf, StringComparison.OrdinalIgnoreCase));
                foreach (var t in foundTypes)
                {
                    foreach (var m in t.Fields)
                    {
                        if (!m.IsPublic)
                            m.IsPublic = true;
                    }
                }
            }


            autoRemappedClassCount = renamedClasses.Count;
            Log($"Remapper: Auto Remapped {autoRemappedClassCount} classes");
        }

        private static void RemapByDefinedConfiguration(AssemblyDefinition oldAssembly, AutoRemapperConfig autoRemapperConfig)
        {
            if (!autoRemapperConfig.EnableDefinedRemapping)
                return;

            int countOfDefinedMappingSucceeded = 0;
            int countOfDefinedMappingFailed = 0;

            foreach (var config in autoRemapperConfig.DefinedRemapping.Where(x => !string.IsNullOrEmpty(x.RenameClassNameTo)))
            {
                
                try
                {
                    var findTypes
                        = oldAssembly.MainModule.GetTypes().ToList();
                    // Filter Types by Class Name Matching
                    findTypes = findTypes.Where(
                        x =>
                            (
                                config.ClassName == null || config.ClassName.Length == 0 || (x.FullName.Contains(config.ClassName))
                            )
                        ).ToList();
                    // Filter Types by Methods
                    findTypes = findTypes.Where(x
                            =>
                                (config.HasMethods == null || config.HasMethods.Length == 0
                                    || (x.Methods.Select(y=>y.Name.Split('.')[y.Name.Split('.').Length-1]).Count(y => config.HasMethods.Contains(y)) >= config.HasMethods.Length))

                            ).ToList();
                    // Filter Types by Virtual Methods
                    if (config.HasMethodsVirtual != null && config.HasMethodsVirtual.Length > 0)
                    {
                        findTypes = findTypes.Where(x
                               =>
                                 (   x.Methods.Count(y => y.IsVirtual) > 0
                                    && x.Methods.Where(y => y.IsVirtual).Count(y => config.HasMethodsVirtual.Contains(y.Name)) >= config.HasMethodsVirtual.Length
                                    )
                               ).ToList();
                    }

                    // Filter Types by Events
                    findTypes = findTypes.Where(x
                           =>
                               (config.HasEvents == null || config.HasEvents.Length == 0
                                   || (x.Events.Select(y => y.Name.Split('.')[y.Name.Split('.').Length - 1]).Count(y => config.HasEvents.Contains(y)) >= config.HasEvents.Length))

                           ).ToList();

                    // Filter Types by Field/Properties
                    findTypes = findTypes.Where(
                        x =>
                                (
                                    // fields
                                    (
                                    config.HasFields == null || config.HasFields.Length == 0 
                                    || (!config.HasExactFields && x.Fields.Count(y => config.HasFields.Contains(y.Name)) >= config.HasFields.Length)
                                    || (config.HasExactFields && x.Fields.Count(y => y.IsDefinition && config.HasFields.Contains(y.Name)) == config.HasFields.Length)
                                    )
                                    ||
                                    // properties
                                    (
                                    config.HasFields == null || config.HasFields.Length == 0 
                                    || (!config.HasExactFields && x.Properties.Count(y => config.HasFields.Contains(y.Name)) >= config.HasFields.Length)
                                    || (config.HasExactFields && x.Properties.Count(y => y.IsDefinition && config.HasFields.Contains(y.Name)) == config.HasFields.Length)

                                    )
                                )).ToList();

                    // Filter Types by Class/Interface
                    findTypes = findTypes.Where(
                        x =>
                            (
                                (!config.IsClass.HasValue || (config.IsClass.HasValue && config.IsClass.Value && (x.IsClass && !x.IsEnum && !x.IsInterface)))
                                && (!config.IsInterface.HasValue || (config.IsInterface.HasValue && config.IsInterface.Value && (x.IsInterface && !x.IsEnum && !x.IsClass)))
                            )
                        ).ToList();

                    

                    if (findTypes.Any())
                    {
                        if (findTypes.Count() > 1)
                        {
                            findTypes = findTypes
                                .OrderBy(x => !x.Name.StartsWith("GClass") && !x.Name.StartsWith("GInterface"))
                                .ThenBy(x => x.Name.StartsWith("GInterface"))
                                .ToList();

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

                                newClassName = newClassName + (!t.IsInterface && numberOfChangedIndexes > 0 ? numberOfChangedIndexes.ToString() : "");

                                t.Name = newClassName;
                                if (!t.IsInterface)
                                    numberOfChangedIndexes++;

                                Log($"Remapper: Remapped {oldClassName} to {newClassName}");
                                countOfDefinedMappingSucceeded++;

                            }
                        }
                        else
                        {
                            var newClassName = config.RenameClassNameTo;
                            var t = findTypes.SingleOrDefault();
                            var oldClassName = t.Name;
                            if (t.IsInterface && !newClassName.StartsWith("I"))
                                newClassName = newClassName.Insert(0, "I");

                            t.Name = newClassName;

                            Log($"Remapper: Remapped {oldClassName} to {newClassName}");
                            countOfDefinedMappingSucceeded++;
                        }
                    }
                    else
                    {
                        Log($"Remapper: Failed to remap {config.RenameClassNameTo}");
                        countOfDefinedMappingFailed++;

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            Log($"Defined Remapper: SUCCESS: {countOfDefinedMappingSucceeded}");
            Log($"Defined Remapper: FAILED: {countOfDefinedMappingFailed}");
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
