using Mono.Cecil;
using Mono.Cecil.Cil;
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
		internal static bool Deobfuscate(string exeLocation)
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

			// Do final backup
			if(!File.Exists(assemblyPath + ".backup"))
				File.Copy(assemblyPath, assemblyPath + ".backup", false);

			// Do final copy to Assembly
			File.Copy(cleanedDllPath, assemblyPath, true);
			// Delete -cleaned
			File.Delete(cleanedDllPath);

			return true;
		}

		internal static async Task<bool> DeobfuscateAsync(string exeLocation)
		{
			return await Task.Run(() => { return Deobfuscate(exeLocation); });

		}
	}
}
