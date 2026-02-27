using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Verse;

namespace BetterModSort.Tools
{
    public static class I18n
    {
        private static Dictionary<string, string> _earlyTranslations = new Dictionary<string, string>();
        private static bool _initialized = false;

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // 获取当前正在使用的语言文件夹名称，如果在极早期调用导致异常则回退到"English"
                string langFolderName = "English";
                try
                {
                    if (Prefs.LangFolderName != null)
                        langFolderName = Prefs.LangFolderName;
                }
                catch
                {
                    // ignored
                }

                // 获取自身 Mod 的 RootDir
                string? modRootDir = null;
                try
                {
                    foreach (var mod in LoadedModManager.RunningMods)
                        if (string.Equals(mod.PackageId, "com.bettermodsort.rimworld", StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(mod.PackageIdPlayerFacing, "com.bettermodsort.rimworld", StringComparison.OrdinalIgnoreCase))
                        {
                            modRootDir = mod.RootDir;
                            break;
                        }
                }
                catch
                {
                    // ignored
                }

                if (string.IsNullOrEmpty(modRootDir))
                {
                    string assemblyLoc = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(assemblyLoc))
                        modRootDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyLoc), ".."));
                }

                if (string.IsNullOrEmpty(modRootDir)) return;
                string[] possibleLangDirs = [ Path.Combine(modRootDir, "Languages"), ];

                string? targetXmlPath = null;
                foreach (string langDir in possibleLangDirs)
                {
                    // 优先加载用户设置的语言，如果没有找到则回退到英语
                    string langFile = Path.Combine(langDir, langFolderName, "Keyed", "BetterModSort.xml");
                    if (File.Exists(langFile))
                    {
                        targetXmlPath = langFile;
                        break;
                    }
                    
                    // 回退方案：如果不是English但没找到，尝试找English
                    if (langFolderName != "English")
                    {
                         string fallbackFile = Path.Combine(langDir, "English", "Keyed", "BetterModSort.xml");
                         if (File.Exists(fallbackFile))
                         {
                             targetXmlPath = fallbackFile;
                             break;
                         }
                    }
                }

                if (targetXmlPath != null && File.Exists(targetXmlPath))
                    LoadTranslationsFromXml(targetXmlPath);
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_EarlyI18nFailed".Translate(ex.ToString()));
            }
        }

        private static void LoadTranslationsFromXml(string filePath)
        {
            try
            {
                XmlDocument doc = new();
                doc.Load(filePath);
                
                XmlNodeList languageDataNodes = doc.GetElementsByTagName("LanguageData");
                if (languageDataNodes.Count > 0)
                {
                    XmlNode dataNode = languageDataNodes[0];
                    foreach (XmlNode node in dataNode.ChildNodes)
                        if (node.NodeType == XmlNodeType.Element)
                            _earlyTranslations[node.Name] = node.InnerText;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_XmlLoadFailed".Translate(filePath, ex.ToString()));
            }
        }


        public static TaggedString TranslateSafe(this string key, params NamedArgument[] args)
        {
            // 尝试直接使用官方的 Translate
            try
            {
                if (LanguageDatabase.activeLanguage != null)
                    return key.Translate(args);
            }
            catch
            {
                // 如果抛出 No active language 异常，或者其他情况，吃掉异常走早期缓存
            }

            // 走早期缓存
            Initialize();

            if (_earlyTranslations.TryGetValue(key, out string translated))
            {
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        return string.Format(translated, args);
                    }
                    catch
                    {
                        return translated; // 格式化异常时直接返回
                    }
                }
                return translated;
            }

            // 如果都找不到，直接返回 key 即最坏情况
            return key;
        }
    }
}
