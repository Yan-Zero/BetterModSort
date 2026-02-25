using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace BetterModSort.Tools
{
    /// <summary>
    /// DLL 查找工具 - 根据错误堆栈或 DLL 名称找到对应的 MOD
    /// 使用 LoadedModManager 内置的程序集信息
    /// </summary>
    public static class DllLookupTool
    {
        private static Dictionary<Assembly, ModContentPack>? _assemblyToModCache;
        private static Dictionary<string, ModContentPack>? _assemblyNameToModCache;

        /// <summary>
        /// 构建 Assembly 到 MOD 的映射缓存
        /// </summary>
        public static void BuildCache()
        {
            _assemblyToModCache = new Dictionary<Assembly, ModContentPack>();
            _assemblyNameToModCache = new Dictionary<string, ModContentPack>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                if (mod.assemblies?.loadedAssemblies == null) continue;

                foreach (var assembly in mod.assemblies.loadedAssemblies)
                {
                    if (!_assemblyToModCache.ContainsKey(assembly))
                    {
                        _assemblyToModCache[assembly] = mod;
                    }

                    var assemblyName = assembly.GetName().Name;
                    if (!_assemblyNameToModCache.ContainsKey(assemblyName))
                    {
                        _assemblyNameToModCache[assemblyName] = mod;
                    }
                }
            }
        }

        /// <summary>
        /// 根据异常获取对应的 MOD 信息
        /// </summary>
        public static List<ModDllInfo> GetModsFromException(Exception ex)
        {
            EnsureCache();
            var results = new List<ModDllInfo>();
            var stackTrace = new StackTrace(ex, true);

            foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                if (method == null) continue;

                var assembly = method.DeclaringType?.Assembly;
                if (assembly == null) continue;

                var modInfo = GetModFromAssembly(assembly);
                if (modInfo != null && !results.Any(r => r.PackageId == modInfo.PackageId))
                {
                    modInfo.StackFrameInfo = $"{method.DeclaringType?.FullName}.{method.Name}";
                    results.Add(modInfo);
                }
            }

            return results;
        }

        /// <summary>
        /// 根据 Assembly 获取对应的 MOD
        /// </summary>
        public static ModDllInfo GetModFromAssembly(Assembly assembly)
        {
            EnsureCache();

            // 优先直接查找 Assembly 对象
            if (_assemblyToModCache.TryGetValue(assembly, out var mod))
            {
                return CreateModDllInfo(mod, assembly);
            }

            // 回退到按名称查找
            var assemblyName = assembly.GetName().Name;
            if (_assemblyNameToModCache.TryGetValue(assemblyName, out mod))
            {
                return CreateModDllInfo(mod, assembly);
            }

            return null;
        }

        /// <summary>
        /// 根据 DLL 名称查找对应的 MOD
        /// </summary>
        public static ModDllInfo GetModFromDllName(string dllName)
        {
            EnsureCache();

            var cleanName = Path.GetFileNameWithoutExtension(dllName);
            if (_assemblyNameToModCache.TryGetValue(cleanName, out var mod))
            {
                return new ModDllInfo
                {
                    ModContentPack = mod,
                    PackageId = mod.PackageId,
                    ModName = mod.Name,
                    DllName = cleanName
                };
            }

            return null;
        }

        /// <summary>
        /// 获取所有已加载 MOD 的 DLL 信息
        /// </summary>
        public static List<ModDllInfo> GetAllModDllMappings()
        {
            EnsureCache();
            var results = new List<ModDllInfo>();

            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                if (mod.assemblies?.loadedAssemblies == null) continue;

                foreach (var assembly in mod.assemblies.loadedAssemblies)
                {
                    results.Add(CreateModDllInfo(mod, assembly));
                }
            }

            return results;
        }

        /// <summary>
        /// 分析错误日志字符串，提取可能的 MOD 信息
        /// </summary>
        public static List<ModDllInfo> AnalyzeErrorLog(string errorLog)
        {
            EnsureCache();
            var results = new List<ModDllInfo>();

            foreach (var kvp in _assemblyNameToModCache)
            {
                if (errorLog.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var mod = kvp.Value;
                    if (!results.Any(r => r.PackageId == mod.PackageId))
                    {
                        results.Add(new ModDllInfo
                        {
                            ModContentPack = mod,
                            PackageId = mod.PackageId,
                            ModName = mod.Name,
                            DllName = kvp.Key
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 根据 ModContentPack 获取其所有已加载的程序集
        /// </summary>
        public static List<Assembly> GetAssembliesFromMod(ModContentPack mod)
        {
            return mod.assemblies?.loadedAssemblies?.ToList() ?? new List<Assembly>();
        }

        private static ModDllInfo CreateModDllInfo(ModContentPack mod, Assembly assembly)
        {
            return new ModDllInfo
            {
                ModContentPack = mod,
                PackageId = mod.PackageId,
                ModName = mod.Name,
                DllName = assembly.GetName().Name,
                DllPath = assembly.Location
            };
        }

        private static void EnsureCache()
        {
            if (_assemblyToModCache == null || _assemblyNameToModCache == null)
            {
                BuildCache();
            }
        }
    }

    public class ModDllInfo
    {
        public ModContentPack ModContentPack { get; set; }
        public string PackageId { get; set; }
        public string ModName { get; set; }
        public string DllName { get; set; }
        public string DllPath { get; set; }
        public string StackFrameInfo { get; set; }

        public override string ToString()
        {
            var info = $"[{PackageId}] {ModName} - DLL: {DllName}";
            if (!string.IsNullOrEmpty(StackFrameInfo))
            {
                info += $" at {StackFrameInfo}";
            }
            return info;
        }
    }
}
