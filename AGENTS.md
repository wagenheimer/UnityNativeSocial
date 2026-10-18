# Native Social — Agent Notes

UPM package. Repo root = package root, installed via git URL, no wrapper Unity project.

## Structure

- `Runtime/NativeSocial.cs` — the static entry point. `Initialize(androidMap, iosMap, steamMap)` must be
  called once at startup (see `Editor/PackageHubBootstrap`/`NativeSocialAudit`'s "Project bootstrap script"
  check) or every `Report`/`SubmitScore` call silently no-ops. `NativeSocial.AutoConnect(bool enablePlatformSocial = true)`
  is the zero-boilerplate one-liner for game managers (`Main.cs`): automatically manages Google Play Games
  activation with debug logs + fallback to manual auth on Android, Apple Game Center on iOS/Mac App Store,
  and avoids touching platform social on Big Fish / Steam / standalone builds.
  `NativeSocial.OpenAchievementsUI(Action<bool, string> onResult)` is the zero-boilerplate one-liner for
  in-game achievement buttons (`formExtras.cs`): shows the native UI directly if signed in, or triggers
  the platform-specific sign-in retry flow and presents the UI upon success.
  Platform SDKs are wrapped behind `WAGENHEIMER_NATIVESOCIAL_STEAM`/`WAGENHEIMER_NATIVESOCIAL_GPGS` compile-time defines
  (`Runtime/Wagenheimer.NativeSocial.asmdef`'s `versionDefines`, both with an empty `expression` so they
  fire for *any* resolved version of `com.rlabrecque.steamworks.net`/`com.google.play.games` — Game Center
  needs no such gate, it's built into Unity's iOS and macOS StandaloneOSX support).
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
- `Editor/GpgsInstaller.cs` — one-click installer for the official Google Play Games plugin, exposed as the
  Setup Audit's GPGS fix button, a "Platform SDK Actions" card, and a menu item. Installs the UPM package
  embedded in Google's repo (`https://github.com/playgameservices/play-games-plugin-for-unity.git?path=Assets/Public/GooglePlayGames/com.google.play.games#v2.3.0`)
  via `Client.Add`, so it resolves as `com.google.play.games` and the asmdef's `versionDefines` sets
  `WAGENHEIMER_NATIVESOCIAL_GPGS`. It also installs EDM4U (git) first when absent, because GPGS' package.json
  depends on it, and moves any loose `Assets/GooglePlayGames` copy to the Trash (a duplicate-types failure
  otherwise). Never revert this to `Application.OpenURL` — the whole point is that the .unitypackage install
  can't set the define.
- `Editor/UI/AchievementMapEditorView.cs` — the card-per-trophy achievement editor, shared by the Dashboard's
  Achievements tab and `AchievementTierMapInspector` (custom Inspector). Don't fork it into two copies — the
  UnityBuildPipeline Publishers UI drifted exactly that way.
- `Editor/AppDeployHubClient.cs` - `AppDeployHubSettings` (server URL + chosen app in
  `ProjectSettings/NativeSocialAppDeployHub.json`; the e-mail and the device key AppDeployHub issues at sign-in ONLY in EditorPrefs (the key is tied to the
  server that issued it) or the `APPDEPLOYHUB_API_KEY` env var; the PASSWORD is used once and never stored or logged -
  never write a secret into a project file) and `AppDeployHubClient` (HttpClient against AppDeployHub
  `/api/v1`; results are delivered on the main thread by polling `EditorApplication.update`, never from a background
  thread). `ValidateBaseUrl` refuses plain http except for localhost so the key never travels unencrypted. A push to
  Google Play / Apple is opt-in per call and must stay opt-in: it creates real achievements on the store.
  The wire format is AppDeployHub `appdeployhub-achievements/v1` plus the optional per-entry `localizations` list; the
  AppDeployHub side lives in its `AchievementExchange` / `AchievementImportService` and must be kept in sync by hand.
  `Editor/AssemblyInfo.cs` exposes internals to `Wagenheimer.NativeSocial.EditorTests`.
- `Editor/NativeSocialDashboardWindow.cs` + `Editor/UI/*` — UI Toolkit dashboard (`ns-` USS prefix), same
  structure as `UnityRewiredHelper`'s:   **Setup Audit**, **Achievements** (lists every `AchievementTierMap`
  asset, per-platform fill-in counts, Create New Map), **Live Tester** (`NativeSocialHelperView.cs` —
  Authenticate/Report/SubmitScore), **Checklist** (`EditorPrefs`-persisted, `nativesocial_chk_` prefix —
  `NativeSocialChecklistView.cs` is the detailed per-platform guide: a "How the integration works" card plus
  Project setup / Steam / Android / iOS / Google Play Console / App Store Connect / Steamworks partner site /
  Release groups; keep every group's `Category` string matching an entry in the `Items` array or the items
  silently don't render), **Docs & Updates**. Menu items: `Tools/Wagenheimer/Native Social/Dashboard...` (priority 0),
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

## UI Toolkit: leading-icon text
Never put an emoji/symbol inline at the start of a `Button.text` (or a lone `Label`) — on Windows the fallback glyph draws wider than it measures and the following text overlaps the icon. Use `NativeSocialUIStyle.ApplyIconText(button, text)` (and `CreateIconLabel` for title labels) so the icon gets its own reserved box.


## REQUIRED: Android certificates and remote sign-in setup

Read [docs/ANDROID_SIGN_IN_SETUP.md](docs/ANDROID_SIGN_IN_SETUP.md) before diagnosing Android sign-in or changing setup instructions. Keep that guide, README and `Editor/UI/NativeSocialChecklistView.cs` consistent.

- Inventory STORE app signing, UPLOAD and TEST/local certificates. Register every distinct **package + SHA-1** in the linked Cloud project AND link each Android OAuth client in Play Games Configuration. Three roles can share keys; do not demand exactly three clients or create duplicates.
- Current Store certificate path: app > **Protected with Play > Play Store protection > Manage Play app signing > App signing key certificate**. Upload key certificate is a different section. Verify menu labels against current official docs; do not keep sending users to the retired App integrity page.
- Never infer a certificate from a name, Development Build flag, a Published badge or the signing filename. Inspect the installed build diagnostics/native log. Do not generate/reset keys, replace working credentials or wipe game/Play Services data to fix OAuth registration.
- Cloud client creation alone is incomplete. Verify Play Games credential linkage, exact package/fingerprint/Application ID, tester account, Google Auth Platform testing audience when applicable, pending Play Games publication and propagation. Do not claim the remote setup is correct from local audit checks.
- Basic Android sign-in uses Android clients. A Web/game-server client is only for intentional backend server-auth-code integration; never require it for basic authentication or confuse it with the numeric manifest APP_ID.
- Unity `Canceled` can hide native `DEVELOPER_ERROR`. Compare native logs at the same timestamp. If startup remembered a dismissal, use `OpenAchievementsUI` for a user-triggered retry; do not erase progress.
- Test a direct install AND a Play release-track install. Internal App Sharing can have its own certificate; signing-key upgrades may require additional clients. Do not hardcode this game's fingerprints/project IDs into reusable package defaults.
- CLI IAM/IAP OAuth commands are not Android OAuth client creation. State tool/access limits accurately. Never log private keys, passwords, access tokens or client secrets.
- Server-only credential changes normally need no new APK; manifest/package/signature/SDK changes do. Keep release/version automation unchanged for documentation work.

## Google Play Games login debugging playbook (for AI agents)

When a user reports "Google Play Games sign-in fails / returns Canceled", do NOT jump to
"reinstall the plugin" or "reset keys". Work this checklist first:

1. Get the runtime diagnostics: `NativeSocial.GetAndroidDiagnostics()` output (package, Play Games app id,
   signing cert SHA-1, installer, Play services versions) from the device log or the debug overlay.
2. Capture a FRESH logcat attempt:
   `adb logcat -d -v time -s Unity 'PlayGamesServices[SignInAuthenticator]' SignInChimeraActivity`
   Unity's `Canceled` frequently HIDES a native `DEVELOPER_ERROR` - compare timestamps.
3. Verify the chain: installed build's (package + SHA-1) -> Android OAuth client in the SAME Cloud project
   -> Android credential linked in Play Games Services -> tester access -> publication/propagation.
   Cloud client alone is NOT enough; Play Games credential linkage is mandatory.
4. Check the manifest application id (`com.google.android.gms.games.APP_ID`) is the 12-digit Play Games
   number, not an OAuth client id.
5. Test BOTH a direct install (TEST/UPLOAD key) and a Play-distributed install (STORE key) - they use
   different certificates and need separate credentials.
6. Do NOT wipe game/Play Services data, reset upload keys, or generate new keystores as a fix.
7. If startup remembered a dismissal, use `NativeSocial.OpenAchievementsUI` for a user-triggered retry;
   do not erase progress (PlayerPrefs pending progress is preserved).
8. Run the headless audit: `Unity -batchmode -quit -projectPath <project> -executeMethod
   Wagenheimer.NativeSocial.Editor.NativeSocialAudit.RunHeadlessAndLog` to catch local config errors.

Useful code entry points: `Runtime/NativeSocial.AndroidDiagnostics.cs` (diagnostics), `Runtime/NativeSocial.cs`
(`DescribeSignInFailure`, `Authenticate`, `AuthenticateManually`, `AutoConnect`), `Editor/NativeSocialAudit.cs`
(local checks, prompts). Docs: `docs/ANDROID_SIGN_IN_SETUP.md`.
