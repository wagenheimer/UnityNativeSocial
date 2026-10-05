# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.23.2] - 2026-10-05
- fix(ui): `NativeSocialDebugOverlay` now defines the `FindMapAsset()` helper it calls, and `ResolveLocIdTitle` iterates the entries instead of comparing the `AchievementTierEntry` struct to null (`CS0103` / `CS0019`).
- fix(audit): Escape the literal braces in the "Manual Edit Needed" dialog's interpolated string (`CS8635`).

## [1.23.1] - 2026-10-05
- fix(ui): Added the missing `using System.Collections;` to `NativeSocialDebugOverlay` so `HideToastRoutine`'s non-generic `IEnumerator` return type resolves (fixes `CS0305: Using the generic type 'IEnumerator<T>' requires 1 type arguments`).

## [1.23.0] - 2026-10-05
- feat(steam): Steam achievements can now go through `NativeSocial.Report` on every platform. `SteamEntry` gains `Mode` (`SteamUnlockMode.StatThreshold` / `ExplicitAchievement`) and `SetStatAbsolute`; `StatThreshold` + absolute writes reproduce the legacy Storm Tale 2 behavior (stats only, no `SetAchievement`), so a single cross-platform `Report` call covers Android, iOS and Steam.
- feat(map): `AchievementTierMap` gains map-wide `SteamDefaultUnlockMode` / `SteamSetStatAbsolute` and a per-tier `SteamAchievement` name; `BuildSteamMap()` honors them. Defaults keep pre-existing maps behaving exactly as before.
- feat(ui): Achievement editor gains a "Steam unlock model" card (dropdowns + live help); Setup Audit reports the active model; Checklist, Docs and README document both models.
- tests: Cover the new `BuildSteamMap` modes and the backward-compatible `SteamEntry` constructor.

## [1.22.3] - 2026-10-05
- feat(ios): Enable GameKit default achievement completion banner (`ShowDefaultAchievementCompletionBanner(true)`) automatically on iOS during initialization and authentication.
- feat(ui): Added in-game achievement unlock toast notification to `NativeSocialDebugOverlay` for visual feedback in Editor and builds.
- feat(audit): Added Mobile Player Authentication audit check and 1-click `Add Authenticate to <file>` automatic script injector.
- feat(audit): Added Best Practice check for In-Game Achievements UI Button (`ShowAchievementsUI()`) across Core, Android, and iOS audits and checklists.
- feat(bootstrap): Updated `NativeSocialBootstrap` template with `autoAuthenticateOnMobile = true` to authenticate automatically at startup.

## [1.22.0] - 2026-10-05
- feat: Comprehensive modern UI Toolkit in-game `NativeSocialDebugOverlay` matching `UnityIAPHelper`, `UnityRateControl`, and `UnityBuildPipeline`:
  - **Auto-Initialization**: Runs automatically via `[RuntimeInitializeOnLoadMethod]` in Development Builds and Editor (zero manual setup).
  - **Floating Pill Button**: Draggable `"🎮 SOCIAL DBG"` button with live status LED dot (Green/Amber/Red).
  - **Interactive Achievement Tester**: Dynamically renders card list of all achievements from `AchievementTierMap` and registered maps with live progress bars, `+1 Step`, `+5 Steps`, `✓ Unlock (100%)`, and `↺ Reset` buttons.
  - **Search & Filters**: Live search by title, LocID or platform key, platform filter pills (All / Android / iOS / Steam), and completion state filters (All / In Progress / Completed).
  - **Diagnostics & Quick Actions**: 1-click `Authenticate`, `Manual Auth (GPGS)`, `Show Achievements UI`, `Show Leaderboard UI`, and `Re-Sync Completed`.
  - **Manual Command Dispatcher**: Test arbitrary LocIDs, step counters, and leaderboard score submissions.
  - **Live Event Log**: Real-time event stream tracking all `Report`, `Authenticate`, and `SubmitScore` calls with timestamps and severity highlights.
  - **Mobile Touch Zoom & Maximize**: `A-` / `A+` zoom controls and `⛶` maximize toggle, with automatic mobile scale and PlayerPrefs persistence.
  - **PanelSettings Resource**: Bundled `NativeSocialDebugPanelSettings.asset` and `NativeSocialDebugTheme.tss` for error-free rendering on device builds.
- feat: Added observability properties (`IsInitialized`, `AndroidMap`, `IosMap`, `SteamMap`, `AndroidLeaderboardMap`, `IosLeaderboardMap`) and event hooks (`OnReport`, `OnSubmitScore`, `OnAuthenticated`, `OnLog`) to `NativeSocial`.

## [1.21.2] - 2026-10-03
- fix: Use the `UnityEngine.Social` facade for Game Center calls on iOS (`GameCenterPlatform` static methods no longer exist in Unity 6), fixing CS0120/CS1501 iOS build errors. `ShowLeaderboardUI` now opens the default Game Center leaderboards screen.

## [1.21.1] - 2026-10-03
- fix: Exclude Steamworks code from Android/iOS builds (Steamworks.NET has no mobile assembly), fixing CS0246 "The type or namespace name 'Steamworks' could not be found".

## [1.15.0] - 2026-09-30
- feat: Add "Pull from AppDeployHub" button in Achievements view to pull store-generated Google Play IDs and Apple IDs directly into `AchievementTierMap`.
- feat: Achievement list tooltips + I2 source/refresh, points card with store-limit validation and auto-distribute.
- feat: Searchable multi-select AppDeployHub app picker with platform badges.

## [1.14.0] - 2026-09-30
- chore: Automated version bump to v1.14.0.

## [1.4.1] - 2026-09-26
- fix: Replace invalid `Color.transparent` with `Color.clear` in `NativeSocialHubWindow.cs`.
- fix: Fix `PackageHubWindow.Open` method invocation to `PackageHubWindow.ShowWindow`.

## [1.4.0] - 2026-09-26
- feat: Add runtime in-game `NativeSocialDebugOverlay` and scene menu helper.

## [1.3.0] - 2026-09-26
- feat: UI Toolkit Dashboard (`Tools/Wagenheimer/Native Social/Dashboard...`, priority = 0) with comprehensive Checker and Helper:
  - **Overview**: System platform summary, detected SDKs, architecture highlights.
  - **Checker & Audit**: Platform diagnostics for Android GPGS, iOS Game Center, and Steamworks.NET with 1-click scripting define switcher.
  - **Helper & Live Tester**: Interactive test runner for Authenticate, Report progress, Submit leaderboard score, and platform UI invocations.
  - **Guides & Snippets**: Ready-to-copy code snippets with 1-click clipboard actions.
  - **About**: Ecosystem directory and author bio.
- feat: Fail-safe dual styling (inline flex layouts + `NativeSocialCommon.uss`).
- chore: Reorganize menu priorities with Dashboard as the primary option.

## [1.2.3] - 2026-09-18
- feat: migrate to centralized UnityPackageHub (`com.wagenheimer.packagehub`)

## [1.2.2] - 2026-09-05
- 9b8b256 fix: declare missing _iosLeaderboardMap field
- 351295e chore: bump version to v1.2.1

## [1.2.1] - 2026-09-05
- 06a9259 fix: expose WAGENHEIMER_NATIVESOCIAL_GPGS version define for com.google.play.games
- 096029e chore: bump version to v1.2.0

## [1.2.0] - 2026-09-05
- 979b1c9 feat: Android GPGS authentication (auto + manual), server auth code and leaderboards
- 8b7b5f7 chore: bump version to v1.1.4

## [1.1.4] - 2026-07-19
- a452ad6 fix: avoid false Report-before-Initialize warnings and log spam
- 03d763e chore: bump version to v1.1.3

## [1.1.3] - 2026-07-16
- f5e4b87 chore: translate UI, docs and comments to English
- c39d41f chore: bump version to v1.1.1

## [1.1.1] - 2026-07-09
- 2524ad9 fix: add missing .meta for Editor/ and Runtime/ folders
- 1c346fc chore: bump version to v1.1.0

## [1.1.0] - 2026-07-09
- 0deb51e feat: fix manual check ignoring 24h throttle, show dialog, redesign popup
- 93b9b40 chore: bump version to v1.0.3

## [1.0.3] - 2026-07-08
- a7c1fe6 fix: corrige geracao do CHANGELOG.md no workflow de bump de versao
### Changed
- `NativeSocial.ShowAchievementsUI()` now returns `bool` instead of `void`: true when a native achievements screen was shown, false when there is none for the current platform/state (e.g. Steam/standalone has no scriptable achievements overlay, or Android where the player isn't signed in). Callers should show their own custom achievements UI when it returns false.

## [1.0.2] - 2026-07-08
### Fixed
- `Editor/UpdateChecker.cs`: ambiguous `PackageInfo` reference (CS0104) between `UnityEditor.PackageInfo` and `UnityEditor.PackageManager.PackageInfo` — now fully qualified.
- `Editor/UpdateChecker.cs`: update-check URLs pointed at the `github.com` repo page on the `main` branch instead of `raw.githubusercontent.com` on `master` — the check silently never worked before this fix.
- `Editor/UpdateAvailableWindow.cs`: "Update Now" blocked the Editor UI thread with a busy-wait loop on the UPM `AddRequest`; now polled non-blockingly via `EditorApplication.update`.
### Added
- `Runtime/Wagenheimer.NativeSocial.asmdef`: explicit reference to the Steamworks.NET assembly (`com.rlabrecque.steamworks.net`), required to compile the Steam code path.
- `Runtime/Wagenheimer.NativeSocial.asmdef`: `versionDefines` entry that auto-defines `WAGENHEIMER_NATIVESOCIAL_STEAM` whenever Steamworks.NET is present, removing the manual Scripting Define Symbols step.
- Full English XML-doc/inline comments across `Runtime/NativeSocial.cs`, `Editor/UpdateChecker.cs`, `Editor/UpdateAvailableWindow.cs`, `Editor/NativeSocialMenuItems.cs`, and `Samples~/DefaultSetup/NativeSocialBootstrap.cs`.

## [1.0.0] - 2026-07-08
### Added
- NativeSocial static API: `Initialize`, `Report`, `SyncCompleted`, `ShowAchievementsUI`, `Authenticate`, `Flush`
- Android: Google Play Games integration via PlayGamesPlatform (IncrementAchievement, UnlockAchievement, ShowAchievementsUI)
- iOS: Game Center integration via GameCenterPlatform (ReportProgress, ShowAchievementsUI, Authenticate)
- Steam: Steamworks integration via SteamUserStats (SetStat, SetAchievement, StoreStats)
- Configurable achievement ID maps per platform (`Initialize` with LocID → PlatformID dictionaries)
- Editor auto-update checker (checks GitHub releases every 24h)
- UpdateAvailableWindow with one-click upgrade via UPM
- Menu items under Tools/Wagenheimer/Native Social
- CI workflow for automatic version bump on push to main
