# Native Social — Agent Notes

UPM package. Repo root = package root, installed via git URL, no wrapper Unity project.

## Structure

- `Runtime/NativeSocial.cs` — the static entry point. `Initialize(androidMap, iosMap, steamMap)` must be
  called once at startup (see `Editor/PackageHubBootstrap`/`NativeSocialAudit`'s "Project bootstrap script"
  check) or every `Report`/`SubmitScore` call silently no-ops. Platform SDKs are wrapped behind
  `WAGENHEIMER_NATIVESOCIAL_STEAM`/`WAGENHEIMER_NATIVESOCIAL_GPGS` compile-time defines
  (`Runtime/Wagenheimer.NativeSocial.asmdef`'s `versionDefines`, both with an empty `expression` so they
  fire for *any* resolved version of `com.rlabrecque.steamworks.net`/`com.google.play.games` — Game Center
  needs no such gate, it's built into Unity's iOS support).
- `Runtime/AchievementTierMap.cs` — reusable per-game `ScriptableObject`: one row per achievement tier
  (`TrophyNumber`/`Tier`/`SteamStat`/`GooglePlayId`/`AppleId`, plus display metadata used only by the
  AppDeployHub exporter below). `LocId(trophyNumber, tier)` is the canonical `"Trophy{N}_{tier}"` string —
  use this static method everywhere a LocId is built, never re-format the string by hand; every map builder
  and any `NativeSocial.Report`/`SyncCompleted` call site must agree on the exact format. A cell left empty
  (Google Play/Apple IDs are often unknown early) is always safe: the map builders skip it, and
  `NativeSocial.Report` already no-ops for a LocId with no mapping on the active platform. Unit-tested in
  `Tests/Editor/AchievementTierMapTests.cs`.
- `Editor/AchievementExchangeExporter.cs` — exports an `AchievementTierMap` to the versioned JSON exchange
  format (`appdeployhub-achievements/v1`) [AppDeployHub](https://github.com/wagenheimer/AppDeployHub)'s
  Achievements import feature reads (`AppDeployHub.Domain.Models.AchievementExchange` on that side — the
  two must stay in sync by hand, there is no shared package between the two repos).
- `Editor/NativeSocialAudit.cs` — headless-friendly audit (`AuditSeverity`/`AuditResult`, same shape as
  `UnityRewiredHelper`'s `RewiredHelperAudit.cs`): platform SDK presence/defines, bootstrap script presence
  (and `AddBootstrapToCurrentScene()` — finds the compiled `NativeSocialBootstrap` type via reflection and
  attaches it; if the script was *just* generated this run and hasn't compiled yet, it tells the user to
  retry rather than silently creating an empty GameObject, which was a real bug fixed 2026-09-29), and
  achievement-mapping completeness (every `AchievementTierMap` asset in the project, missing Google
  Play/Apple ID counts). Every finding also carries a plain-language `WhatIsThis` explanation, rendered by
  `Editor/UI/NativeSocialAuditView.cs` — when adding a new check, always fill this in; the audience is a
  developer unfamiliar with platform SDKs, not someone who's read this file.
- `Editor/I2Bridge.cs` — reflection-only I2 Localization wrapper (languages, term lookup, `EnsureTerm` that seeds an
  English translation without ever overwriting one). Never write `using I2.Loc;` here: no hard I2 dependency.
  `Editor/AchievementTextResolver.cs` turns an entry's term keys (+ literal fallbacks) into name/earned/not-earned
  strings for an I2 language, and `AchievementI2Tools` fills default term keys / generates missing terms — the UI
  preview and the AppDeployHub exporter both go through the resolver so they can't disagree. Tier numeral
  (I/II/III) is appended only to names resolved from a shared I2 term (`AppendTierNumeral`), never to literals.
- `Editor/UI/AchievementMapEditorView.cs` — the card-per-trophy achievement editor, shared by the Dashboard's
  Achievements tab and `AchievementTierMapInspector` (custom Inspector). Don't fork it into two copies — the
  UnityBuildPipeline Publishers UI drifted exactly that way.
- `Editor/AppDeployHubClient.cs` - `AppDeployHubSettings` (server URL + chosen app in
  `ProjectSettings/NativeSocialAppDeployHub.json`; the API key ONLY in EditorPrefs / the `APPDEPLOYHUB_API_KEY`
  env var - never write a secret into a project file) and `AppDeployHubClient` (HttpClient against AppDeployHub
  `/api/v1`; results are delivered on the main thread by polling `EditorApplication.update`, never from a background
  thread). `ValidateBaseUrl` refuses plain http except for localhost so the key never travels unencrypted. A push to
  Google Play / Apple is opt-in per call and must stay opt-in: it creates real achievements on the store.
  The wire format is AppDeployHub `appdeployhub-achievements/v1` plus the optional per-entry `localizations` list; the
  AppDeployHub side lives in its `AchievementExchange` / `AchievementImportService` and must be kept in sync by hand.
  `Editor/AssemblyInfo.cs` exposes internals to `Wagenheimer.NativeSocial.EditorTests`.
- `Editor/NativeSocialDashboardWindow.cs` + `Editor/UI/*` — UI Toolkit dashboard (`ns-` USS prefix), same
  structure as `UnityRewiredHelper`'s: **Setup Audit**, **Achievements** (lists every `AchievementTierMap`
  asset, per-platform fill-in counts, Create New Map), **Live Tester** (`NativeSocialHelperView.cs` —
  Authenticate/Report/SubmitScore), **Checklist** (`EditorPrefs`-persisted, `nativesocial_chk_` prefix),
  **Docs & Updates**. Menu items: `Tools/Wagenheimer/Native Social/Dashboard...` (priority 0),
  `.../Verify Setup...` (priority 1, opens the Setup Audit tab directly).
- `Editor/PackageHubBootstrap/` — copied from `UnityIAPHelper`'s, renamed to the
  `Wagenheimer.NativeSocial.PackageHubBootstrap` namespace. Own `.asmdef`, not merged into the main Editor
  assembly.
- `Runtime/UI/NativeSocialDebugOverlay.cs` — in-game runtime debug overlay (not Editor-only), added to a
  scene via `Editor/UI/NativeSocialDebugOverlayEditor.cs`'s `AddDebugOverlayToScene()`.
- `Editor/UpdateChecker.cs` — copy-pasted-and-renamed from the sibling packages
  (UnityRateControl/UnityCloudSave/UnityRewiredHelper), not a shared library. If you fix a bug here, port
  the fix to the other `wagenheimer/Unity*` repos by hand.
- `Tests/Editor/` — `Wagenheimer.NativeSocial.EditorTests.asmdef` (same shape as
  `UnityRewiredHelper/Tests/Editor/Wagenheimer.RewiredHelper.EditorTests.asmdef`:
  `UNITY_INCLUDE_TESTS` define constraint, `nunit.framework.dll` precompiled reference,
  `autoReferenced: false`). Added 2026-09-29, covering only `AchievementTierMap` so far — compiles against
  the real asmdef shape but has never been run inside the actual Unity Test Runner (no Unity Editor
  available to verify this from an agent session); confirm a green run before trusting it as regression
  coverage for a release.

## Conventions & Gotchas

- Version bump/tag/release is automated by `.github/workflows/bump-version.yml` on every push to `master` —
  do not manually edit `package.json`'s `version` or add CHANGELOG entries by hand. **This workflow had a
  real ordering bug until 2026-09-29**: it used `anothrNick/github-tag-action` to create the git tag
  *before* the version-bump commit existed, so every tag pointed at a commit whose `package.json` still had
  the *old* version number (symptom: Package Hub's "Update" succeeds, but "Check for Updates" always reports
  outdated again, because the installed tag's own `package.json` never actually matches its tag name). Fixed
  by running that step with `DRY_RUN: true` (compute the next version only) and tagging *after* the bump
  commit — mirroring `UnityBuildPipeline`/`UnityRewiredHelper`'s (correct) commit-then-tag order. If you ever
  see this symptom again on any Wagenheimer package, check this exact ordering first before assuming a
  Package Hub bug.
- Every `.cs` file added to this package (or any Wagenheimer Unity package resolved via git UPM) **must**
  have its `.meta` committed alongside it. Unity does not auto-generate `.meta` files for immutable
  `Library/PackageCache` content the way it does for `Assets/` — a script with no committed `.meta` is
  invisible to `AssetDatabase` and silently excluded from compilation (symptom: `CS0246` "type not found" in
  a *different* file that references it). This bit the `Editor/Steam/*.cs` files added to
  `UnityBuildPipeline` on 2026-09-29 and was the root cause, not a stale cache.
- No Odin Inspector, no I2 Localization dependency in `Runtime/` or `Editor/`.
- Steamworks.NET/Google Play Games are Asset Store / manually-installed packages, referenced by name in
  `Runtime/Wagenheimer.NativeSocial.asmdef`'s `references` — always verify an API actually exists in a REAL
  resolved copy (`Library/PackageCache/com.rlabrecque.steamworks.net@*` /
  `com.google.play.games@*`) before assuming a symbol name; these APIs change across SDK versions.
