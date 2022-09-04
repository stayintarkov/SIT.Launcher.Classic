using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher.DeObfus
{
    internal class AutoRemapperInfo
    {
        public string RenameClassNameTo { get; set; }
        public string ClassName { get; set; }
        public string ClassFullNameContains { get; set; }
        public bool? OnlyTargetInterface { get; set; }
        public bool? IsClass { get; set; }
        public bool? IsInterface { get; set; }
        public bool? IsStruct { get; set; }
        public string[] HasFields { get; set; }
        public string[] HasProperties { get; set; }
        public string[] HasMethods { get; set; }

        public override string ToString()
        {
            return !string.IsNullOrEmpty(RenameClassNameTo) ? RenameClassNameTo : base.ToString();
        }
    }
}
