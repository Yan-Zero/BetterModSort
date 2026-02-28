# Better Mod Sort (And Error Inspector)

[English](README.md) | [简体中文](README.zh.md)

<p align="center">
  <img src="Assets/About/preview.png" alt="Better Mod Sort" width="600"/>
</p>

<p align="center">
  RimWorld 1.6 · Prerequisite MOD: <a href="https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077">Harmony</a>
  ·
  <a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3673408015">Steam Workshop Page</a>
</p>

---

## What Does This Mod Do?

Have you installed a bunch of mods and don't know how to arrange the load order? This mod can connect to Large Language Models (LLMs) to suggest a more reasonable load order based on your mod list and actual error logs.

To let the AI know which mods might be conflicting, the mod automatically intercepts errors during game startup/running in the background, analyzes which mod caused them, and records them as a reference for AI sorting. Therefore, the general usage is: **play a session normally first (or at least load the game once) so the mod collects enough error information, and then use AI to sort.** This yields better results.

Of course, the error analysis itself is also incredibly useful. Even if you don't use AI sorting, it can help you clearly see whose fault the red errors are.

---

## Features

### AI-Assisted Sorting (Experimental)

This is the main feature of the mod. It is disabled by default and needs to be manually enabled in the Mod Settings, along with configuring the LLM's API Key.

Once enabled, the "Auto-sort" button in the mod list will trigger the AI process:

1. **Collect Basic Structure** — To save tokens, the engine won't send the detailed descriptions of all mods. It only collects the names of all active mods and vanilla load requirements (like `loadBefore` / `loadAfter` dependencies).
2. **Extract Descriptions of Suspect Mods** — Only "suspect mods" that have caused errors in the game will be passed to the LLM to shrink their descriptions into short summaries. (The results are cached locally so they don't need to be extracted again next time).
3. **Generate Soft Constraints** — The condensed mod information and historical error records are submitted to the LLM, requesting sorting suggestions in the form of `loadBefore` / `loadAfter`.
4. **Topological Sort** — The soft constraints returned by the AI are injected into the vanilla sorting engine, which then performs a topological sort.

So, for the AI to sort accurately, it's best to run the game once with your current mod list so the error analysis system can accumulate some data. It can still sort the first time you use it with no error data, but the AI will have less information to reference.

Note that AI sorting consumes API tokens. For instance, with 400 mods, the input will consume about 20k tokens.

### Error Source Analysis

This feature works automatically as soon as it's installed; no extra configuration is needed. It provides data for AI sorting, but it can also be used as a standalone troubleshooting tool.

When red errors occur in the game, the mod analyzes them from the following aspects:

- **DLL Stack Trace Mapping** — Extracts the assembly corresponding to each frame from the exception's call stack to find out which mod's DLL it is.
- **XML Parsing Exception Localization** — Intercepts the underlying XML processing routines, directly mapping the exact XML nodes that threw an exception back to their source files, mods, and specific XML NodePaths.
- **Def Config Error Analysis** — Intercepts Def configuration errors, traces their actual source files, extracts the entire `ParentName` inheritance chain, and identifies all Patch operations applied to that Def and its parent nodes.
- **Cross-Reference Sourcing** — Deeply parses errors like `Could not resolve cross-reference` to track down the exact Def, file, and specific XML node that used the missing reference, and lists any involved Patches.
- **CE Compatibility Attribution** — Resolves `no support for Combat Extended` errors by deeply analyzing stack frame parameters to pinpoint the specific item or race Defs that lack CE compatibility.
- **File Path & Workshop ID Extraction** — Extracts local path strings from errors like `Cannot load texture` or missing files, matching them to active mod root directories or decoding Steam Workshop IDs to find the mod.

The analysis results are saved to `BetterModSort.Error.txt` in the save directory, and the previous session's log is backed up as `BetterModSort.Error.prev.txt`. Formatted reports are also output in the game console (press `~` to open), looking something like this:

```text
Could not resolve cross-reference to Verse.WorkTypeDef named DoctorRescue (wanter=workTypes)
  -> [Cross-ref used in: DoctorRescue]
     - [Mod: Project RimFactory - Drones (spdskatr.projectrimfactory.drones)] Defs/ThingDef[defName=WarDroneStation]/modExtensions/li/workTypes
       File: D:\SteamLibrary\steamapps\workshop\content\294100\2037491557\Defs\ThingDefs_Buildings\Buildings_DroneStation.xml
  -> [Patch possibly involving: WarDroneStation]
     - [Mod: Project RimFactory - Drones (spdskatr.projectrimfactory.drones)] PatchOperationAdd (FindMod: Achtung!)
```

```text
[BetterModSort] ========== Error Analysis ==========
Time: 10:10:20
Error: Trying to get stat MeleeDamageAverage from TM_GloryMaul which has no support for Combat Extended.
Related Mods (3):
  - [ceteam.combatextended] Combat Extended
    DLL: CombatExtended
    Location: CombatExtended.StatWorker_MeleeDamageAverage.GetValueUnfinalized
  - [andromeda.nicebilltab] Nice Bill Tab
    DLL: NiceBillTab
    Location: NiceBillTab.StatRequestWorker.GetDPS
  - [ilyvion.loadingprogress] Loading Progress
    DLL: ilyvion.LoadingProgress
    Location: ilyvion.LoadingProgress.StaticConstructorOnStartupUtilityReplacement+d__2.MoveNext
=====================================
```

---

## Installation

### Steam Workshop

1. Subscribe to [Better Mod Sort](https://steamcommunity.com/sharedfiles/filedetails/?id=3673408015)
2. Ensure you are also subscribed to [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
3. Activate them in the mod list, placing Harmony above this mod.

### Manual Installation

1. Download the Release or clone this repository.
2. Copy the contents of the `Assets` folder to `Mods/BetterModSort/`
3. Compile the project and place the generated DLL in the `Assemblies/` directory.

---

## Settings

In the game, click **Options → Mod Settings → Better Mod Sort** to open.

### AI Connection Configuration

- **LLM API Key** — Your API key. If left blank, AI features will be unavailable.
- **LLM Base URL** — API address, defaulting to `https://api.openai.com/v1/chat/completions`. You can alter this to use compatible services like DeepSeek or Ollama.
- **LLM Model Name** — The model name, defaulting to `gpt-4o`.

### Experimental Features

- **Enable AI-Assisted Sorting** — Once checked, the "Auto-sort" button uses the AI process. Disabled by default.
- **Max Error Log Characters Sent to AI** — Limits the length of error content to prevent a single massive error from filling the context (Default: 8000).
- **Max Raw Description Characters for Summary Extraction** — Limits the length when extracting descriptions to save tokens (Default: 2500).
- **LLM Request Timeout (seconds)** — Sets the maximum waiting time for a single request (Default: 600).

### Debug Options

- **Enable Debug Dump** — Writes a key summary of each LLM communication to a file to help troubleshoot issues caused by prompts. Files are located under `%LOCALAPPDATA%Low/Ludeon Studios/RimWorld by Ludeon Studios/BetterModSort/Dump/`.
- **Open Data Folder** — Clicking this in-game directly opens the mod's data storage directory via the file explorer, which contains `Dump` (LLM communication summary logs), `ShortDesc` (AI-extracted Mod Desc summary cache), and `BetterModSort.Error.txt` (error analysis log file).

---

## Localization

Currently supports English, Simplified Chinese, and Russian.

Translation files are located at `Assets/Languages/<Language>/Keyed/BetterModSort.xml`. Pull Requests to add more languages are welcome.

---

## Project Structure

```txt
BetterModSort/
├── Assets/
│   ├── About/                 # Mod metadata, preview image
│   └── Languages/             # Translation files
├── AI/
│   ├── Dialog_AILoading.cs    # AI sorting progress dialog
│   ├── LLMClient.cs           # OpenAI-compatible HTTP client
│   ├── MetaDataManager.cs     # Suspect mod list & short desc cache persistence
│   └── PromptBuilder.cs       # Builds various Prompts
├── Core/
│   └── ErrorAnalysis/         # Error analysis core
│       ├── Enrichers/         # Specific error enrichers (XML, CE, DefConfig, etc.)
│       ├── CapturedErrorInfo.cs
│       ├── ErrorAnalyzer.cs
│       ├── ErrorHistoryManager.cs
│       ├── IEnrichmentData.cs # Standard interface for Enrichment Data
│       └── IErrorEnricher.cs  # Standard interface for Enrichers
├── Hooks/
│   ├── ErrorCaptureHook.cs    # Interception entry point when an error occurs
│   ├── LogPatch.cs            # Hooks Log.Error / ErrorOnce
│   ├── ModsConfigPatch.cs     # Hacks auto-sort & duplicate mod checking
│   └── XmlSource.cs           # Hooks game asset loading to build tracking
├── Tools/
│   ├── DllLookupTool.cs       # DLL ↔ Mod Mapping
│   ├── I18n.cs                # Early multi-language loading
│   ├── ModInfo.cs             # Unified Mod info entity
│   └── XmlSourceMap.cs        # Source tracking dictionary for Defs/Patches
├── BetterModSortMod.cs        # Mod entry point
└── BetterModSortSettings.cs   # Settings definitions
```
