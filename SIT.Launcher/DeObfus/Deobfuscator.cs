using Microsoft.VisualBasic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
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
        public static List<string> UsedTypesByOtherDlls { get; } = new List<string>();

        public delegate void LogHandler(string text);
        public static event LogHandler OnLog;
        public static List<string> Logged = new List<string>();

        private static ILogger NestedLogger { get; set; }

        internal static void Log(string text)
        {
            if (NestedLogger != null) 
            {
                NestedLogger.Log(text);
                return;
            }

            if (OnLog != null)
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
            var cleanedDllPath = Path.Combine(Path.GetDirectoryName(assemblyPath), Path.GetFileNameWithoutExtension(assemblyPath) + "-cleaned.dll");
            var de4dotPath = Path.Combine(Path.GetDirectoryName(executablePath), "DeObfus", "de4dot", "de4dot.exe");

            De4DotDeobfuscate(assemblyPath, managedPath, cleanedDllPath, de4dotPath);

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

        private static void De4DotDeobfuscate(string assemblyPath, string managedPath, string cleanedDllPath, string de4dotPath)
        {
            if (File.Exists(cleanedDllPath))
            {
                Log($"Initial Deobfuscation Ignored. Cleaned DLL already exists.");
                return;
            }

            Log($"Initial Deobfuscation. Firing up de4dot.");

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

                Debug.WriteLine($"Deobfuscation token: {token}");
                Log($"Deobfuscation token: {token}");
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = de4dotPath;
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;
            psi.Arguments = $"--un-name \"!^<>[a-z0-9]$&!^<>[a-z0-9]__.*$&![A-Z][A-Z]\\$<>.*$&^[a-zA-Z_<{{$][a-zA-Z_0-9<>{{}}$.`-]*$\" \"{assemblyPath}\" --strtyp delegate --strtok \"{token}\"";

            Process proc = Process.Start(psi);
            proc.WaitForExit(new TimeSpan(0,2,0));
            if(proc != null)
                proc.Kill(true);


            // Final Cleanup
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedPath);

            using (var memoryStream = new MemoryStream(File.ReadAllBytes(cleanedDllPath)))
            {
                using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(memoryStream
                    , new ReaderParameters()
                    {
                        AssemblyResolver = resolver
                    }))
                {
                    assemblyDefinition.Write(cleanedDllPath);
                }
            }
        }

        private static void OverwriteExistingAssembly(string assemblyPath, string cleanedDllPath, bool deleteCleaned = true)
        {
            // Do final copy to Assembly
            File.Copy(cleanedDllPath, assemblyPath, true);
            //// Delete -cleaned
            //if(deleteCleaned)
            //    File.Delete(cleanedDllPath);
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
            var readerParameters = new ReaderParameters { AssemblyResolver = resolver };


            UsedTypesByOtherDlls.Clear();

            var managedFiles = Directory.GetFiles(managedPath).Where(x => !x.Contains("AssemblyCSharp"));
            foreach (var managedFile in managedFiles)
            {
                try
                {
                    using (var fsManagedFile = new FileStream(managedFile, FileMode.Open))
                    {
                        using (var managedFileAssembly = AssemblyDefinition.ReadAssembly(fsManagedFile, readerParameters))
                        {
                            if (managedFileAssembly != null)
                            {
                                foreach (var t in managedFileAssembly.MainModule.Types)
                                {
                                    UsedTypesByOtherDlls.Add(t.Name);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            }

            using (var fsAssembly = new FileStream(assemblyPath, FileMode.Open))
            {
                using (var oldAssembly = AssemblyDefinition.ReadAssembly(fsAssembly, readerParameters))
                {
                    if (oldAssembly != null)
                    {
                        Log($"Deobfuscating: Run file BaseRemapperConfig.json");
                        foreach(var fI in Directory.GetFiles(App.ApplicationDirectory + "//DeObfus//mappings//", "*.json", SearchOption.AllDirectories).Select(x=> new FileInfo(x)))
                        {
                            if (!fI.Exists)
                                continue;

                            if (fI.Extension != ".json")
                                continue;

                            Log($"-Deobfuscating-Run file--------------------------------------------------------------------------------------");
                            Log($"{fI.Name}");

                            AutoRemapperConfig autoRemapperConfig = JsonConvert.DeserializeObject<AutoRemapperConfig>(File.ReadAllText(fI.FullName));
                            RemapByAutoConfiguration(oldAssembly, autoRemapperConfig);
                            if (autoRemapperConfig.EnableAutomaticRemapping.HasValue && autoRemapperConfig.EnableAutomaticRemapping.Value)
                            {
                                var gclasses = oldAssembly.MainModule.GetTypes()
                                .Where(x => x.Name.StartsWith("GClass") || x.Name.StartsWith("GStruct") || x.Name.StartsWith("Class") || x.Name.StartsWith("GInterface"))
                                .OrderBy(x => x.Name)
                                .ToArray();
                                var gclassToNameCounts = new Dictionary<string, int>();
                                foreach (var t in gclasses)
                                {
                                    RemapAutoDiscoverAndCountByBaseType(ref gclassToNameCounts, t);
                                    RemapAutoDiscoverAndCountByInterfaces(ref gclassToNameCounts, t);
                                }
                                RenameClassesByCounts(oldAssembly, gclassToNameCounts);
                            }

                            RemapSwitchClassesToPublic(oldAssembly, autoRemapperConfig);
                            RemapByDefinedConfiguration(oldAssembly, autoRemapperConfig);
                            RemapAddSPTUsecAndBear(oldAssembly, autoRemapperConfig);
                            RemapPublicTypesMethodsAndFields(oldAssembly, autoRemapperConfig);

                            Log(Environment.NewLine);

                        }

                        oldAssembly.Write(assemblyPath.Replace(".dll", "-remapped.dll"));
                    }
                }
            }

            File.Copy(assemblyPath.Replace(".dll", "-remapped.dll"), assemblyPath, true);
            File.Delete(assemblyPath.Replace(".dll", "-remapped.dll"));

        }

        private static void RemapPublicTypesMethodsAndFields(AssemblyDefinition assemblyDefinition, AutoRemapperConfig autoRemapperConfig)
        {
            if (true)
            {

            }

            foreach (var ctf in autoRemapperConfig.DefinedTypesToForcePublic)
            {
                var foundTypes = assemblyDefinition.MainModule.GetTypes()
                  .Where(x => x.FullName.StartsWith(ctf) || x.FullName.EndsWith(ctf));

                foreach (var t in foundTypes.Where(x => x.IsClass))
                {
                    if (t.IsNested)
                    {
                        t.IsNestedPublic = true;
                        if(!t.DeclaringType.IsPublic)
                        {
                            t.DeclaringType.IsPublic = true;
                        }
                    }
                    else
                    {
                        t.IsPublic = true;
                        t.BaseType.Resolve().IsPublic = true;
                    }

                }
            }

            foreach (var ctf in autoRemapperConfig.TypesToForceAllPublicMethods)
            {
                var foundTypes = assemblyDefinition.MainModule.GetTypes()
                    .Where(x => x.FullName.StartsWith(ctf, StringComparison.OrdinalIgnoreCase) || x.FullName.EndsWith(ctf));
                foreach (var t in foundTypes)
                {
                    foreach (var m in t.Methods)
                    {
                        if (!m.IsPublic)
                            m.IsPublic = true;
                    }

                    foreach (var nt in t.NestedTypes)
                    {
                        foreach (var m in nt.Methods)
                        {
                            if (!m.IsPublic)
                                m.IsPublic = true;
                        }
                    }
                }
            }

            foreach (var ctf in autoRemapperConfig.TypesToForceAllPublicFieldsAndProperties)
            {
                var foundTypes = assemblyDefinition.MainModule.GetTypes()
                    .Where(x => x.FullName.StartsWith(ctf, StringComparison.OrdinalIgnoreCase) || x.FullName.EndsWith(ctf));
                foreach (var t in foundTypes)
                {
                    foreach (var field in t.Fields)
                    {
                        if (!field.IsDefinition)
                            continue;

                        if (!field.IsPublic)
                            field.IsPublic = true;
                    }

                    foreach (var property in t.Properties)
                    {
                        if (!property.IsDefinition)
                            continue;
                    }
                }
            }
        }


        /// <summary>
        /// This function adds sptUsec and sptBear to the EFT.WildSpawnType enum
        /// </summary>
        /// <param name="assembly"></param>
        /// <param name="config"></param>
        private static void RemapAddSPTUsecAndBear(AssemblyDefinition assembly, AutoRemapperConfig config)
        {
            if (!config.EnableAddSPTUsecBearToDll.HasValue || !config.EnableAddSPTUsecBearToDll.Value)
                return;

            long sptUsecValue = 99; 
            long sptBearValue = 100;

            var botEnums = assembly.MainModule.GetType("EFT.WildSpawnType");

            if (botEnums.Fields.Any(x => x.Name == "sptUsec"))
                return;

            var sptUsec = new FieldDefinition("sptUsec",
                    Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault,
                    botEnums)
            { Constant = sptUsecValue };

            var sptBear = new FieldDefinition("sptBear",
                    Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault,
                    botEnums)
            { Constant = sptBearValue };

            botEnums.Fields.Add(sptUsec);
            botEnums.Fields.Add(sptBear);

            Log($"Remapper: Added SPTUsec and SPTBear to EFT.WildSpawnType");
        }

        private static void RemapSwitchClassesToPublic(AssemblyDefinition assembly, AutoRemapperConfig autoRemapperConfig)
        {
            int countOfPublications = 0;
            Log($"Remapper: Ensuring EFT classes are public");
            var nonPublicTypes = assembly
                .MainModule
                .GetTypes()
                .Where(x => x.IsNotPublic).ToList();

            var nonPublicClasses = nonPublicTypes.Where(t =>
                t.IsClass
                    && t.IsDefinition
                    && t.BaseType != null
                    //&& (t.BaseType.FullName != "System.Object")
                    && !Assembly.GetAssembly(typeof(Attribute))
                        .GetTypes()
                        .Any(x => x.Name.StartsWith(t.Name, StringComparison.OrdinalIgnoreCase))
                    ).ToList();

            foreach (var t in nonPublicClasses)
            {
                t.IsPublic = true;
                countOfPublications++;
            }
            Log($"Remapper: {countOfPublications} EFT classes have been converted to public");
        }

        /// <summary>
        /// Attempts to remap all GClass/GInterface/GStruct to a readable name
        /// </summary>
        /// <param name="assemblyDefinition"></param>
        /// <param name="autoRemapperConfig"></param>
        private static void RemapByAutoConfiguration(AssemblyDefinition assemblyDefinition, AutoRemapperConfig autoRemapperConfig)
        {
            if (!autoRemapperConfig.EnableAutomaticRemapping.HasValue || !autoRemapperConfig.EnableAutomaticRemapping.Value)
                return;

            var allTypes = assemblyDefinition.MainModule.GetTypes();
            var gclasses = assemblyDefinition.MainModule.GetTypes()
                .Where(x => x.Name.StartsWith("GClass") || x.Name.StartsWith("GStruct") || x.Name.StartsWith("Class") || x.Name.StartsWith("GInterface"))
                // .Where(x => !x.Name.Contains("`"))
                .OrderBy(x => x.Name)
                .ToArray();
            var gclassToNameCounts = new Dictionary<string, int>();

            foreach (var t in gclasses)
            {

                // --------------------------------------------------------
                // Renaming by the classes being in methods
                RemapAutoDiscoverAndCountByMethodParameters(ref gclassToNameCounts, t, allTypes);

                // --------------------------------------------------------
                // Renaming by the classes being used as Members/Properties/Fields in other classes
                RemapAutoDiscoverAndCountByProperties(ref gclassToNameCounts, t, allTypes);

                RemapAutoDiscoverAndCountByNameMethod(ref gclassToNameCounts, t);
            }

            foreach (var t in gclasses)
            {
                RemapAutoDiscoverAndCountByBaseType(ref gclassToNameCounts, t);
                RemapAutoDiscoverAndCountByInterfaces(ref gclassToNameCounts, t);
            }

            var autoRemappedClassCount = 0;

            // ----------------------------------------------------------------------------------------
            // Rename classes based on discovery above
            Dictionary<string, string> renamedClasses = RenameClassesByCounts(assemblyDefinition, gclassToNameCounts);
            // end of renaming based on discovery
            // ---------------------------------------------------------------------------------------

            foreach (var t in assemblyDefinition.MainModule.GetTypes().Where(x
                =>
                    x.FullName.Contains("/")

                ))
            {
                var indexOfControllerNest = 0;
                foreach (var nc in t.NestedTypes.Where(x => x != null && x.Name.StartsWith("Class")))
                {
                    //var oldClassName = nc.Name;
                    //var newClassName = t.Name + "Sub" + indexOfControllerNest++;
                    //nc.Name = newClassName;
                    //renamedClasses.Add(oldClassName, newClassName);
                    //Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                    //nc.Name = nc.Name.Replace("Class", newStartOfName).Substring(0, newStartOfName.Length) + indexOfControllerNest.ToString();
                }
            }


            // ------------------------------------------------
            // Auto rename FirearmController sub classes
            foreach (var t in assemblyDefinition.MainModule.GetTypes().Where(x
                =>
                    x.FullName == "EFT.Player/FirearmController"

                ))
            {
                var indexOfControllerNest = 0;
                foreach (var nc in t.NestedTypes.Where(x => x != null && x.Name.StartsWith("Class")))
                {
                    var oldClassName = nc.Name;
                    var newClassName = t.Name + "Sub" + indexOfControllerNest++;
                    nc.Name = newClassName;
                    renamedClasses.Add(oldClassName, newClassName);
                    Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                    //nc.Name = nc.Name.Replace("Class", newStartOfName).Substring(0, newStartOfName.Length) + indexOfControllerNest.ToString();
                }
            }

            // ------------------------------------------------
            // Auto rename GrenadeController sub classes
            foreach (var t in assemblyDefinition.MainModule.GetTypes().Where(x
                =>
                    x.FullName == "EFT.Player/GrenadeController"
                ))
            {
                var indexOfGrenadeControllerNest = 0;
                foreach (var nc in t.NestedTypes.Where(x => x != null))
                {
                    indexOfGrenadeControllerNest++;
                    nc.Name = nc.Name.Replace("Class", "GrenadeControllerSub").Substring(0, "GrenadeControllerSub".Length) + indexOfGrenadeControllerNest.ToString();
                }
            }

            // ------------------------------------------------
            // Auto rename descriptors
            foreach (var t in assemblyDefinition.MainModule.GetTypes())
            {
                foreach (var m in t.Methods.Where(x => x.Name.StartsWith("ReadEFT")))
                {
                    if (m.ReturnType.Name.StartsWith("GClass"))
                    {
                        var rT = assemblyDefinition.MainModule.GetTypes().FirstOrDefault(x => x == m.ReturnType);
                        if (rT != null)
                        {
                            var oldTypeName = rT.Name;
                            rT.Name = m.Name.Replace("ReadEFT", "");
                            renamedClasses.Add(oldTypeName, rT.Name);
                            Log($"Remapper: Auto Remapped {oldTypeName} to {rT.Name}");
                        }
                    }
                }
            }



            autoRemappedClassCount = renamedClasses.Count;
            Log($"Remapper: Auto Remapped {autoRemappedClassCount} classes");
        }

        private static Dictionary<string, string> RenameClassesByCounts(AssemblyDefinition assemblyDefinition, Dictionary<string, int> gclassToNameCounts)
        {
            var orderedGClassCounts = gclassToNameCounts
            .Where(x => x.Value > 0)
            // .Where(x => !x.Key.Contains("`"))
            .Where(x => !x.Key.Contains("_"))
            .Where(x => !x.Key.Contains("("))
            .Where(x => !x.Key.Contains(")"))
            .Where(x => !x.Key.Contains("<"))
            .Where(x => !x.Key.Contains(".Value", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains(".Attribute", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains(".Instance", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains(".Default", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Contains(".Current", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Value);

#if DEBUG

            if (gclassToNameCounts.Any(z => z.Key.Contains("GClass2103")))
            {

            }
#endif

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
                    || assemblyDefinition.MainModule.GetTypes().Any(x => x.Name.Equals(gclassNameNew, StringComparison.OrdinalIgnoreCase))
                    )
                    continue;

                var t = assemblyDefinition.MainModule.GetTypes().FirstOrDefault(x => x.Name == gclassName);
                if (t == null)
                    continue;

                // Store the old name
                var oldClassName = t.Name;

                // Follow standard naming convention, PascalCase all class names
                var newClassName = char.ToUpper(gclassNameNew[0]) + gclassNameNew.Substring(1);

                // The class has already been renamed, ignore
                if (renamedClasses.ContainsKey(oldClassName))
                    continue;

                // The new class name has already been used, ignore
                if (renamedClasses.Values.Contains(newClassName))
                    continue;

                if (UsedTypesByOtherDlls.Contains(newClassName))
                    continue;

                // Following BSG naming convention, begin Abstract classes names with "Abstract"
                if (t.IsAbstract && !t.IsInterface)
                    newClassName = "Abstract" + newClassName;
                // Follow standard naming convention, Interface names begin with "I"
                else if (t.IsInterface)
                    newClassName = "I" + newClassName;

                newClassName = newClassName.Replace("`1", "");
                newClassName = newClassName.Replace("`2", "");
                newClassName = newClassName.Replace("`3", "");

                if (!usedNamesCount.ContainsKey(newClassName))
                    usedNamesCount.Add(newClassName, 0);

                usedNamesCount[newClassName]++;

                if (usedNamesCount[newClassName] > 1)
                    newClassName += usedNamesCount[newClassName];

                if (!assemblyDefinition.MainModule.GetTypes().Any(x => x.Name == newClassName)
                    && !Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(newClassName, StringComparison.OrdinalIgnoreCase))
                    && !assemblyDefinition.MainModule.GetTypes().Any(x => x.Name.Equals(newClassName, StringComparison.OrdinalIgnoreCase))
                    )
                {


                    //var oldType = (TypeDefinition)t as TypeDefinition;
                    //var oldTypeStub = CreateStubOfOldType(oldType);
                    //oldTypeStub.BaseType = t;
                    //oldTypeStub.IsSealed = true;
                    t.Name = newClassName;
                    //t.IsSealed = false;
                    //if(!assemblyDefinition.MainModule.Types.Any(x=>x.FullName == oldTypeStub.FullName))
                    //    assemblyDefinition.MainModule.Types.Add(oldTypeStub);

                    renamedClasses.Add(oldClassName, newClassName);
                    Log($"Remapper: Auto Remapped {oldClassName} to {newClassName}");
                }
            }

            return renamedClasses;
        }

        private static void RemapAutoDiscoverAndCountByProperties(ref Dictionary<string, int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> allTypes)
        {
            foreach (var other in allTypes)
            {

                if (!other.HasFields && !other.HasProperties)
                    continue;

    #if DEBUG
                if (other.FullName.Contains("EFT.Player"))
                {

                }
    #endif

                PropertyDefinition[] propertyDefinitions = null;
                try
                {
                    propertyDefinitions = other.Properties.Where(p =>
                                    p.PropertyType.Name == t.Name
                                    && p.PropertyType.Name.Length > 4
                                    // p.PropertyType.Name.StartsWith("GClass")
                                    // || p.PropertyType.Name.StartsWith("GStruct")
                                    // || p.PropertyType.Name.StartsWith("GInterface")
                                    // || p.PropertyType.Name.StartsWith("Class")
                                    ).ToArray();
                }
                catch (Exception)
                {
                    return;
                }

                if (propertyDefinitions == null)
                    return;

                foreach (var prop in propertyDefinitions)
                {
                    // if the property name includes "gclass" or whatever, then ignore it as its useless to us
                    if (prop.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.StartsWith("GStruct", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.StartsWith("GInterface", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.StartsWith("Class", StringComparison.OrdinalIgnoreCase)
                        )
                        continue;

                    var n = prop.PropertyType.Name
                        .Replace("[]", "")
                        .Replace("`1", "")
                        .Replace("`2", "")
                        .Replace("`3", "")
                        .Replace("&", "")
                        .Replace(" ", "")
                        + "." + char.ToUpper(prop.Name[0]) + prop.Name.Substring(1)
                        ;
                    if (!gclassToNameCounts.ContainsKey(n))
                        gclassToNameCounts.Add(n, 0);

                    gclassToNameCounts[n]++;
                }

                try
                {

                    foreach (var prop in other.Fields.Where(p =>
                                        p.FieldType.Name == t.Name
                                        && p.FieldType.Name.Length > 4

                                        ))
                    {
                        // if the property name includes "gclass" or whatever, then ignore it as its useless to us
                        if (prop.Name.StartsWith("GClass", StringComparison.OrdinalIgnoreCase)
                            || prop.Name.StartsWith("GStruct", StringComparison.OrdinalIgnoreCase)
                            || prop.Name.StartsWith("GInterface", StringComparison.OrdinalIgnoreCase)
                            || prop.Name.StartsWith("Class", StringComparison.OrdinalIgnoreCase)
                            )
                            continue;

                        var n = prop.FieldType.Name
                            .Replace("[]", "")
                            .Replace("`1", "")
                            .Replace("`2", "")
                            .Replace("`3", "")
                            .Replace("&", "")
                            .Replace(" ", "")
                            + "." + char.ToUpper(prop.Name[0]) + prop.Name.Substring(1)
                            ;
                        if (!gclassToNameCounts.ContainsKey(n))
                            gclassToNameCounts.Add(n, 0);

                        gclassToNameCounts[n]++;
                    }
                }
                catch
                {

                }
            }
        }

        private static void RemapAutoDiscoverAndCountByNameMethod(ref Dictionary<string, int> gclassToNameCounts, TypeDefinition t)
        {
            try
            {
                var nameMethod = t.Methods.FirstOrDefault(x => x.Name == "Name");
                if (nameMethod == null)
                    return;

                if (nameMethod.HasBody)
                {
                    if (!nameMethod.Body.Instructions.Any())
                        return;

                    var instructionString = nameMethod.Body.Instructions[0];
                    if (instructionString == null)
                        return;

                    if (instructionString.OpCode.Name != "ldstr")
                        return;

                    if (string.IsNullOrEmpty(instructionString.Operand.ToString()))
                        return;

                    var matchingGClassName = t.Name + "." + instructionString.Operand.ToString().Replace(" ", "").Replace("&", "");
                    if (!gclassToNameCounts.ContainsKey(matchingGClassName))
                        gclassToNameCounts.Add(matchingGClassName, 0);

                    gclassToNameCounts[matchingGClassName]++;
                }
            }
            catch { }
        }

        private static void RemapAutoDiscoverAndCountByMethodParameters(ref Dictionary<string, int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> otherTypes)
        {
            try
            {
#if DEBUG
                if(t.Name == "GStruct143")
                {

                }
#endif 


                foreach(var other in otherTypes)
                {
                    if (!other.HasMethods || other.Methods == null)
                        continue;

                    if (other.FullName.Contains("FirearmController")) 
                    { 

                    }

                    foreach (var method in other.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        if (!method.Parameters.Any())
                            continue;

                        if(method.Name == "SetLightsState")
                        {

                        }

                        foreach (var parameter in method.Parameters
                            .Where(x => x.ParameterType.Name.Replace("[]","").Replace("`1", "") == t.Name)
                            .Where(x => x.ParameterType.Name.Length > 3)
                            )
                        {
                            

                            var n = 
                            // Key Value is Built like so. KEY.VALUE
                            parameter.ParameterType.Name
                            .Replace("[]", "")
                            .Replace("`1", "")
                            .Replace("`2", "")
                            .Replace("`3", "")
                            .Replace("&", "")
                            .Replace(" ", "")
                            + "." 
                            + char.ToUpper(parameter.Name[0]) + parameter.Name.Substring(1)
                            ;
                            if (!gclassToNameCounts.ContainsKey(n))
                                gclassToNameCounts.Add(n, 0);

                            gclassToNameCounts[n]++;
                        }
                    }

                }
            }
            catch { }
        }

        /// <summary>
        /// This will likely only work on a 2nd pass
        /// </summary>
        /// <param name="gclassToNameCounts"></param>
        /// <param name="t"></param>
        /// <param name="allTypes"></param>
        private static void RemapAutoDiscoverAndCountByBaseType(ref Dictionary<string, int> gclassToNameCounts, TypeDefinition t)
        {
            try
            {

#if DEBUG
    if(t.Name.Contains("GClass2103"))
                {

                }
#endif

                if (t.BaseType == null)
                    return;
    
                if(t.BaseType.Name.Contains("GClass") 
                    || t.BaseType.Name.Contains("GStruct") 
                    || t.BaseType.Name.Contains("GInterface") 
                    || t.BaseType.Name.Contains("Class")
                    || t.BaseType.Name == "Object"
                    )
                    return;
                
                var n = 
                // Key Value is Built like so. KEY.VALUE
                t.Name
                .Replace("[]", "")
                .Replace("`1", "")
                .Replace("`2", "")
                .Replace("`3", "")
                .Replace("&", "")
                .Replace(" ", "")
                + "." 
                
                + char.ToUpper(t.BaseType.Name[0]) + t.BaseType.Name.Substring(1)

                // + (t.BaseType.Name.Contains("`1") ? "`1" : "") // cater for `1
                // + (t.BaseType.Name.Contains("`2") ? "`2" : "") // cater for `2
                ;
                if (!gclassToNameCounts.ContainsKey(n))
                    gclassToNameCounts.Add(n, 0);

                gclassToNameCounts[n]++;
                 
            }
            catch { }
        }

        private static void RemapAutoDiscoverAndCountByInterfaces(ref Dictionary<string, int> gclassToNameCounts, TypeDefinition t)
        {
            try
            {

#if DEBUG
                if (t.Name.Contains("GClass2103"))
                {

                }
#endif
                if (t.Interfaces == null)
                    return;

                foreach (var interf in t.Interfaces) {

                    if (interf.InterfaceType.Name.Contains("GClass")
                        || interf.InterfaceType.Name.Contains("GStruct") 
                        || interf.InterfaceType.Name.Contains("GInterface") 
                        || interf.InterfaceType.Name.Contains("Class")
                        || interf.InterfaceType.Name.Contains("Disposable")
                        || interf.InterfaceType.Name.Contains("Enumerator")
                        || interf.InterfaceType.Name.Contains("Comparer")
                        || interf.InterfaceType.Name.Contains("Enumerable")
                        || interf.InterfaceType.Name.Contains("Interface")
                        || interf.InterfaceType.Name.Contains("Equatable")
                        || interf.InterfaceType.Name.Contains("Exchangeable")
                        ) 
                        continue;

                    var n =
                    // Key Value is Built like so. KEY.VALUE
                    t.Name
                    .Replace("[]", "")
                    .Replace("`1", "")
                    .Replace("`2", "")
                    .Replace("`3", "")
                    .Replace("&", "")
                    .Replace(" ", "")
                    + "."

                    + char.ToUpper(interf.InterfaceType.Name[1]) + interf.InterfaceType.Name.Substring(2)
                    ;
                    if (!gclassToNameCounts.ContainsKey(n))
                        gclassToNameCounts.Add(n, 0);

                    gclassToNameCounts[n]++;

                }
            }
            catch { }
        }

        private static void RemapByDefinedConfiguration(AssemblyDefinition oldAssembly, AutoRemapperConfig autoRemapperConfig)
        {
            if (!autoRemapperConfig.EnableDefinedRemapping.HasValue || !autoRemapperConfig.EnableDefinedRemapping.Value)
                return;

            int countOfDefinedMappingSucceeded = 0;
            int countOfDefinedMappingFailed = 0;

            foreach (var config in autoRemapperConfig.DefinedRemapping.Where(x => !string.IsNullOrEmpty(x.RenameClassNameTo)))
            {

#if DEBUG

if(config.IsStruct != null) 
{

}

#endif

                try
                {
                    var findTypes
                        = oldAssembly
                        .MainModule
                        .GetTypes()
                        .OrderBy(x => x.Name)
                        .Where(x => !x.Namespace.StartsWith("System"))
                        .ToList();

                    findTypes = findTypes.Where(
                       x =>
                           (
                               !config.MustBeGClass.HasValue
                               || (config.MustBeGClass.Value && x.Name.StartsWith("GClass"))
                           )
                       ).ToList();

                    findTypes = findTypes.Where(
                       x =>
                           (
                               string.IsNullOrEmpty(config.IsNestedInClass)
                               || (!string.IsNullOrEmpty(config.IsNestedInClass) && x.FullName.Contains(config.IsNestedInClass + "+", StringComparison.OrdinalIgnoreCase))
                               || (!string.IsNullOrEmpty(config.IsNestedInClass) && x.FullName.Contains(config.IsNestedInClass + ".", StringComparison.OrdinalIgnoreCase))
                               || (!string.IsNullOrEmpty(config.IsNestedInClass) && x.FullName.Contains(config.IsNestedInClass + "/", StringComparison.OrdinalIgnoreCase))
                           )
                       ).ToList();


                    // Filter Types by Inherits Class
                    findTypes = findTypes.Where(
                        x =>
                            (
                                config.InheritsClass == null || config.InheritsClass.Length == 0
                                || (x.BaseType != null && x.BaseType.Name == config.InheritsClass)
                            )
                        ).ToList();

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
                                    || (x.Methods.Where(x => !x.IsStatic).Select(y=>y.Name.Split('.')[y.Name.Split('.').Length-1]).Count(y => config.HasMethods.Contains(y)) >= config.HasMethods.Length))

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
                    // Filter Types by Static Methods
                    findTypes = findTypes.Where(x
                            =>
                                (config.HasMethodsStatic == null || config.HasMethodsStatic.Length == 0
                                    || (x.Methods.Where(x => x.IsStatic).Select(y => y.Name.Split('.')[y.Name.Split('.').Length - 1]).Count(y => config.HasMethodsStatic.Contains(y)) >= config.HasMethodsStatic.Length))

                            ).ToList();

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

                    // Filter Types by Class
                    findTypes = findTypes.Where(
                        x =>
                            (
                                (!config.IsClass.HasValue || (config.IsClass.HasValue && config.IsClass.Value && ((x.IsClass || x.IsAbstract) && !x.IsEnum && !x.IsInterface)))
                            )
                        ).ToList();

                    // Filter Types by Interface
                    findTypes = findTypes.Where(
                       x =>
                           (
                                (!config.IsInterface.HasValue || (config.IsInterface.HasValue && config.IsInterface.Value && (x.IsInterface && !x.IsEnum && !x.IsClass)))
                           )
                       ).ToList();

                    findTypes = findTypes.Where(
                   x =>
                       (
                            (!config.IsStruct.HasValue || (config.IsStruct.HasValue && config.IsStruct.Value && (x.IsValueType)))
                       )
                   ).ToList();

                    // Filter Types by Constructor
                    if (config.HasConstructorArgs != null)
                        findTypes = findTypes.Where(t => t.Methods.Any(x => x.IsConstructor && x.Parameters.Count == config.HasConstructorArgs.Length)).ToList();
                    //findTypes = findTypes.Where(x
                    //        =>
                    //            (config.HasConstructorArgs == null || config.HasConstructorArgs.Length == 0
                    //                || (x.Methods.Where(x => x.IsConstructor).Where(y => y.Parameters.Any(z => config.HasConstructorArgs.Contains(z.Name))).Count() >= config.HasConstructorArgs.Length))

                    //        ).ToList();


                    if (findTypes.Any())
                    {
                        var onlyRemapFirstFoundType = config.OnlyRemapFirstFoundType.HasValue && config.OnlyRemapFirstFoundType.Value;
                        if (findTypes.Count() > 1 && !onlyRemapFirstFoundType)
                        {
                            findTypes = findTypes
                                .OrderBy(x => !x.Name.StartsWith("GClass") && !x.Name.StartsWith("GInterface"))
                                .ThenBy(x => x.Name.StartsWith("GInterface"))
                                .ToList();

//#if DEBUG
//                            if (findTypes.Any(x => x.BaseType != null && x.BaseType.Name == "ABindableState"))
//                            {


//                            }
//#endif

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
                            var t = findTypes.FirstOrDefault();
                            var oldClassName = t.Name;
                            if (t.IsInterface && !newClassName.StartsWith("I"))
                                newClassName = newClassName.Insert(0, "I");

                            t.Name = newClassName;
                            //t.Namespace = "EFT";

                            Log($"Remapper: Remapped {oldClassName} to {newClassName}");
                            countOfDefinedMappingSucceeded++;
                        }

                        if(config.RemoveAbstract.HasValue && config.RemoveAbstract.Value) 
                        {
                            foreach (var type in findTypes) 
                            {
                                if(type.IsAbstract)
                                {
                                    type.IsAbstract = false;
                                }
                            }
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

        public static TypeDefinition CreateStubOfOldType(TypeDefinition oldType)
        {
            var stubTypeDefinition = new TypeDefinition(oldType.Namespace, oldType.Name, oldType.Attributes);
            foreach (var cons in oldType.GetConstructors()) 
            {
                MethodDefinition methodDefinition = new MethodDefinition(cons.Name, cons.Attributes, cons.ReturnType);
                foreach (var parameters in cons.Parameters)
                {
                    methodDefinition.Parameters.Add(parameters);
                }
                methodDefinition.Body = cons.Body;
                methodDefinition.CallingConvention = cons.CallingConvention;
                foreach (var securityDeclaration in cons.SecurityDeclarations)
                {
                    methodDefinition.SecurityDeclarations.Add(securityDeclaration);
                }
                stubTypeDefinition.Methods.Add(methodDefinition);
            }
            //stubTypeDefinition.Methods.Add(new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public, null));
            return stubTypeDefinition;
        }

        public static string[] SplitCamelCase(string input)
        {
            return System.Text.RegularExpressions.Regex
                .Replace(input, "(?<=[a-z])([A-Z])", ",", System.Text.RegularExpressions.RegexOptions.Compiled)
                .Trim().Split(',');
        }

        internal static async Task<bool> DeobfuscateAsync(string exeLocation, bool createBackup = true, bool overwriteExisting = false, bool doRemapping = false, ILogger logger = null)
		{
            NestedLogger = logger;
			return await Task.Run(() => { return Deobfuscate(exeLocation, createBackup, overwriteExisting, doRemapping); });
		}
	}
}
