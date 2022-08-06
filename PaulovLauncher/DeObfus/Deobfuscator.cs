using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/**
 * Original code for this was written by Bepis here - https://dev.sp-tarkov.com/bepis/SPT-AssemblyTool/src/branch/master/SPT-AssemblyTool/Deobfuscator.cs
 */
namespace SIT.Launcher.DeObfus
{
	internal static class Deobfuscator
	{
		internal static bool Deobfuscate(string exeLocation, bool createBackup = true, bool overwriteExisting = false, bool doRemapping = false)
        {
            var assemblyPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            var managedPath = Path.Combine(assemblyPath, "EscapeFromTarkov_Data", "Managed");
            assemblyPath = Path.Combine(managedPath, "Assembly-CSharp.dll");

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
            if(overwriteExisting)
                OverwriteExistingAssembly(assemblyPath, cleanedDllPath);
            

            return true;
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
                        if(autoRemapperConfig.EnableAttemptToRenameAllClasses)
                        {
                            foreach(var t in oldAssembly.MainModule.GetTypes())
                            {
                                if (!t.Name.Contains("GClass"))
                                    continue;

                                {
                                    Dictionary<string, int> countOfNames = new Dictionary<string, int>();
                                    foreach (var f in t.Fields)
                                    {
                                        foreach (var n in SplitCamelCase(f.Name))
                                        {
                                            if (countOfNames.ContainsKey(n))
                                                countOfNames[n]++;
                                            else
                                                countOfNames.Add(n, 1);
                                        }
                                    }
                                    foreach (var f in t.Methods)
                                    {
                                        foreach (var n in SplitCamelCase(f.Name))
                                        {
                                            if (countOfNames.ContainsKey(n))
                                                countOfNames[n]++;
                                            else
                                                countOfNames.Add(n, 1);
                                        }
                                    }
                                    foreach (var f in t.Properties)
                                    {
                                        foreach (var n in SplitCamelCase(f.Name))
                                        {
                                            if (countOfNames.ContainsKey(n))
                                                countOfNames[n]++;
                                            else
                                                countOfNames.Add(n, 1);
                                        }
                                    }

                                    if (countOfNames.Count > 0 && countOfNames.All(x => x.Value == 1))
                                        continue;

                                    var orderedCount = countOfNames.OrderByDescending(x => x.Value);
                                }
                            }
                        }
                        
                        foreach(var config in autoRemapperConfig.DefinedRemapping)
                        {
                            try
                            {
                                var findType
                                    = oldAssembly.MainModule.GetTypes()
                                    .SingleOrDefault(x
                                        => 
                                            (config.HasMethods == null || config.HasMethods.Length == 0 || (x.Methods.Count(y => config.HasMethods.Contains(y.Name)) == config.HasMethods.Length))
                                            &&
                                            (config.HasFields == null || config.HasFields.Length == 0 || (x.Fields.Count(y => config.HasFields.Contains(y.Name)) == config.HasFields.Length))
                                            //&&
                                            //(config.HasFields.Length == 0 || x.Properties.Any(y => config.HasFields.Contains(y.Name)))
                                        );
                                if (findType != null)
                                {
                                    findType.Name = config.RenameClassNameTo;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                        }

                        oldAssembly.Write(assemblyPath.Replace(".dll", "-remapped.dll"));
                    }
                }
            }
            File.Copy(assemblyPath.Replace(".dll", "-remapped.dll"), assemblyPath, true);

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
