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
        public bool EnableDefinedRemapping { get; set; }
        public AutoRemapperInfo[] DefinedRemapping { get; set; }

        public bool EnableForceAllTypesPublic { get; set; }
        public string[] DefinedTypesToForcePublic { get; set; }
        public string[] TypesToForceAllPublicMethods { get; set; }
        public string[] TypesToForceAllPublicFieldsAndProperties { get; set; }
    }
}
