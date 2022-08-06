using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher.DeObfus
{
    internal class AutoRemapperConfig
    {
        public bool EnableAutomaticRemapping { get; set; }
        public bool EnableAttemptToRenameAllClasses { get; set; }
        public AutoRemapperInfo[] DefinedRemapping { get; set; }
    }
}
