# Unity Native Social

Platform-native social API wrapper for Unity. Replaces Unity's deprecated `Social` class with direct platform calls.

## Supported Platforms

| Platform | Service | Features |
|----------|---------|----------|
| Android  | Google Play Games | Authenticate (auto + manual sign-in), Increment/Unlock achievements, Show achievements UI, Leaderboards (submit/show), Server auth code (for Unity Authentication / backends) |
| iOS      | Game Center       | Authenticate, Report progress (0-100%), Show achievements UI, Leaderboards (submit/show) |
| Windows/Mac/Linux | Steamworks | Set stats, Unlock achievements, StoreStats, Flush |

### Android requirements

Android features require the [Google Play Games plugin for Unity](https://github.com/playgameservices/play-games-plugin-for-unity)
v2+ (`com.google.play.games` package) — the same plugin also provides the achievement/leaderboard
IDs you map in `Initialize`. The `Authenticate`/`ManuallyAuthenticate`/`GetServerAuthCode` APIs
require plugin v11+.

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

### 3. Authenticate (Android: Google Play Games / iOS: Game Center)

```csharp
NativeSocial.Authenticate(success =>
{
    if (success)
    {
        NativeSocial.SyncCompleted(new[] { "ach_wildcards" });
    }
    else
    {
        // Retry path: shows the profile-creation UI when the player has no
        // Play Games Services profile yet (Android, plugin v11+).
        NativeSocial.AuthenticateManually(retrySuccess => { ... });
    }
});
```

### 4. Server auth code (Android)

```csharp
// For Unity Authentication: CloudAuth.LinkGooglePlayGamesAsync(serverAuthCode)
NativeSocial.GetServerAuthCode(serverAuthCode => { ... });
```

### 5. Leaderboards

```csharp
NativeSocial.SubmitScore("lb_highscore", 1234);
NativeSocial.ShowLeaderboardUI("lb_highscore"); // null = all leaderboards screen (Android)
```

### 6. Set platform as ready (Android only)

```csharp
// NativeSocial.Authenticate already sets IsAuthenticated on success,
// but you can set it manually after your own GPGS sign-in flow:
NativeSocial.IsAuthenticated = true;
NativeSocial.SyncCompleted(new[] { "ach_wildcards", "ach_lightning" });
```

### 7. Show achievements UI

```csharp
NativeSocial.ShowAchievementsUI();
```

## Steam Setup

Steam integration is enabled automatically — the package's asmdef defines
`WAGENHEIMER_NATIVESOCIAL_STEAM` via `versionDefines` whenever
`com.rlabrecque.steamworks.net` (Steamworks.NET) is present in your project.
No manual Scripting Define Symbols setup is required.

## API

### `NativeSocial.Initialize(androidMap, iosMap, steamMap, androidLeaderboardMap, iosLeaderboardMap)`
Register achievement/leaderboard ID maps per platform. Call once at startup.

### `NativeSocial.Report(locId, delta, current, total, completed)`
Report achievement progress to the active platform.

### `NativeSocial.Authenticate(callback)`
Authenticate: Game Center on iOS, Google Play Games on Android (result of the plugin's automatic sign-in attempt).

### `NativeSocial.AuthenticateManually(callback)` (Android)
Manual sign-in with profile-creation UI — retry path when Authenticate fails. Plugin v11+.

### `NativeSocial.GetServerAuthCode(callback, forceRefreshToken)` (Android)
Server auth code for server-side sign-in (Unity Authentication / your backend). Plugin v2+.

### `NativeSocial.SyncCompleted(completedLocIds)`
Sync already-completed achievements after platform authentication.

### `NativeSocial.SubmitScore(locId, score)` / `NativeSocial.ShowLeaderboardUI(locId)`
Leaderboards: Google Play Games on Android, Game Center on iOS.

### `NativeSocial.ShowAchievementsUI()`
Show platform-native achievements UI.

### `NativeSocial.Authenticate(callback)`
Authenticate with Game Center (iOS) / Google Play Games (Android).

### `NativeSocial.Flush()` (Steam only)
Flush pending Steam stats.

## License

MIT
