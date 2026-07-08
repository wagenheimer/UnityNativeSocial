# Unity Native Social

Platform-native social API wrapper for Unity. Replaces Unity's deprecated `Social` class with direct platform calls.

## Supported Platforms

| Platform | Service | Features |
|----------|---------|----------|
| Android  | Google Play Games | Increment/Unlock achievements, Show achievements UI |
| iOS      | Game Center       | Report progress (0-100%), Show achievements UI, Authenticate |
| Windows/Mac/Linux | Steamworks | Set stats, Unlock achievements, StoreStats, Flush |

## Installation

### Via Unity Package Manager (Git URL)

```
https://github.com/wagenheimer/UnityNativeSocial.git
```

Or via `manifest.json`:
```json
"com.wagenheimer.nativesocial": "https://github.com/wagenheimer/UnityNativeSocial.git"
```

## Quick Start

### 1. Configure achievement maps at game startup

```csharp
using Wagenheimer.NativeSocial;

void Awake()
{
    NativeSocial.Initialize(
        androidMap: new Dictionary<string, string> {
            { "ach_wildcards", "CgkI_aXR36YSEAIQAQ" },
            { "ach_lightning", "CgkI_aXR36YSEAIQAw" },
        },
        iosMap: new Dictionary<string, string> {
            { "ach_wildcards", "com.example.wildcards" },
            { "ach_lightning", "com.example.lightning" },
        },
        steamMap: new Dictionary<string, SteamEntry> {
            { "ach_wildcards", new SteamEntry("STAT_WILDCARDS", "ACH_WILDCARDS") },
            { "ach_lightning", new SteamEntry("STAT_LIGHTNING", "ACH_LIGHTNING") },
        }
    );
}
```

### 2. Report achievement progress

```csharp
NativeSocial.Report("ach_wildcards", delta: 1, current: 5, total: 20, completed: false);
NativeSocial.Report("ach_wildcards", delta: 0, current: 20, total: 20, completed: true);
```

### 3. Set platform as ready (Android only)

```csharp
// After GPGS authentication succeeds:
NativeSocial.IsAuthenticated = true;
NativeSocial.SyncCompleted(new[] { "ach_wildcards", "ach_lightning" });
```

### 4. Show achievements UI

```csharp
NativeSocial.ShowAchievementsUI();
```

## Steam Setup

Steam integration is enabled automatically — the package's asmdef defines
`WAGENHEIMER_NATIVESOCIAL_STEAM` via `versionDefines` whenever
`com.rlabrecque.steamworks.net` (Steamworks.NET) is present in your project.
No manual Scripting Define Symbols setup is required.

## API

### `NativeSocial.Initialize(androidMap, iosMap, steamMap)`
Register achievement ID maps per platform. Call once at startup.

### `NativeSocial.Report(locId, delta, current, total, completed)`
Report achievement progress to the active platform.

### `NativeSocial.SyncCompleted(completedLocIds)`
Sync already-completed achievements after platform authentication.

### `NativeSocial.ShowAchievementsUI()`
Show platform-native achievements UI.

### `NativeSocial.Authenticate(callback)`
Authenticate with Game Center (iOS only).

### `NativeSocial.Flush()` (Steam only)
Flush pending Steam stats.

## License

MIT
