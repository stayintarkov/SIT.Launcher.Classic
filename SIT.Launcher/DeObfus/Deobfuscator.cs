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
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using de4dot.cui;
using System.Windows.Ink;
using System.Runtime.CompilerServices;

/**
 * Original code for this was written by Bepis here - https://dev.sp-tarkov.com/bepis/SPT-AssemblyTool/src/branch/master/SPT-AssemblyTool/Deobfuscator.cs
 */
namespace SIT.Launcher.DeObfus
{
	internal static class Deobfuscator
	{

        public static HashSet<string> UsedTypesByOtherDlls { get; } = new HashSet<string>();

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

            // If backup file exists, delete .dll and replace with .backup
            if (File.Exists(assemblyPath + ".backup") || File.Exists(cleanedDllPath))
            {
                if(File.Exists(assemblyPath))
                    File.Delete(assemblyPath);

                if (File.Exists(cleanedDllPath))
                    File.Delete(cleanedDllPath);

                File.Move(assemblyPath + ".backup", assemblyPath);
            }

            if (createBackup)
                BackupExistingAssembly(assemblyPath);

            De4DotDeobfuscate(assemblyPath, managedPath, cleanedDllPath, de4dotPath);

            if (doRemapping)
                RemapClasses(managedPath, cleanedDllPath);
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
            psi.CreateNoWindow = false;// true;
            psi.Arguments = $"--un-name \"!^<>[a-z0-9]$&!^<>[a-z0-9]__.*$&![A-Z][A-Z]\\$<>.*$&^[a-zA-Z_<{{$][a-zA-Z_0-9<>{{}}$.`-]*$\" \"{assemblyPath}\" --strtyp delegate --strtok \"{token}\"";

            //var de4dotcuiAssembly = Assembly.LoadFile(de4dotPath.Replace(".exe", ".cui.dll"));
            //var de4dotcuiTypes = de4dotcuiAssembly.GetTypes();
            //var de4dotcuiProgramType = de4dotcuiTypes.FirstOrDefault(x => x.Name == "Program");
            //var de4dotcuiProgramMainMethod = de4dotcuiProgramType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);

            //de4dot.cui.Program
            //try
            //{
            //    Directory.CreateDirectory("bin");
            //    var de4dotresultcode = de4dotcuiProgramMainMethod.Invoke(null,
            //        // method parameters
            //        new object[] { 
            
            //    // args parameter
            //    new string[]
            //    {
            //        "--un-name",
            //        @"!^<>[a-z0-9]$&!^<>[a-z0-9]__.*$&![A-Z][A-Z]\$<>.*$&^[a-zA-Z_<{$][a-zA-Z_0-9<>{}$.`-]*$",
            //        @"H:\EFT_OFFLINE\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll",
            //        "--strtyp",
            //        "delegate",
            //        "--strtok",
            //        "0x6011091"
            //    }

            //    });
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine(ex);
            //}

            Process proc = Process.Start(psi);
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
                Debug.WriteLine(e.Data);
            };
            proc.WaitForExit(new TimeSpan(0,0,30));
            if(proc != null && !proc.HasExited)
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
        private static void RemapClasses(string managedPath, string assemblyPath)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedPath);
            var readerParameters = new ReaderParameters { AssemblyResolver = resolver };


            UsedTypesByOtherDlls.Clear();

            var managedFiles = Directory.GetFiles(managedPath);
            foreach (var managedFile in managedFiles)
            {
                if (managedFile.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (managedFile.Contains("DOTween", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (managedFile.Contains("FilesChecker", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (managedFile.Contains("TextMeshPro", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (managedFile.Contains("UnityEngine.CoreModule", StringComparison.OrdinalIgnoreCase))
                    continue;

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
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            using (var fsAssembly = new FileStream(assemblyPath, FileMode.Open))
            {
                using (var oldAssembly = AssemblyDefinition.ReadAssembly(fsAssembly, readerParameters))
                {
                    if (oldAssembly != null)
                    {
                        foreach(var fI in Directory.GetFiles(App.ApplicationDirectory + "//DeObfus//mappings//", "*.json", SearchOption.AllDirectories).Select(x=> new FileInfo(x)))
                        {
                            if (!fI.Exists)
                                continue;

                            if (fI.Extension != ".json")
                                continue;

                            Log($"-Deobfuscating-Run file--------------------------------------------------------------------------------------");
                            Log($"{fI.Name}");

                            AutoRemapperConfig autoRemapperConfig = JsonConvert.DeserializeObject<AutoRemapperConfig>(File.ReadAllText(fI.FullName));

                            // If the Remapper Config is set to use Auto Configuration. Run these two passes
                            Log("Remapping by Auto Configuration: PASS 1");
                            var renamedClasses = new HashSet<TypeDefinition>();
                            RemapByAutoConfiguration(oldAssembly, autoRemapperConfig, ref renamedClasses, pass: 1);
                            // A second pass finds unmapped GClass that use Interfaces that have been renamed
                            Log("Remapping by Auto Configuration: PASS 2");
                            RemapByAutoConfiguration(oldAssembly, autoRemapperConfig, ref renamedClasses, pass: 2);

                            // Run the defined mapping in the configuration file
                            Log("Remapping by Defined Configuration");
                            RemapByDefinedConfiguration(oldAssembly, autoRemapperConfig);

                            // Add SPT Usec & Bear to the WildSpawnType Enums
                            RemapAddSPTUsecAndBear(oldAssembly, autoRemapperConfig);

                            // Remap the Public Types dependant on mapping
                            RemapPublicTypesMethodsAndFields(oldAssembly, autoRemapperConfig);

                            Log(Environment.NewLine);

                        }

                        RemapSwitchClassesToPublic(oldAssembly);
                        Log(Environment.NewLine);

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
                        foreach(var inte in t.Interfaces)
                        {

                        }
                        t.Resolve();
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
                ForcePublicMethodsForType(assemblyDefinition, ctf);
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

            if (autoRemapperConfig.TypesToConvertConstructorsToPublic != null)
            {
                foreach (var ctf in autoRemapperConfig.TypesToConvertConstructorsToPublic)
                {
                    var foundTypes = assemblyDefinition.MainModule.GetTypes()
                        .Where(x => x.FullName.StartsWith(ctf, StringComparison.OrdinalIgnoreCase) || x.FullName.EndsWith(ctf));
                    foreach (var t in foundTypes)
                    {
                        foreach (var c in t.GetConstructors())
                        {
                            c.IsPublic = true;
                        }
                        t.Resolve();
                    }
                }
            }
        }

        private static void ForcePublicMethodsForType(AssemblyDefinition assemblyDefinition, TypeDefinition t)
        {
            foreach (var m in t.Methods)
            {
                //if (m.Name.StartsWith("set_") || m.CustomAttributes.Any(y => y.GetType() == typeof(CompilerGeneratedAttribute)))
                //{
                //    continue;
                //}

                if (!m.IsPublic)
                    m.IsPublic = true;
            }

            foreach (var nt in t.NestedTypes)
            {
                ForcePublicMethodsForType(assemblyDefinition, nt);
            }

            foreach (var otherT in assemblyDefinition.MainModule.GetTypes())
            {
                if (otherT.BaseType != null && otherT.BaseType.FullName != "System.Object" && otherT.BaseType == t)
                {
                    ForcePublicMethodsForType(assemblyDefinition, otherT);
                }
            }
        }

        private static void ForcePublicMethodsForType(AssemblyDefinition assemblyDefinition, string ctf)
        {
            var foundTypes = assemblyDefinition.MainModule.GetTypes()
                                .Where(x => x.FullName.StartsWith(ctf, StringComparison.OrdinalIgnoreCase) || x.FullName.EndsWith(ctf));
            foreach (var t in foundTypes)
            {
               ForcePublicMethodsForType(assemblyDefinition, t);
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

            long sptUsecValue = 0x29;
            long sptBearValue = 0x2A;

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

        /// <summary>
        /// Finds defined types and attempts to remove "Internal" from them
        /// </summary>
        /// <param name="assembly"></param>
        private static void RemapSwitchClassesToPublic(AssemblyDefinition assembly)
        {
            int countOfPublications = 0;
            Log($"Remapper: Ensuring EFT classes are public");
            var types = assembly
                .MainModule
                .GetTypes()
                .Where(x => 
                x.IsClass 
                && x.IsDefinition 
                //&& x.Namespace.StartsWith("EFT")
                && !x.Name.Contains("<")
                && !x.Name.Contains(">")
                && !x.Name.Contains("Module")
                && !Assembly.GetAssembly(typeof(Attribute))
                    .GetTypes()
                    .Any(y => y.Name.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase))
                );

            var nonPublicTypes = types
                .Where(x => !x.IsNested && x.IsNotPublic).ToList();

            var nonPublicNestedTypes = types
                .Where(x => x.IsNested && !x.IsNestedPublic).ToList();

            //var nonPublicClasses = nonPublicTypes.Where(t =>
            //    t.IsClass
            //        && t.IsDefinition
            //        && t.BaseType != null
            //        //&& (t.BaseType.FullName != "System.Object")
            //        && !Assembly.GetAssembly(typeof(Attribute))
            //            .GetTypes()
            //            .Any(x => x.Name.StartsWith(t.Name, StringComparison.OrdinalIgnoreCase))
            //        ).ToList();

            foreach (var t in nonPublicTypes)
            {
                if (t.IsNested)
                {
                    t.IsNestedPublic = true;
                    if (!t.DeclaringType.IsPublic)
                    {
                        t.DeclaringType.IsPublic = true;
                    }
                }
                else
                {
                    t.IsPublic = true;
                    if (t.BaseType != null)
                    {
                        t.BaseType.Resolve().IsPublic = true;
                    }
                }
                countOfPublications++;
            }
            foreach (var t in nonPublicNestedTypes)
            {
                if (t.IsNested)
                {
                    t.IsNestedPublic = true;
                    //if (!t.DeclaringType.IsPublic)
                    //{
                    //    if(t.DeclaringType.IsNested)
                    //        t.DeclaringType.IsNestedPublic = true;
                    //    else
                    //        t.DeclaringType.IsPublic = true;
                    //}
                }
                countOfPublications++;
            }
            Log($"Remapper: {countOfPublications} EFT classes have been converted to public");
        }

        /// <summary>
        /// Attempts to remap all GClass/GInterface/GStruct to a readable name
        /// </summary>
        /// <param name="assemblyDefinition"></param>
        /// <param name="autoRemapperConfig"></param>
        private static void RemapByAutoConfiguration(
            AssemblyDefinition assemblyDefinition
            , AutoRemapperConfig autoRemapperConfig
            , ref HashSet<TypeDefinition> renamedClasses
            , int pass = 1)
        {
            if (!autoRemapperConfig.EnableAutomaticRemapping.HasValue || !autoRemapperConfig.EnableAutomaticRemapping.Value)
                return;

            Log("Remapping by Auto Configuration");
            Stopwatch stopwatch = Stopwatch.StartNew();

            var allTypes = assemblyDefinition.MainModule.GetTypes()
                .Where(x => !x.Name.Contains("MainModule") && !x.Name.Contains("<M"))
                .ToArray();
            var gclasses = assemblyDefinition.MainModule.GetTypes()
                .Where(x => 
                x.Name.StartsWith("GClass") 
                || x.Name.StartsWith("GStruct") 
                || (x.Name.StartsWith("Class") || x.Name.StartsWith("Class"))
                || x.Name.StartsWith("GInterface"))
                // .Where(x => !x.Name.Contains("`"))
                .OrderBy(x => x.Name)
                .ToArray();
            var gclassToNameCounts = new Dictionary<(string, TypeDefinition), int>();

            if (pass == 1)
            {
                foreach (var t in gclasses)
                {
                    // --------------------------------------------------------
                    // Renaming by the classes being in methods
                    RemapAutoDiscoverAndCountByMethodParameters(ref gclassToNameCounts, t, allTypes);

                    // --------------------------------------------------------
                    // Renaming by the classes being used as Members/Properties/Fields in other classes
                    RemapAutoDiscoverAndCountByProperties(ref gclassToNameCounts, t, allTypes);

                    RemapAutoDiscoverAndCountByNameMethod(ref gclassToNameCounts, t);


                    //RemapAutoDiscoverAndCountByMethodBody(ref gclassToNameCounts, t, allTypes);
                }
            }

            foreach (var t in gclasses)
            {
                RemapAutoDiscoverAndCountByBaseType(ref gclassToNameCounts, t);
                RemapAutoDiscoverAndCountByInterfaces(ref gclassToNameCounts, t);
            }

            var autoRemappedClassCount = 0;

            // ----------------------------------------------------------------------------------------
            // Rename classes based on discovery above
            RenameClassesByCounts(assemblyDefinition, ref gclassToNameCounts, ref renamedClasses);
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
                    var oldClassName = nc.Name;
                    var newClassName = t.Name + "Sub" + indexOfControllerNest++;
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
                    renamedClasses.Add(t);
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
                    if (m.ReturnType.Name.StartsWith("GClass") || m.ReturnType.Name.StartsWith("Descriptor"))
                    {
                        var rT = assemblyDefinition.MainModule.GetTypes().FirstOrDefault(x => x == m.ReturnType);
                        if (rT != null)
                        {
                            var oldTypeName = rT.Name;
                            rT.Name = m.Name.Replace("ReadEFT", "");
                            renamedClasses.Add(rT);
                            Log($"Remapper: Auto Remapped {oldTypeName} to {rT.Name}");
                        }
                    }
                }
            }

            autoRemappedClassCount = renamedClasses.Count;
            Log($"Remapper: Auto Remapped {autoRemappedClassCount} classes in {stopwatch.Elapsed}");
        }

        private static void RenameClassesByCounts
            (
            AssemblyDefinition assemblyDefinition
            , ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts
            , ref HashSet<TypeDefinition> renamedClasses
            )
        {
            var orderedGClassCounts = gclassToNameCounts
            .Where(x => x.Value > 0)
            .Where(x => !x.Key.Item1.Contains("_"))
            .Where(x => !x.Key.Item1.Contains("("))
            .Where(x => !x.Key.Item1.Contains(")"))
            .Where(x => !x.Key.Item1.Contains("<"))
            .Where(x => !x.Key.Item1.Contains(".Value", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Item1.Contains(".Attribute", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Item1.Contains(".Instance", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Item1.Contains(".Default", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Key.Item1.Contains(".Current", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Value)
            .ToArray();

            var usedNamesCount = new Dictionary<string, int>();
            foreach (var g in orderedGClassCounts)
            {
                var keySplit = g.Key.Item1.Split('.');
                var className = keySplit[0];
                var classNameNew = keySplit[1];

#if DEBUG

                if (className.Equals("GClass2779"))
                {

                }
#endif 

                if (classNameNew.Length <= 3)
                    continue;

                if (classNameNew.StartsWith("Value", StringComparison.OrdinalIgnoreCase)
                    || classNameNew.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)
                    || classNameNew.StartsWith("Instance", StringComparison.OrdinalIgnoreCase)
                    || classNameNew.StartsWith("_", StringComparison.OrdinalIgnoreCase)
                    || classNameNew.StartsWith("<", StringComparison.OrdinalIgnoreCase)
                    || Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.StartsWith(classNameNew, StringComparison.OrdinalIgnoreCase))
                    //|| assemblyDefinition.MainModule.GetTypes().Any(x => x.Name.Equals(classNameNew, StringComparison.OrdinalIgnoreCase))
                    )
                    continue;

                var t = assemblyDefinition.MainModule.GetTypes().FirstOrDefault(x => x.Name == className);
                if (t == null)
                    continue;

                // Follow standard naming convention, PascalCase all class names
                var ultimateGoalName = char.ToUpper(classNameNew[0]) + classNameNew.Substring(1);
                // Following BSG naming convention, begin Abstract classes names with "Abstract"
                if (t.IsAbstract && !t.IsInterface)
                    ultimateGoalName = "Abstract" + ultimateGoalName;
                // Follow standard naming convention, Interface names begin with "I"
                else if (t.IsInterface)
                    ultimateGoalName = "I" + ultimateGoalName;

                if (string.IsNullOrEmpty(t.Namespace)
                    && 
                     (
                        assemblyDefinition.MainModule.GetTypes().Count(x => x.Name.Equals(ultimateGoalName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Namespace)) > 0
                        ||
                        renamedClasses.Count(x => x.Name.Equals(ultimateGoalName) && !string.IsNullOrEmpty(x.Namespace)) > 0
                        //|| 
                        //UsedTypesByOtherDlls.Count(x => x.Equals(ultimateGoalName)) > 0
                     )
                    )
                    ultimateGoalName = "G" + ultimateGoalName;

#if DEBUG
                if (ultimateGoalName == "Stamina")
                {

                }

                if (ultimateGoalName == "Health")
                {

                }
#endif
                // If the class is nested in another class
                // Use the Class Name (this will include namespace "." too)
                if (t.FullName.Contains("/"))
                {
                    t.Resolve();
                    if (t.DeclaringType.Properties.Any(x => x.Name.Equals(ultimateGoalName)) || t.DeclaringType.Fields.Any(x => x.Name.Equals(ultimateGoalName)))
                    {
                        ultimateGoalName = t.FullName.Split('/')[0] + ultimateGoalName;
                    }
                }

                // If the new name contains a "." (namespace), then remove the namespace from the new name
                if (ultimateGoalName.Contains("."))
                {
                    var indexOfLastDot = ultimateGoalName.LastIndexOf(".");
                    if (indexOfLastDot != -1)
                    {
                        ultimateGoalName = ultimateGoalName.Substring(indexOfLastDot + 1, ultimateGoalName.Length - indexOfLastDot - 1);
                    }
                }

                ultimateGoalName = ultimateGoalName.Replace(".", "");

                // Do a check. You cannot have two classes with the same name.
                var countOfExisting = assemblyDefinition.MainModule.GetTypes().Count(x => x.Name.Equals(ultimateGoalName, StringComparison.OrdinalIgnoreCase));
                countOfExisting += renamedClasses.Count(x => x.Name.Equals(ultimateGoalName));
                countOfExisting += UsedTypesByOtherDlls.Count(x => x.Equals(ultimateGoalName));
                if (countOfExisting > 0)
                {
                    countOfExisting++;
                    ultimateGoalName += countOfExisting.ToString();
                }

                // Store the old name
                var oldClassName = t.Name;

                if (UsedTypesByOtherDlls.Contains(ultimateGoalName))
                    continue;

                ultimateGoalName = ultimateGoalName.Replace("`1", "");
                ultimateGoalName = ultimateGoalName.Replace("`2", "");
                ultimateGoalName = ultimateGoalName.Replace("`3", "");
                ultimateGoalName = ultimateGoalName.Replace("`4", "");

                if (Assembly.GetAssembly(typeof(Attribute)).GetTypes().Any(x => x.Name.Contains(ultimateGoalName, StringComparison.OrdinalIgnoreCase)))
                    continue;


                // The new class name has already been used, ignore
                if (renamedClasses.Any(x => x.Name.Equals(ultimateGoalName)))
                    continue;

#if DEBUG
                if (ultimateGoalName.Equals("JsonConverter2"))
                {

                }
#endif
                if (renamedClasses.Add(t))
                {
                    Log($"Remapper: Auto Remapped {oldClassName} to {ultimateGoalName}");
                    t.Name = ultimateGoalName;
                }
                else
                {
                    Log($"Remapper (ERROR): Unable to Auto Remap {oldClassName} to {ultimateGoalName}. {ultimateGoalName} has already been used!");
                }

            }

        }

        private static void RemapAutoDiscoverAndCountByProperties(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> allTypes)
        {
            foreach (var other in allTypes.Where(x => x.HasProperties))
            {
                PropertyDefinition[] propertyDefinitions = other.Properties.Where(p =>
                                    p.PropertyType.Name == t.Name
                                    && p.PropertyType.Name.Length > 4
                                    ).ToArray();
               
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
                    if (!gclassToNameCounts.ContainsKey((n, t)))
                        gclassToNameCounts.Add((n, t), 0);

                    gclassToNameCounts[(n, t)]++;
                }
            }

            RemapAutoDiscoverAndCountByFields(ref gclassToNameCounts, t, allTypes);
        }

        private static void RemapAutoDiscoverAndCountByFields(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> allTypes)
        {
            foreach (var other in allTypes.Where(x => x.HasFields))
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
                    if (!gclassToNameCounts.ContainsKey((n, t)))
                        gclassToNameCounts.Add((n, t), 0);

                    gclassToNameCounts[(n, t)]++;
                }
            }
        }

        private static void RemapAutoDiscoverAndCountByNameMethod(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t)
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
                    if (!gclassToNameCounts.ContainsKey((matchingGClassName, t)))
                        gclassToNameCounts.Add((matchingGClassName, t), 0);

                    gclassToNameCounts[(matchingGClassName, t)]++;
                }
            }
            catch { }
        }

        private static void RemapAutoDiscoverAndCountByMethodParameters(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> otherTypes)
        {
            foreach (var other in otherTypes)
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

                    if (method.Name == "SetLightsState")
                    {

                    }

                    foreach (var parameter in method.Parameters
                        .Where(x => x.ParameterType.Name.Replace("[]", "").Replace("`1", "") == t.Name)
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
                        if (!gclassToNameCounts.ContainsKey((n, t)))
                            gclassToNameCounts.Add((n, t), 0);

                        gclassToNameCounts[(n, t)]++;
                    }
                }

            }
        }

//        private static void RemapAutoDiscoverAndCountByMethodBody(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t, IEnumerable<TypeDefinition> otherTypes)
//        {
//            foreach (var other in otherTypes)
//            {
//                if (!other.HasMethods || other.Methods == null)
//                    continue;

//                foreach (var method in other.Methods)
//                {
//                    if (!method.HasBody)
//                        continue;

//                    if (!method.Parameters.Any())
//                        continue;

//                    var methodBody = method.Body;
//                    if (methodBody == null)
//                        continue;

//#if DEBUG
//                    //if (other.Name.Contains("BaseLocalGame", StringComparison.OrdinalIgnoreCase))
//                    //{
//                    //    if(method.Name.Contains("method_4", StringComparison.OrdinalIgnoreCase))
//                    //    {
//                    //        foreach (var instruction in methodBody.Instructions)
//                    //        {
//                    //            Debug.WriteLine($"{instruction.OpCode} \"{instruction.Operand}\"");
//                    //        }
//                    //    }
//                    //}

                    

//#endif


//                    foreach (var parameter in method.Parameters
//                        .Where(x => x.ParameterType.Name.Replace("[]", "").Replace("`1", "") == t.Name)
//                        .Where(x => x.ParameterType.Name.Length > 3)
//                        )
//                    {


//                        var n =
//                        // Key Value is Built like so. KEY.VALUE
//                        parameter.ParameterType.Name
//                        .Replace("[]", "")
//                        .Replace("`1", "")
//                        .Replace("`2", "")
//                        .Replace("`3", "")
//                        .Replace("&", "")
//                        .Replace(" ", "")
//                        + "."
//                        + char.ToUpper(parameter.Name[0]) + parameter.Name.Substring(1)
//                        ;
//                        if (!gclassToNameCounts.ContainsKey((n, t)))
//                            gclassToNameCounts.Add((n, t), 0);

//                        gclassToNameCounts[(n, t)]++;
//                    }
//                }

//            }
//        }

        /// <summary>
        /// This will likely only work on a 2nd pass
        /// </summary>
        /// <param name="gclassToNameCounts"></param>
        /// <param name="t"></param>
        /// <param name="allTypes"></param>
        private static void RemapAutoDiscoverAndCountByBaseType(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t)
        {

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
                if (!gclassToNameCounts.ContainsKey((n, t)))
                    gclassToNameCounts.Add((n, t), 0);

                gclassToNameCounts[(n, t)]++;
        }

        private static void RemapAutoDiscoverAndCountByInterfaces(ref Dictionary<(string, TypeDefinition), int> gclassToNameCounts, TypeDefinition t)
        {
         
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

                    if (!gclassToNameCounts.ContainsKey((n, t)))
                        gclassToNameCounts.Add((n, t), 0);

                    gclassToNameCounts[(n, t)]++;

                }
        }

        private static void RemapByDefinedConfiguration(AssemblyDefinition assembly, AutoRemapperConfig autoRemapperConfig)
        {
            if (!autoRemapperConfig.EnableDefinedRemapping.HasValue || !autoRemapperConfig.EnableDefinedRemapping.Value)
                return;

            int countOfDefinedMappingSucceeded = 0;
            int countOfDefinedMappingFailed = 0;

            foreach (var config in autoRemapperConfig.DefinedRemapping.Where(x => !string.IsNullOrEmpty(x.RenameClassNameTo)))
            {

                try
                {
                    List<TypeDefinition> findTypes = DiscoverTypeByMapping(assembly, config);

                    if (findTypes.Any())
                    {
                        var onlyRemapFirstFoundType = config.OnlyRemapFirstFoundType.HasValue && config.OnlyRemapFirstFoundType.Value;
                        if (findTypes.Count() > 1 && !onlyRemapFirstFoundType)
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
                            var t = findTypes.FirstOrDefault();
                            var oldClassName = t.Name;
                            if (t.IsInterface && !newClassName.StartsWith("I"))
                                newClassName = newClassName.Insert(0, "I");

                            t.Name = newClassName;
                            //t.Namespace = "EFT";

                            Log($"Remapper: Remapped {oldClassName} to {newClassName}");
                            countOfDefinedMappingSucceeded++;
                        }

                        if (config.RemoveAbstract.HasValue && config.RemoveAbstract.Value)
                        {
                            foreach (var type in findTypes)
                            {
                                if (type.IsAbstract)
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

        private static List<TypeDefinition> DiscoverTypeByMapping(AssemblyDefinition assembly, AutoRemapperInfo config)
        {

#if DEBUG
            if(config.RenameClassNameTo == "AirplaneLogicClass")
            {

            }
#endif 
            var findTypes
                                    = assembly
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
                        config.ClassName == null || config.ClassName.Length == 0 || (x.Name.Equals(config.ClassName))
                    )
                ).ToList();

            // Filter Types by Methods
            findTypes = findTypes.Where(x
                    =>
                        (config.HasMethods == null || config.HasMethods.Length == 0
                            || (x.Methods.Where(x => !x.IsStatic).Select(y => y.Name.Split('.')[y.Name.Split('.').Length - 1]).Count(y => config.HasMethods.Contains(y)) >= config.HasMethods.Length))

                    ).ToList();
            // Filter Types by Virtual Methods
            if (config.HasMethodsVirtual != null && config.HasMethodsVirtual.Length > 0)
            {
                findTypes = findTypes.Where(x
                       =>
                         (x.Methods.Count(y => y.IsVirtual) > 0
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
            return findTypes;
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
