using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.AI
{
    /// <summary>
    /// 负责两类持久化数据的读写：
    /// A. bms.meta.txt  — 本次游戏会话的 LoadOrder Hash 以及通过报错圈定的嫌疑 MOD PackageId 名单（纯文本单行追加，内部去重）
    /// B. ShortDesc/    — AI 提炼后的 MOD 性质短描述缓存（按 PackageId 单文件保存，首行存放原生描述 Hash）
    /// </summary>
    public static class MetaDataManager
    {
        // ──────────────────────────────────────────────────────
        // 路径
        // ──────────────────────────────────────────────────────

        private static string SaveDir =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort");

        public static string MetaFilePath =>
            Path.Combine(SaveDir, "bms.meta.txt");

        public static string PrevMetaFilePath =>
            Path.Combine(SaveDir, "bms.meta.prev.txt");

        public static string ShortDescDir =>
            Path.Combine(SaveDir, "ShortDesc");

        // ──────────────────────────────────────────────────────
        // 中间状态（in-process 去重用）
        // ──────────────────────────────────────────────────────

        /// <summary>本次进程内已经写入过 meta 文件的 PackageId 集合（避免重复追加）</summary>
        private static readonly HashSet<string> _appendedPackageIds =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>本次启动计算出的 LoadOrder Hash，初始化后缓存，避免每次重算</summary>
        private static string? _currentSessionHash;

        // ──────────────────────────────────────────────────────
        // A. 嫌疑追踪 — bms.meta.txt
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 在游戏启动时调用一次。
        /// 计算当前的 LoadOrder Hash；若与已有 meta 文件首行不符，
        /// 则将旧文件备份为 .prev，并重新写入新 Hash；
        /// 若一致，则保留文件继续追加。
        /// </summary>
        public static void InitializeCurrentSession()
        {
            try
            {
                EnsureDir(SaveDir);
                _currentSessionHash = ComputeLoadOrderHash();
                _appendedPackageIds.Clear();

                if (File.Exists(MetaFilePath))
                {
                    string firstLine = ReadFirstLine(MetaFilePath);
                    if (firstLine == _currentSessionHash)
                    {
                        // Hash 一致，恢复已追加集合（避免重复写入）
                        foreach (var line in File.ReadAllLines(MetaFilePath).Skip(1))
                            if (!string.IsNullOrWhiteSpace(line))
                                _appendedPackageIds.Add(line.Trim());
                        return; // 退出，无需重写
                    }

                    // Hash 不同，将旧的 .meta.txt 备份为 .prev，然后重新新建一个 .meta.txt
                    if (File.Exists(PrevMetaFilePath))
                        File.Delete(PrevMetaFilePath);
                    File.Move(MetaFilePath, PrevMetaFilePath);
                }
                
                // 写入新 Hash 作为首行，开始新的记录
                File.WriteAllText(MetaFilePath, _currentSessionHash + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_MetaDataManagerInitFailed".TranslateSafe(ex.Message));
            }
        }

        /// <summary>
        /// 当捕获到报错并分析出相关 MOD 后调用，将新的嫌疑 PackageId 追加到 meta 文件。
        /// 内部 HashSet 保证同一进程内不重复写入。
        /// </summary>
        public static void AppendSuspectMods(IEnumerable<ModInfo> relatedMods)
        {
            if (relatedMods == null) return;
            try
            {
                EnsureDir(SaveDir);
                if (_currentSessionHash == null) InitializeCurrentSession();

                var sb = new StringBuilder();
                foreach (var mod in relatedMods)
                {
                    if (string.IsNullOrWhiteSpace(mod.PackageId)) continue;
                    if (!_appendedPackageIds.Add(mod.PackageId)) continue; // 已写过
                    sb.AppendLine(mod.PackageId.Trim());
                }
                if (sb.Length > 0)
                    File.AppendAllText(MetaFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_MetaDataManagerAppendFailed".TranslateSafe(ex.Message));
            }
        }

        /// <summary>
        /// 尝试从当前或 prev meta 文件中恢复上次会话的嫌疑 PackageId 列表。
        /// </summary>
        public static IReadOnlyCollection<string> GetSuspectPackageIds()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (_currentSessionHash == null) InitializeCurrentSession();
                string hash = _currentSessionHash!;

                string? chosenFile = null;
                if (File.Exists(PrevMetaFilePath) && ReadFirstLine(PrevMetaFilePath) == hash)
                    chosenFile = PrevMetaFilePath;
                else if (File.Exists(MetaFilePath) && ReadFirstLine(MetaFilePath) == hash)
                    chosenFile = MetaFilePath;

                if (chosenFile != null)
                    foreach (var line in File.ReadAllLines(chosenFile).Skip(1))
                        if (!string.IsNullOrWhiteSpace(line))
                            result.Add(line.Trim());
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_MetaDataManagerGetSuspectsFailed".TranslateSafe(ex.Message));
            }
            return result;
        }

        // ──────────────────────────────────────────────────────
        // B. 短描述缓存 — ShortDesc/
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 尝试读取 MOD 对应的短描述缓存。
        /// 若缓存存在且原生描述 Hash 未变，则返回 true 并输出短描述文本。
        /// </summary>
        public static bool TryGetShortDesc(string packageId, string rawDescription, out string shortDesc)
        {
            shortDesc = string.Empty;
            try
            {
                string path = GetShortDescPath(packageId);
                if (!File.Exists(path)) return false;

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2) return false;

                string cachedHash = lines[0].Trim();
                string currentHash = ComputeDescriptionHash(rawDescription);
                if (cachedHash != currentHash) return false;

                shortDesc = string.Join("\n", lines.Skip(1));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_MetaDataManagerTryGetDescFailed".TranslateSafe(packageId, ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 将 AI 提炼后的短描述连同原生描述 Hash 一起持久化到 ShortDesc/ 目录。
        /// </summary>
        public static void SaveShortDesc(string packageId, string rawDescription, string shortDesc)
        {
            try
            {
                EnsureDir(ShortDescDir);
                string path = GetShortDescPath(packageId);
                string hash = ComputeDescriptionHash(rawDescription);
                string content = hash + "\n" + shortDesc;
                File.WriteAllText(path, content, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_MetaDataManagerSaveDescFailed".TranslateSafe(packageId, ex.Message));
            }
        }

        // ──────────────────────────────────────────────────────
        // 辅助函数
        // ──────────────────────────────────────────────────────

        /// <summary>按加载顺序拼接所有激活 MOD 的 PackageId，计算一个简单字符串 Hash。</summary>
        public static string ComputeLoadOrderHash()
        {
            var ids = ModsConfig.ActiveModsInLoadOrder
                .Select(m => m.PackageId.ToLowerInvariant());
            string combined = string.Join("|", ids);
            return ComputeStringHash(combined);
        }

        private static string ComputeDescriptionHash(string text)
            => ComputeStringHash(text ?? string.Empty);

        /// <summary>简单稳定的 32 位 FNV-1a Hash，转十六进制字符串。</summary>
        private static string ComputeStringHash(string text)
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return hash.ToString("x8");
        }

        private static string GetShortDescPath(string packageId)
        {
            // packageId 中可能含有非法文件名字符，做简单清理
            string safe = string.Concat(packageId.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(ShortDescDir, safe + ".txt");
        }

        private static string ReadFirstLine(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            return reader.ReadLine()?.Trim() ?? string.Empty;
        }

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
