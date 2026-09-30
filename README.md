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

## Editor Dashboard

`Tools > Wagenheimer > Native Social > Dashboard...` (or `Verify Setup...` to jump straight to the audit)
opens a UI Toolkit dashboard with five tabs:

- **Setup Audit** — automated checks for GPGS/Steamworks presence and scripting defines, bootstrap
  script presence, and achievement-mapping completeness (see below), each with a one-click fix where
  possible, plus "Copy Report" / "Copy AI Fix Prompt" buttons.
- **Achievements** — lists every `AchievementTierMap` asset in the project, how many entries are
  still missing a Google Play/Apple ID, and an **"⬆ Export for AppDeployHub"** button (see below).
- **Live Tester** — exercises `Authenticate`/`Report`/`SubmitScore` against whichever platform SDK
  is active, for quick manual testing in Play Mode.
- **Checklist** — a persistent (per-machine, `EditorPrefs`-backed) release checklist covering Setup,
  Google Play Console, App Store Connect, Steam and Release items.
- **Docs & Updates** — the code snippets below, ready to copy, plus the update checker.

## Achievement Tier Map (recommended integration)

Hand-rolling per-platform `Dictionary<string,string>` maps in code works for a handful of
achievements, but doesn't scale to a game with dozens of tiered trophies. `AchievementTierMap` is a
reusable `ScriptableObject` (`Assets > Create > Wagenheimer > Native Social > Achievement Tier Map`)
that holds one row per achievement tier:

| Field | Used by | Purpose |
|---|---|---|
| `TrophyNumber`, `Tier` | `LocId(trophyNumber, tier)` | Together they form the canonical `"Trophy{N}_{tier}"` key shared by every map and by `NativeSocial.Report`/`SyncCompleted` call sites — **always build the key with this static method, never format the string by hand**, or the key used to report progress will silently stop matching the key used to build the maps. |
| `SteamStat` | `BuildSteamMap()` | Steamworks stat name (e.g. `"Trophy4_2_Status"`), assumed to equal the achievement API name too — pass a custom map to `Initialize` instead if your Steam achievement names differ from your stat names. |
| `GooglePlayId` | `BuildAndroidMap()` | Google Play Games achievement ID, from the Play Console (or auto-filled by AppDeployHub — see below). Leave empty until it exists: `NativeSocial.Report` no-ops for an unmapped LocId, so a partially-filled map is always safe to ship. |
| `AppleId` | `BuildIosMap()` | Apple Game Center achievement ID, from App Store Connect (or auto-filled by AppDeployHub). Same empty-is-safe rule applies. |
| `NameTerm`, `EarnedDescriptionTerm`, `NotEarnedDescriptionTerm` | **Export + editor preview** — not read by `NativeSocial` itself | [I2 Localization](https://inter-illusion.com/tools/i2-localization) term keys for the tier's name / "earned" text / "not earned yet" hint. **Preferred source of all player-facing text.** Conventions: name = `trophy{N}` (shared by the trophy's tiers; the tier numeral I/II/III is appended automatically, see `AppendTierNumeral`), earned/not-earned = `Achievements/Trophy{N}_{tier}/Earned` and `.../NotEarned`. |
| `DisplayName`, `EarnedDescription`, `NotEarnedDescription` (literals) | **Fallback only** — used when the term key is empty, I2 isn't installed, or the term has no translation | Plain-text fallbacks; shown in orange in the editor so you can see what still isn't localized. |
| `Points`, `IsHidden` | **Export only** | Store metadata for the AppDeployHub export (points awarded; hidden until earned). |

```csharp
[SerializeField] private AchievementTierMap achievementMap;

void Awake()
{
    NativeSocial.Initialize(
        achievementMap.BuildAndroidMap(),
        achievementMap.BuildIosMap(),
        achievementMap.BuildSteamMap());
}

// Report a tier by trophy number — no hand-formatted LocId strings:
NativeSocial.Report(AchievementTierMap.LocId(trophyNumber: 4, tier: 2), delta: 1, current: 2, total: 3, completed: false);
```

### The achievement editor (Dashboard → Achievements, or select the asset)

Selecting an `AchievementTierMap` (or opening **Dashboard → Achievements**) shows a card per trophy with one
row per tier: the resolved **name / earned / not-earned** texts, points, hidden flag, and ✔/✘ chips for Steam,
Google Play, Apple and I2 at a glance. Use **Search** and the filter chips (*Missing Steam / Google Play /
Apple / I2 terms / Hidden*) to find gaps; the **✎** button opens the full field editor for a tier.
Texts are previewed in the **I2 language you pick** — the same resolution the AppDeployHub export uses.

I2 support is optional and reflection-based (the package never hard-depends on I2):

- **🔗 Fill default term keys** sets every empty term field to the convention above.
- **🛠 Generate missing I2 terms** creates, in your I2 language source, every configured term that doesn't exist
  yet. Only **English** text is written (seeded from the literal fallback, or the trophy's shared
  `trophy{N}description` term) and an existing translation is **never overwritten** — translate the rest with
  I2's own tools.
- **⬆ Export for AppDeployHub** exports the texts resolved in the selected language (locale derived from the
  I2 language code).

### Exporting to AppDeployHub

If you use [AppDeployHub](https://github.com/wagenheimer/AppDeployHub) (a self-hosted dashboard that
publishes app metadata, In-App Purchases, and achievements to the real store APIs), the
**"⬆ Export for AppDeployHub"** button on the Dashboard's Achievements tab writes a JSON file
(`appdeployhub-achievements/v1` format — one object per `AchievementTierMap` entry, with the
`DisplayName`/`EarnedDescription`/`NotEarnedDescription`/`Points`/`IsHidden`/`SteamStat`/`GooglePlayId`/`AppleId`
fields above) that you upload in AppDeployHub's Achievements page ("Import from Unity"). From there,
AppDeployHub can:

- **Create the achievements on Google Play and Apple for you**, via their real publishing APIs
  (Google's Games Configuration API and Apple's App Store Connect API both support programmatic
  achievement creation — confirmed against their official docs; this is not scraping or UI
  automation). You review/edit the imported rows first; nothing is pushed to either store until you
  click "Push".
- **Generate a ready-to-paste CSV for Steam**, since Steamworks has no public API for bulk-creating
  achievements — that part stays manual on the Steamworks Partner Site.
- Write the real Google Play/Apple achievement IDs it creates back into... well, not automatically
  back into this Unity asset (there's no live connection) — copy them from AppDeployHub's UI into this
  `AchievementTierMap`'s `GooglePlayId`/`AppleId` columns once created, the same way you would if you'd
  created them by hand.

The export format is intentionally simple, versioned JSON (see
`Editor/AchievementExchangeExporter.cs`) — if you don't use AppDeployHub, it's still a reasonable
starting point for writing your own importer against Google Play Console / App Store Connect.

### Sending straight to AppDeployHub (no file, no key to copy)

The achievement editor has a **☁ AppDeployHub** card that pushes the whole map to AppDeployHub through its API:

1. Enter the **Server URL** (https; plain http only for localhost) and sign in with your **AppDeployHub e-mail and
   password**. AppDeployHub checks them with the same rules as the website (same lockout) and issues a key for this
   machine *automatically* - you never see, copy or manage it, and the **password is never stored or logged**. The key
   acts as your account, so **one sign-in covers every app in every studio you own**; it is kept in your Editor
   preferences (tied to that server) and can be revoked per device in AppDeployHub (**Studio > API keys**) or with
   **Sign out**.
2. Pick the app(s) from the searchable list: records are grouped per game and badged **Android** / **iOS** with their
   package / bundle id. Tick **both** store records of your game - Android receives the Google Play achievements, iOS
   the Game Center ones (the project's Android and iOS identifiers are preselected, siblings included; the
   server URL and chosen apps are
   saved in `ProjectSettings/NativeSocialAppDeployHub.json` - no secret in it), then **Send to AppDeployHub**. A
   confirmation shows exactly what will be sent.

Accounts that cannot sign in by password, and CI, can paste a studio API key under *Advanced* (or set the
`APPDEPLOYHUB_API_KEY` environment variable) instead.

Texts go as English plus every other I2 language that has a *real* translation of the name (English is never sent
labeled as another language). AppDeployHub upserts by key into both Google Play and Apple Game Center, nothing is ever
deleted, and **nothing is pushed to a store unless you tick the push boxes** - by default you review and push from
AppDeployHub. The file export above remains available and carries the same translations.

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
