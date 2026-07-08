# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
