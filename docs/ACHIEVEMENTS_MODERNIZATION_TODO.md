# Achievements modernization — session handoff

Started from the user's request: fix/verify Storm-Tale2's achievement port, add Google Play + Apple
achievements, modernize `UnityNativeSocial`'s Editor UI to match `UnityRewiredHelper`/`UnityIAPHelper`
(Dashboard + Setup Audit + Checklist + Docs, with checks). **Update (this session, continued):** the
Dashboard rewrite (former item 1) plus the Setup Audit / Achievements / Live Tester / Checklist / Docs
views (former items 1-4) are now DONE — see "Done, round 2" below. Only tests (item 5), docs/AGENTS/CHANGELOG
(item 6) and the Storm-Tale2 wiring (item 7) remain. **Everything below compiles today** (verified with a
standalone `dotnet build` against real Unity/PackageHub DLLs) — nothing was left in a broken state.

The full plan (context, rationale, all four parts) is at
`C:\Users\Cezar Wagenheimer\.claude\plans\k-games-open-source-unityrewiredhelper-t-dapper-puppy.md` — read
that first for the *why*. This file is the concrete *what's left*.

## Done

### Storm-Tale2 (`K:\Games\Green Sauce Games\Storm-Tale2`, all uncommitted)
- Fixed the missed trophy 4 achievement hookup in `Assets/_Game/Scripts/Village/VillageItem.cs`.
- Normalized trophy 16's redundant sync pattern in `Assets/_Game/Scripts/Board/Token.cs`.
- Documented the `AchievementsList` (global) vs `TrophySaveList` (per-profile) scoping decision with a code
  comment in `Assets/_Game/Scripts/_Game/Main.cs`.
- Wrote `docs/ACHIEVEMENTS.md`: all 18 trophies, real thresholds read from the 18
  `Assets/_Game/Trophies/Trophy N - *.asset` files, Steam stat names, empty Google Play/Apple ID columns.
- All of this (plus the earlier Steam port itself: `AchievementsSteam.cs`, `SteamManager.cs`,
  `steam_appid.txt`, the 18 original call sites) is **still uncommitted** — nothing pushed yet.

### UnityNativeSocial (`K:\Games\Open Source\UnityNativeSocial`, all uncommitted)
- `Runtime/AchievementTierMap.cs` (new): the reusable `ScriptableObject` type any game fills in —
  `TrophyNumber`/`Tier`/`SteamStat`/`GooglePlayId`/`AppleId` rows, `BuildAndroidMap()`/`BuildIosMap()`/
  `BuildSteamMap()` to feed `NativeSocial.Initialize(...)`, `CountMissingGooglePlay()`/`CountMissingApple()`
  for the audit. `LocId(trophyNumber, tier)` is the canonical `"Trophy{N}_{tier}"` string format shared by
  the maps and by any `NativeSocial.Report(...)` call site — **use this static method everywhere a LocId is
  built, never re-format the string by hand.**
- `Editor/PackageHubBootstrap/` (new folder + asmdef): copied verbatim from `UnityIAPHelper`'s, renamed to
  the `Wagenheimer.NativeSocial.PackageHubBootstrap` namespace.
- `Editor/NativeSocialAudit.cs` (new): `AuditSeverity`/`AuditResult` model (same shape as
  `UnityRewiredHelper`'s `RewiredHelperAudit.cs` — copy that file's `ToMarkdown`/`ToPromptMarkdown`/
  `RunHeadlessAndLog` conventions if you need a refresher), checks for GPGS/Steamworks presence+defines,
  bootstrap script presence, and **the new achievement-mapping completeness check** (finds every
  `AchievementTierMap` asset in the project, reports missing Google Play/Apple IDs per asset).
- `Editor/UI/NativeSocialUIStyle.cs`: added shared factory helpers (`CreateCard`, `CreateCodeCard`,
  `CreateInfoRow`, `CreateBulletPoint`, `CreateButton`, `CreateBadge`, `Apply`) promoted out of the old
  monolithic Hub window, so every new view file can share them — mirrors `RewiredHelperUIStyle.cs`'s role.
- **Stopgap wiring** (not the final structure — see below): `NativeSocialAudit.cs`'s two entry points that
  need a window (`OpenWindow()`, and the bootstrap-fix action) currently point at the *existing*
  `NativeSocialHubWindow` (its `Checker` tab, and a newly-exposed `CreateBootstrapScriptAssetPublic()`
  wrapper method added to that file) — purely so the package keeps compiling while the real Dashboard
  rewrite (next section) isn't done yet. **Both of these references must be repointed once the new
  Dashboard window exists** (see Next steps, item 1).

## Done, round 2 (this continued session)

- `Editor/NativeSocialDashboardWindow.cs` (new): replaces `NativeSocialHubWindow.cs` (deleted, along with its
  `.meta`). Mirrors `RewiredHelperDashboardWindow.cs` exactly — same header banner + tab bar + `ScrollView`
  content pattern. 5 tabs: **Setup Audit**, **Achievements**, **Live Tester**, **Checklist**, **Docs & Updates**.
  Menu items unchanged: `Tools/Wagenheimer/Native Social/Dashboard...` (priority 0) and
  `Tools/Wagenheimer/Native Social/Verify Setup...` (declared in `NativeSocialAudit.cs`, priority 1, now opens
  `NativeSocialDashboardWindow.Tab.SetupAudit`).
- `Editor/UI/NativeSocialAuditView.cs` (new): copied `RewiredHelperAuditView.cs`'s structure 1:1 (Run Audit,
  severity filters, Copy Report / Copy AI Fix Prompt, per-finding Fix buttons), swapped in
  `NativeSocialAudit`/`NativeSocialUIStyle` and `ns-` classes. Also carries a "Scene Setup Actions" card
  (Add Bootstrap to Scene / Add In-Game Debug Overlay to Scene — these two manual actions had no other home
  after the old Hub window's Checker tab was retired).
- `Editor/UI/NativeSocialAchievementsView.cs` (new): lists every `AchievementTierMap` asset
  (`NativeSocialAudit.FindAllAchievementTierMaps()`), per-platform fill-in counts, a Select Asset button, and
  a "+ Create New Map" button (`NativeSocialAudit.CreateAchievementTierMapAsset()`).
- `Editor/UI/NativeSocialHelperView.cs` (new): the old Hub window's Helper tab (Authenticate / Report /
  SubmitScore live testers) moved here basically unchanged — it's genuinely useful and NativeSocial-specific.
- `Editor/UI/NativeSocialChecklistView.cs` (new): persistent via `EditorPrefs` (`nativesocial_chk_` prefix),
  copied `RewiredHelperChecklistView.cs`'s structure exactly. Categories: Setup, Google Play Console, App
  Store Connect, Steam, Release (17 items total).
- `Editor/UI/NativeSocialDocsView.cs` (new): the 4 original code snippets (Initialize, Report, Leaderboard,
  SyncCompleted) plus a new 5th snippet showing `AchievementTierMap` end-to-end, plus the About card and an
  "🔄 Check for Updates" button wired to the existing `Editor/UpdateChecker.cs`.
- `NativeSocialAudit.cs`'s two stopgap references are fixed: `OpenWindow()` and the bootstrap-fix action now
  point at `NativeSocialDashboardWindow`/its own `CreateBootstrapScriptAsset()` (moved in-file from the old
  Hub window, now `internal static` alongside the new `AddBootstrapToCurrentScene()`).
- `Editor/UI/NativeSocialUIStyle.cs`: added `CreateCallout(message, severity)`, mirroring
  `RewiredHelperUIStyle.CreateCallout` — needed by the Audit/Achievements views' empty-state messages.
- `Editor/UI/NativeSocialCommon.uss`: added `ns-header*`, `ns-toolbar-actions`, `ns-tab-row`, `ns-callout*`,
  `ns-checklist-item*` rules (mirroring `RewiredHelperCommon.uss`'s equivalents) so the new Dashboard/Audit/
  Checklist views have real styling, not just the legacy `ns-tab-bar`/`ns-card`/`ns-btn-*` classes.
- **`.meta` gap closed**: every new/moved file from this whole modernization effort (this round's 6 new
  `.cs` files + the `Editor/NativeSocialAudit.cs`, `Runtime/AchievementTierMap.cs`,
  `Editor/PackageHubBootstrap/PackageHubBootstrap.cs`/`.asmdef` from the previous round, and the
  `PackageHubBootstrap` folder itself) now has a real Unity `.meta` with a random GUID — previously flagged
  as a known gap, now fixed.
- Re-verified with the same standalone `dotnet build` harness
  (`C:/Users/CEZARW~1/AppData/Local/Temp/rwcheck2/ns/ns.csproj`, updated to include all the new files):
  **0 errors, 0 warnings.**

## Next steps (in order)

1. ~~**Tests**~~ — **DONE (2026-09-29).** `Tests/Editor/Wagenheimer.NativeSocial.EditorTests.asmdef`
   (exact shape of `UnityRewiredHelper`'s), `Tests/Editor/AchievementTierMapTests.cs`: `LocId` format,
   `TryGetEntry` hit/miss, `BuildAndroidMap`/`BuildIosMap`/`BuildSteamMap` skip empty-ID entries,
   `CountMissingGooglePlay`/`CountMissingApple`. **Honesty note (still applies):** these compile against
   the real asmdef shape but were never run inside the actual Unity Test Runner from this session (no
   Unity Editor available here) — confirm a green run before trusting this as real regression coverage.

2. ~~**Docs**~~ — **DONE.** `README.md`'s "Editor Dashboard"/"Achievement Tier Map"/"Exporting to
   AppDeployHub" sections were already written in an earlier pass of this modernization (just not marked
   done in this file). `AGENTS.md` added 2026-09-29, modeled on `UnityRewiredHelper/AGENTS.md` — also
   documents the `bump-version.yml` tag-ordering bug found and fixed the same day (see below) and the
   git-package `.meta`-file requirement. CHANGELOG.md is auto-generated by CI on push — never hand-edited,
   per the established rule.

3. **Found and fixed while doing the above, worth flagging separately**: `.github/workflows/bump-version.yml`
   created the git tag *before* the version-bump commit (via `anothrNick/github-tag-action`'s default
   behavior), so every tag here pointed at a commit whose `package.json` still had the OLD version number.
   Symptom the user actually hit: Package Hub's "Update" succeeds (clones the tag fine — the tag's CODE is
   current), but "Check for Updates" keeps reporting outdated forever after, because the installed package's
   own `package.json` never matches its tag name. Fixed by switching the tag-computation step to
   `DRY_RUN: true` and tagging manually *after* the bump commit, matching `UnityBuildPipeline`/
   `UnityRewiredHelper`'s already-correct order. The existing bad `v1.6.0` tag itself still needs to be
   re-pointed at the correct commit (`f6fb707`, where `package.json` actually says `1.6.0`) — that's a
   force-push and needs explicit user confirmation before doing it (asked, awaiting answer as of this note).

3. ~~**Storm-Tale2 wiring (Part D of the plan)**~~ — **DONE.** `com.wagenheimer.nativesocial` (`v1.5.0`,
   the tag `bump-version.yml` cut from this round's commit) is in `Packages/manifest.json`.
   `Assets/Resources/Social/AchievementTierMap.asset` holds all 54 rows (Google Play/Apple columns
   empty). `Main.cs`'s `Awake()` loads it via `Resources.Load` and calls `NativeSocial.Initialize`.
   `AchievementsSteam.cs` gained an `#if UNITY_ANDROID || UNITY_IOS`-gated `SyncAchievementTiersToNativeSocial`
   method (uses a hardcoded `TierThresholds` table, since there's no runtime TrophyConfig registry to query
   by number) — the Steam path and all 18 original call sites are untouched. See Storm-Tale2's
   `docs/ACHIEVEMENTS.md` → "Runtime wiring" for the exact detail. **Verification note:** unlike the rest of
   this round, this was NOT re-checked with a standalone `dotnet build` (Storm-Tale2's `Main.cs`/
   `AchievementsSteam.cs` pull in too many game-specific dependencies — I2 Localization, DarkTonic
   MasterAudio, etc. — to stand up a harness cheaply); it was verified by manual brace-balance/diff review
   and by directly cross-checking the exact `NativeSocial.Report`/`AchievementTierMap.LocId` signatures
   against `Runtime/NativeSocial.cs`/`Runtime/AchievementTierMap.cs`. Confirm with a real Unity Editor
   compile before shipping.

## Verification still needed once the above is done
- Full `dotnet build` re-check of every new/changed NativeSocial file (same standalone-harness technique
  used this session — see the plan's Verification section for why: this session already found a real
  Steamworks API name that changed between versions by checking the actual installed package rather than
  trusting memory; same discipline applies to GPGS/GameCenter API names here).
- Brace-balance + full diff review of the Storm-Tale2 wiring changes (same technique used for the original
  18-call-site port).
- Ask the user for confirmation before any commit+push, on both repos — nothing has been pushed yet, and
  Storm-Tale2's changes are not even committed locally yet.
