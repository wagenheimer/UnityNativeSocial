# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
