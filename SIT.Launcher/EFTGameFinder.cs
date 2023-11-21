using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher
{
    public static class EFTGameChecker
    {
        public static FileInfo FindOfficialGame()
        {
            if (Check(out var filePath))
                return new FileInfo(filePath);

            else return null;
        }

        public static bool Check(out string gameFilePath)
        {
            gameFilePath = null;

            try
            {
                gameFilePath = RegistryManager.GamePathEXE;
                if (!string.IsNullOrEmpty(gameFilePath))
                {
                    if (LC1A(gameFilePath))
                    {
                        if (LC2B(gameFilePath))
                        {
                            return LC3C(gameFilePath);
                        }
                    }
                }

            }
            catch
            {
            }
            return false;
        }

        internal static bool LC1A(string gfp)
        {
            var fiGFP = new FileInfo(gfp);
            return (fiGFP.Exists && fiGFP.Length >= 647 * 1000);
        }

        internal static bool LC2B(string gfp)
        {
            var fiBE = new FileInfo(gfp.Replace(".exe", "_BE.exe"));
            return (fiBE.Exists && fiBE.Length >= 1024000);
        }

        internal static bool LC3C(string gfp)
        {
            var diBattlEye = new DirectoryInfo(gfp.Replace("EscapeFromTarkov.exe", "BattlEye"));
            return (diBattlEye.Exists);
        }

    }
}
