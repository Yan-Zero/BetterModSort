using Verse;

namespace BetterModSort.Tools
{
    public class ModInfo
    {
        public ModContentPack? ModContentPack { get; set; }
        public string PackageId { get; set; }
        public string ModName { get; set; }
        public string? DllName { get; set; }
        public string? DllPath { get; set; }
        /// <summary>
        /// 通用定位上下文：可以是 DLL 堆栈帧 (Namespace.Class.Method)，
        /// 也可以是 XML 节点路径或文件路径等
        /// </summary>
        public string? LocationContext { get; set; }

        public ModInfo()
        {
            PackageId = string.Empty;
            ModName = string.Empty;
        }

        public override string ToString()
        {
            var info = $"[{PackageId}] {ModName}";
            if (!string.IsNullOrEmpty(DllName))
                info += $" - DLL: {DllName}";
            if (!string.IsNullOrEmpty(LocationContext))
                info += $" at {LocationContext}";
            return info;
        }
    }
}
