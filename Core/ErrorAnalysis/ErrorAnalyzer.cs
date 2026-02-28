using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterModSort.Tools;

namespace BetterModSort.Core.ErrorAnalysis
{
    public static class ErrorAnalyzer
    {
        public static CapturedErrorInfo AnalyzeError(string errorText, StackTrace stackTrace, Exception? exception)
        {
            var info = new CapturedErrorInfo
            {
                ErrorMessage = errorText,
                CapturedTime = DateTime.Now,
                StackTraceText = stackTrace.ToString()
            };

            var analyzedMods = new Dictionary<string, ModDllInfo>();

            foreach (var frame in stackTrace.GetFrames() ?? [])
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType == null) continue;

                var assembly = method.DeclaringType.Assembly;
                var assemblyName = assembly.GetName().Name;

                if (IsSystemAssembly(assemblyName ?? "")) continue;

                if (assemblyName == "BetterModSort" && method.DeclaringType.Namespace == "BetterModSort.Hooks")
                    continue;

                var modInfo = DllLookupTool.GetModFromAssembly(assembly);
                if (modInfo != null && !analyzedMods.ContainsKey(modInfo.PackageId))
                {
                    modInfo.StackFrameInfo = $"{method.DeclaringType.FullName}.{method.Name}";
                    analyzedMods[modInfo.PackageId] = modInfo;
                }
            }

            if (exception != null)
                foreach (var mod in DllLookupTool.GetModsFromException(exception))
                    if (!analyzedMods.ContainsKey(mod.PackageId))
                        analyzedMods[mod.PackageId] = mod;

            foreach (var mod in DllLookupTool.AnalyzeErrorLog(errorText))
                if (!analyzedMods.ContainsKey(mod.PackageId))
                    analyzedMods[mod.PackageId] = mod;

            info.RelatedMods = [.. analyzedMods.Values];
            return info;
        }

        private static bool IsSystemAssembly(string assemblyName)
        {
            var systemPrefixes = new[]
            {
                "mscorlib", "System", "Unity", "Mono", 
                "Assembly-CSharp", "0Harmony", "HarmonyLib",
                "netstandard", "Microsoft"
            };

            return systemPrefixes.Any(prefix => 
                assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
