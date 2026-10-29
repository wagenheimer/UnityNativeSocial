# Unity Native Social

Platform-native social API wrapper for Unity. Replaces Unity's deprecated `Social` class with direct platform calls.

## Supported Platforms

| Platform | Service | Features |
|----------|---------|----------|
| Android  | Google Play Games | Authenticate (auto + manual sign-in), Increment/Unlock achievements, Show achievements UI, Leaderboards (submit/show), Server auth code (for Unity Authentication / backends) |
| iOS      | Game Center       | Authenticate, Report progress (0-100%), Show achievements UI, Leaderboards (submit/show) |
| Windows/Mac/Linux | Steamworks | Set stats (delta or absolute), Unlock achievements (explicit or stat-threshold), StoreStats, Flush |

### Android requirements

Android features require the [Google Play Games plugin for Unity](https://github.com/playgameservices/play-games-plugin-for-unity)
v2+ (`com.google.play.games` package) — the same plugin also provides the achievement/leaderboard
IDs you map in `Initialize`. The `Authenticate`/`ManuallyAuthenticate`/`GetServerAuthCode` APIs
require plugin v11+.

Google ships the plugin as a UPM package embedded in its repository, so it can be installed directly
from Git (the recommended method — it makes Unity's Version Define set `WAGENHEIMER_NATIVESOCIAL_GPGS`
automatically). The Setup Audit's **"⬇ Install Google Play Games (UPM)"** button (or
`Tools > Wagenheimer > Native Social > Install Google Play Games (UPM)...`) does this for you; manually:

```
https://github.com/playgameservices/play-games-plugin-for-unity.git?path=Assets/Public/GooglePlayGames/com.google.play.games#v2.3.0
```

> Importing the `.unitypackage` from the release page instead installs *loose* files under
> `Assets/GooglePlayGames`, which are **not** a resolvable package — the version define never fires and
> every Android call compiles out silently. The one-click installer detects that layout and migrates it.

## ANDROID SIGN-IN: certificates, Google Cloud AND Play Games are required

> **Do not stop after creating an OAuth client in Google Cloud. Every distinct package + signing SHA-1 used by an installed build needs an Android OAuth client in the SAME Cloud project AND a linked Android credential in Play Games Services. Store signing, upload signing and local testing can use THREE DIFFERENT certificates.**
>
> **Saving credentials is not the final step:** configure tester access, review/publish pending Play Games changes, then verify both a direct install and a Google Play install. A green Unity Setup Audit cannot verify your remote credentials.

Read the complete [Android sign-in setup and troubleshooting guide](docs/ANDROID_SIGN_IN_SETUP.md) before testing. The same workflow is in **Tools > Wagenheimer > Native Social > Dashboard > Checklist**, with separate checks for each certificate and each console.

| Build/signature origin | Where to find SHA-1 | Register in Cloud and link in Play Games? |
|---|---|---|
| **STORE: Google Play app signing** | Play Console > your app > **Protected with Play > Play Store protection > Manage Play app signing > App signing key certificate** | **Yes**, for Play-distributed installs, including test tracks |
| **UPLOAD: your upload keystore** | Same page > **Upload key certificate**, or `keytool -list -v -keystore "YOUR_KEYSTORE" -alias "YOUR_ALIAS"` | Yes **if you directly install builds signed with this key**; uploading an AAB alone does not make this the Store signature |
| **TEST: local/debug/custom development key** | NativeSocial Android diagnostics / installed APK certificate, or the actual debug/custom keystore | **Yes**, for each distinct local signing key you test |

**Three roles do not always mean three clients:** reuse an existing linked client when both package and SHA-1 match. Never infer the certificate from a key's filename, a credential's name, or the Development Build checkbox. Preserve existing credentials; add missing certificate combinations instead of replacing a working Store credential. See the guide for key upgrades and additional signing keys.

## Troubleshooting Google Play Games sign-in (quick reference)

If `Authenticate`/`ManuallyAuthenticate` returns `Canceled` or `DeveloperError`, **do not assume the player cancelled**:
Google's plugin frequently maps configuration failures to `Canceled` and hides the real native error.

1. Read `NativeSocial.GetAndroidDiagnostics()` (also printed once per session and available in the
   runtime debug overlay) and compare its **signing cert SHA-1** + **package** + **Play Games app id**
   with Play Console > Play Games Services > Configuration > Credentials.
2. Capture a fresh attempt (not an old log line):
   ```powershell
   adb logcat -d -v time -s Unity 'PlayGamesServices[SignInAuthenticator]' SignInChimeraActivity
   ```
   Look for `DEVELOPER_ERROR` / `APP NOT CORRECTLY CONFIGURED` and the `Cert SHA1 fingerprint` /
   `App ID from manifest` Google prints.
3. Common causes: unregistered signing SHA-1 (debug/upload/store key), missing tester access,
   unpublished Play Games changes, wrong Cloud project, empty `com.google.android.gms.games.APP_ID`.
4. Startup prompt was dismissed before? Tap the game's achievements button
   (`NativeSocial.OpenAchievementsUI`) for a fresh manual attempt - progress is never erased.

Full checklist: [docs/ANDROID_SIGN_IN_SETUP.md](docs/ANDROID_SIGN_IN_SETUP.md).

## Installation

### Via Unity Package Manager (Git URL)

```
https://github.com/wagenheimer/UnityNativeSocial.git
```

Or via `manifest.json`:
```json
"com.wagenheimer.nativesocial": "https://github.com/wagenheimer/UnityNativeSocial.git"
```
The package also installs [Wagenheimer Package Hub](https://github.com/wagenheimer/UnityPackageHub) as an unpinned dependency, so update checks always use the latest Hub.

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

### 3. Authenticate & AutoConnect (Recommended)

The easiest and cleanest way to connect on all platforms with zero boilerplate:

```csharp
// Call in your startup GameManager / Main.cs
// On mobile (Android/iOS) it always connects.
// On desktop, pass true only for Mac App Store builds (e.g. Config.Publisher == Publisher.MacAppStore).
NativeSocial.AutoConnect(enablePlatformSocial: Main.main.IsMobile);
```

`AutoConnect` automatically:
- Enables Google Play Games with debug logging and handles fallback to manual sign-in on Android. If the player dismisses that sign-in screen, it is not shown again at startup (remembered until a successful sign-in); `OpenAchievementsUI` still offers sign-in from the achievements button.
- Logs a `GPGS diagnostics` block on Android (package, Play Games app id, **SHA-1 of the certificate that signed the install**, installer, Play services versions). Also available as `NativeSocial.GetAndroidDiagnostics()` and in the debug overlay.
- Re-checks Play Games sign-in (without UI) when the game returns to the foreground.

On Android, achievement progress is sent with `SetStepsAtLeast(current)` (idempotent), so the `current` passed to `Report` must use the same unit as the achievement's steps in the Play Console. On Android **and** Game Center (iOS/macOS), progress reported while signed out is queued in PlayerPrefs (highest value per achievement) and delivered after the next successful sign-in. `NativeSocial.IsReadyToReport` is false only when a `Report` would be dropped (Steam before SteamAPI is ready), so code that tracks "already sent" values should wait for it. On macOS, a failed Game Center sign-in explains whether the build lacks the embedded profile / Game Center entitlement.
- Connects to Apple Game Center on iOS and Mac App Store.
- Silently keeps progress local without throwing errors on Big Fish, Steam or generic standalone targets.

For granular control, you can still call the lower-level methods directly:

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

### 5. Open Achievements UI (In-Game Button)

```csharp
// From any menu / button click (e.g. formExtras.cs)
NativeSocial.OpenAchievementsUI((opened, errorMessage) =>
{
    if (!opened && !string.IsNullOrEmpty(errorMessage))
    {
        Debug.LogWarning($"Could not display achievements: {errorMessage}");
    }
});
```

### 6. Leaderboards

```csharp
NativeSocial.SubmitScore("lb_highscore", 1234);
NativeSocial.ShowLeaderboardUI("lb_highscore"); // null = all leaderboards screen (Android)
```

### 6. Re-sync completed achievements (Android only)

```csharp
// IsAuthenticated is read-only: NativeSocial's own sign-in calls maintain it.
if (NativeSocial.IsAuthenticated)
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

`SteamManager` (or wherever you boot `SteamAPI.Init()`) only starts the API; tell NativeSocial when it is
ready, or every Steam call is a silent no-op:

```csharp
NativeSocial.SteamReady = SteamManager.Initialized;
```

### Steam unlock models

Each `SteamEntry` chooses how its achievement is unlocked:

| Mode | The game does | You configure on Steamworks |
|---|---|---|
| `SteamUnlockMode.StatThreshold` | writes the stat only, never calls `SetAchievement` | each achievement unlocks when its stat reaches the threshold — the legacy Storm Tale 2 model |
| `SteamUnlockMode.ExplicitAchievement` | calls `SetAchievement(Achievement)` on completion | the achievement exists with that exact API name |

`SteamEntry.SetStatAbsolute` selects how the stat is written: `true` writes the absolute `current`
value (legacy), `false` adds the reported `delta` to the stat's cached value.

`AchievementTierMap` exposes both as map-wide defaults (`SteamDefaultUnlockMode`,
`SteamSetStatAbsolute`) plus an optional per-tier `SteamAchievement` name (used only in explicit mode,
`BuildSteamMap` falls back to `SteamStat` when empty). For the legacy behavior pick **Stat threshold +
Write stat as absolute counter** in the map's "Steam unlock model" card.

## Editor Dashboard

`Tools > Wagenheimer > Native Social > Dashboard...` (or `Verify Setup...` to jump straight to the audit)
opens a UI Toolkit dashboard with six tabs:

- **Setup Audit** — automated checks for GPGS/Steamworks presence and scripting defines, bootstrap
  script presence, and achievement-mapping completeness (see below), each with a one-click fix where
  possible, plus "Copy Report" / "Copy AI Fix Prompt" buttons. When the Google Play Games plugin is
  missing or was imported as loose `Assets/` files, the fix installs it from Google's Git repository as
  a proper UPM package (moving any loose copy to the Trash first), which also activates the scripting define.
- **Play Games** — the Android setup form: application id, optional Android resources XML and the optional
  Web client id used by server auth codes (Cloud Save), with field-level validation and shortcuts to the
  Google Cloud credential pages. Apply calls Google's own Play Games setup and regenerates the manifest.
- **Achievements** — lists every `AchievementTierMap` asset in the project, how many entries are
  still missing a Google Play/Apple ID, and an **"⬆ Export for AppDeployHub"** button (see below).
- **Live Tester** — exercises `Authenticate`/`Report`/`SubmitScore` against whichever platform SDK
  is active, for quick manual testing in Play Mode.
- **Checklist** — a persistent (per-machine, `EditorPrefs`-backed) release checklist. It opens with a
  "How the integration works" summary and then spells out, in detail and per platform, everything needed
  for **Steam** (Steamworks.NET define/`SteamReady`/partner-site stats), **Android** (Google Play Games
  install/define/OAuth/IDs/sign-in/device test) and **iOS** (Game Center capability added automatically by the package's build step, IDs/auth/device test),
  plus the Google Play Console, App Store Connect, Steamworks partner-site and release steps. Projects
  that already sync Steam directly (a `SteamManager`-style script) get an explicit reminder that only one
  owner per platform should report, so a trophy isn't unlocked twice.
- **Docs & Updates** — the code snippets below, ready to copy, plus the update checker.

## Achievement Tier Map (recommended integration)

Hand-rolling per-platform `Dictionary<string,string>` maps in code works for a handful of
achievements, but doesn't scale to a game with dozens of tiered trophies. `AchievementTierMap` is a
reusable `ScriptableObject` (`Assets > Create > Wagenheimer > Native Social > Achievement Tier Map`)
that holds one row per achievement tier:

| Field | Used by | Purpose |
|---|---|---|
| `TrophyNumber`, `Tier` | `LocId(trophyNumber, tier)` | Together they form the canonical `"Trophy{N}_{tier}"` key shared by every map and by `NativeSocial.Report`/`SyncCompleted` call sites — **always build the key with this static method, never format the string by hand**, or the key used to report progress will silently stop matching the key used to build the maps. |
| `SteamStat` | `BuildSteamMap()` | Steamworks stat name (e.g. `"Trophy4_2_Status"`). In `SteamThreshold` mode Steamworks unlocks the achievement from this stat; in explicit mode the achievement name defaults to it (override per tier with `SteamAchievement`). Map-wide behavior: `SteamDefaultUnlockMode` + `SteamSetStatAbsolute`. |
| `SteamAchievement` | `BuildSteamMap()` (explicit mode only) | Optional Steamworks achievement API name when it differs from `SteamStat`. Ignored in `StatThreshold` mode. |
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

## In-Game Runtime Debug Overlay (`NativeSocialDebugOverlay`)

Universal in-game UI Toolkit debug panel for testing achievements, authentication, leaderboards, and native UI dialogs in Development Builds and Unity Editor:

- **Automatic Lifecycle**: Automatically attaches in Editor and Development Builds via `[RuntimeInitializeOnLoadMethod]` — zero manual scene setup required.
- **Hot-key & Floating Pill**: Toggle anytime with `F7` or click the draggable floating `"🎮 SOCIAL DBG"` button on screen.
- **Live Status Indicator**: Floating LED dot shows platform connection state (🟢 Green = Connected/Signed in, 🟠 Amber = Initialized/Offline, 🔴 Red = Error/Uninitialized).
- **Interactive Achievement Tester**: Real-time cards for all mapped achievements with live progress bars, `+1 Step`, `+5 Steps`, `✓ Unlock (100%)`, and `↺ Reset` buttons.
- **Search & Filters**: Search by title, LocID or platform key, filter by target platform (Android / iOS / Steam) and status (In Progress / Completed).
- **Quick Platform Actions**: 1-click `Authenticate`, `Manual Auth (GPGS)`, `Show Achievements UI`, `Show Leaderboard UI`, and `Re-Sync Completed`.
- **Manual Command Dispatcher**: Ad-hoc tool to report any custom LocID with delta, current, and total steps, or submit leaderboard scores.
- **Live Event Log**: Real-time console tracking all `Report`, `Authenticate`, and `SubmitScore` calls with timestamps and severity highlights.
- **Mobile-Friendly**: Live zoom adjustment (`A-` / `A+`) and full-screen maximize (`⛶`) with PlayerPrefs persistence.

## Contributing: compile check before pushing

The CI only bumps the version and tags; it cannot compile the package (no Unity license on the runner). Most of
the code lives in platform-only `#if` blocks (Google Play Games, Game Center, Steam), so an error in one of them
used to reach a release unnoticed. Run the check before pushing:

```bash
Tools~/compile-check.sh "/path/to/a/unity/project/using/this/package"
```

It compiles `Runtime/` against the real Unity assemblies (Editor install + the project's `Library/ScriptAssemblies`
for Google Play Games and Steamworks) once per define set: Android, Android+Editor, iOS, macOS, Steam and Windows,
and exits non-zero listing the `error CS…` lines of any platform that fails. No Unity window is opened. The
`Tools~` folder is ignored by Unity.

## License

MIT

---

### Wagenheimer Unity ecosystem

[Package Hub](https://github.com/wagenheimer/UnityPackageHub) ·
[Unity Utils](https://github.com/wagenheimer/UnityUtils) ·
[Build Pipeline](https://github.com/wagenheimer/UnityBuildPipeline) ·
[Cloud Save](https://github.com/wagenheimer/UnityCloudSave) ·
[Rate Control](https://github.com/wagenheimer/UnityRateControl) ·
[IAP Helper](https://github.com/wagenheimer/UnityIAPHelper) ·
[LevelPlay Helper](https://github.com/wagenheimer/UnityLevelPlayHelper) ·
[Native Social](https://github.com/wagenheimer/UnityNativeSocial) ·
[Rewired Helper](https://github.com/wagenheimer/RewiredHelper) ·
[Timeline Typewriter](https://github.com/wagenheimer/UnityTimelineTypewriter) ·
[Tk2d Porter](https://github.com/wagenheimer/UnityTk2dPorter) ·
[Tracking](https://github.com/wagenheimer/UnityTracking) ·
[I2 Localization](https://github.com/wagenheimer/UnityI2Localization)

Explore every project at **[wagenheimer.com/opensource](https://wagenheimer.com/opensource)**.
