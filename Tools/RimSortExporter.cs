using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;
using RimWorld;

namespace BetterModSort.Tools
{
    /// <summary>
    /// 将当前激活的 MOD 列表导出为 RimSort 支持的 ModsConfigData XML 格式。
    /// </summary>
    public static class RimSortExporter
    {
        private static readonly string ExportPath =
            Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort", "RimSort.xml");

        /// <summary>
        /// 将已排序的 MOD 包名列表导出为 RimSort 格式的 XML 文件。
        /// 数据来源由调用方提供，以适配不同 ModManager（Vanilla / ModManager / Prestarter）。
        /// </summary>
        /// <param name="sortedPackageIds">
        ///   排好序的激活 MOD packageId 列表（小写格式）。
        ///   由调用方从对应 ModManager 的数据源读取后传入。
        /// </param>
        public static void ExportCurrentLoadOrder(IEnumerable<string> sortedPackageIds)
        {
            try
            {
                string dir = Path.GetDirectoryName(ExportPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 获取游戏版本字符串（格式同 RimSort 示例：1.6.4633 rev1260）
                string version = VersionControl.CurrentVersionStringWithRev;

                // 获取所有官方扩张（DLC）的包名列表 (knownExpansions)
                var knownExpansions = ModLister.AllExpansions
                    .Select(e => e.linkedMod.ToLowerInvariant())
                    .ToList();

                var doc = new XmlDocument();
                var decl = doc.CreateXmlDeclaration("1.0", null, null);
                doc.AppendChild(decl);

                var root = doc.CreateElement("ModsConfigData");
                doc.AppendChild(root);

                // <version>
                var versionEl = doc.CreateElement("version");
                versionEl.InnerText = version;
                root.AppendChild(versionEl);

                // <activeMods>
                var activeEl = doc.CreateElement("activeMods");
                root.AppendChild(activeEl);
                foreach (var id in sortedPackageIds)
                {
                    var li = doc.CreateElement("li");
                    li.InnerText = id.ToLowerInvariant();
                    activeEl.AppendChild(li);
                }

                // <knownExpansions>
                var expansionsEl = doc.CreateElement("knownExpansions");
                root.AppendChild(expansionsEl);
                foreach (var id in knownExpansions)
                {
                    var li = doc.CreateElement("li");
                    li.InnerText = id;
                    expansionsEl.AppendChild(li);
                }

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\r\n",
                    NewLineHandling = NewLineHandling.Replace,
                    OmitXmlDeclaration = false,
                };
                using var writer = XmlWriter.Create(ExportPath, settings);
                doc.Save(writer);

                Log.Message($"[BetterModSort] RimSort XML exported to: {ExportPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[BetterModSort] Failed to export RimSort XML: {ex}");
            }
        }
    }
}
