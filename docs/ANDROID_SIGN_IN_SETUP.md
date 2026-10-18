# Android sign-in: complete certificate and console checklist

For developers and AI agents setting up NativeSocial Google Play Games authentication. Console labels below were checked on 2026-10-18; Google may rename menus. This guide covers Android platform sign-in, not a mandatory backend integration.

> **REQUIRED CHAIN: installed build's package + signing SHA-1 -> Android OAuth client in the linked Google Cloud project -> Android credential linked in Play Games Services -> tester/publication access -> successful device sign-in.**
>
> Creating a client only in Cloud is incomplete. Linking only the upload certificate is incomplete for a differently signed Store install. Installing the Unity plugin alone cannot configure either console.

## 1. Identify the game and the correct Cloud project

1. Open Play Console > your app > Grow users (or Grow) > Play Games Services > Setup and management > Configuration.
2. Record the Play Games Application ID / project number and the linked Cloud project. Select that SAME project in Google Cloud; a similarly named project is not sufficient.
3. Record the exact Android application/package ID from Unity Player Settings and from the installed build. Free/paid variants or other package names require their own package/certificate combinations.
4. Ensure Google Play Game Services API (`games.googleapis.com`) is enabled in the linked Cloud project. The CLI can help check/select the project and enable APIs; IAM/IAP OAuth-client commands do not create the Android package/SHA-1 clients described here.

## 2. Inventory ALL signing certificates before creating clients

You normally encounter the following three roles. **Do not generate new signing keys merely to configure OAuth. Read the public certificate fingerprints of the keys already used by your builds.** Never reset an upload key as a sign-in troubleshooting step.

| Role | How to obtain its SHA-1 | Which installed builds use it? |
|---|---|---|
| STORE | Play Console > your app > **Protected with Play > Play Store protection > Manage Play app signing > App signing key certificate > SHA-1 certificate fingerprint** | Builds delivered by Google Play, including normal internal/closed/open test tracks |
| UPLOAD | Same page > **Upload key certificate > SHA-1 certificate fingerprint**; compare with your actual local upload keystore | Directly installed APKs signed with the upload key. Google uses this key to verify your upload, then signs distributed APKs with the app signing key |
| TEST / DEBUG / LOCAL | NativeSocial runtime Android diagnostics or `apksigner verify --print-certs "YOUR.apk"`; inspect the actual debug/custom keystore with keytool | APKs installed directly through Unity Build And Run, ADB or another local workflow, depending on the selected signing key |

Example commands (replace paths/aliases; keytool prompts for the password):

```powershell
keytool -list -v -keystore "C:\path\your-upload.keystore" -alias "your-alias"
keytool -list -v -keystore "$env:USERPROFILE\.android\debug.keystore" -alias androiddebugkey
apksigner verify --print-certs "C:\path\your-installed-build.apk"
```

The default debug path is only an example: Unity/custom build pipelines can use a different key. Prefer the actual installed build's diagnostics when investigating a failure. `NativeSocial.GetAndroidDiagnostics()` reports the installed package/signature and can also be viewed in the runtime debug overlay.

**Use SHA-1 (20 bytes), not MD5 or SHA-256, in the Android OAuth fingerprint field.** Digital Asset Links JSON normally shows SHA-256 and is not the source of the SHA-1. The public fingerprint is safe to compare; do not copy private keystores, passwords or tokens into documentation/logs.

Record a small per-game table: role, package, SHA-1, Cloud client name/ID, linked Play Games credential, publication state, tested install source. Names are descriptive only; compare the full fingerprint.

**Register each distinct combination, not an arbitrary total of three.** If upload and local testing use the same package/key, one linked client covers both. Different developer machines, CI keys, free/paid package names, signing-key upgrades or Internal App Sharing may add signatures. Internal App Sharing can re-sign builds with its own test certificate; it is different from an internal testing release track. For upgraded Store signing keys, inspect all app signing certificates and cover the signatures delivered to the Android versions you support.

## 3. Create or reuse an Android OAuth client in GOOGLE CLOUD for EACH combination

Repeat for STORE, UPLOAD-signed direct installs and TEST/local signatures that differ:

1. Open Google Cloud Console and select the project linked in step 1.
2. Go to **Google Auth Platform > Clients > Create client**. In older layouts: **APIs & Services > Credentials > Create credentials > OAuth client ID**.
3. Choose application type **Android**. A Web application client is not a substitute.
4. Use a descriptive name, such as `My Game - Play Store`, `My Game - Upload APK`, or `My Game - Local Test`.
5. Enter the exact package name and the complete **SHA-1 for that role**. Create/save the client and record its client ID.
6. If an existing client already matches both package and SHA-1 in the linked project, reuse it instead of making a duplicate. If Google reports the combination is already registered, locate the existing client/project rather than deleting credentials blindly.

If prompted to configure Google Auth Platform branding/audience/consent, finish the required setup. If the OAuth audience is Testing, authorize the actual Google account under its test users; this is separate from the Play Games tester list. Do not assume a generic OAuth verification warning is the cause of a certificate mismatch. Standard Play Games scopes (`games`, `games_lite`, `drive.appdata`) do not themselves require app verification, per Google's setup guide; additional scopes can change requirements.

## 4. Link EVERY client in GOOGLE PLAY CONSOLE / PLAY GAMES SERVICES

> **THIS SECOND CONSOLE STEP IS MANDATORY. Cloud client created does NOT mean Play Games credential linked.**

1. Return to Play Console > your app > **Grow users > Play Games Services > Setup and management > Configuration**.
2. In **Credentials**, select **Add credential > Android**.
3. Give it a clear role name, then select the matching Android **OAuth client** from step 3.
4. If missing from the dropdown, click **Refresh OAuth clients**. Check that you selected the same linked Cloud project.
5. Before saving, compare the displayed **Application ID, package name and full fingerprint** with your inventory. A name such as `My Game` or a Published badge is not proof of the right certificate.
6. Save. Repeat for EVERY distinct signature/package you need. Preserve the old credentials; the upload credential does not cover a different Store SHA-1 and the Store credential does not cover a differently signed local APK.
7. Review any **Use this credential for new installs** setting and applicable availability/publication options. Do not blindly change an existing credential used by live players. Confirm the new credential is enabled for its intended installs and review pending changes before release.

Do not create a second Play Games game/project just for a different certificate. Keep the game's achievement/leaderboard configuration in the same intended project.

## 5. Tester access, publication and propagation

1. In Play Games Services > Setup and management > **Testers**, add the Google account actually selected for this game in the Google Play Games app on the device. Being the Console owner, a license tester, a Cloud OAuth test user or an app release-track tester is not automatically the same as Play Games tester authorization. If using release-track access, explicitly enable it in the Play Games Testers settings.
2. In Play Games Services > Setup and management > **Publishing**, review pending changes and publish the intended configuration when ready. Saving a new credential is not proof the change is available to all players; an existing Published credential does not prove newly added credentials are published.
3. Play Games publication, Google Auth Platform audience/consent publication and the APK/AAB release are separate operations. An app can be live while its new Play Games changes are not.
4. Allow time for server propagation after changes; do not promise an instant fix or an exact delay. Tester access can take hours. Retry with a fresh sign-in and check current timestamps in logs.

## 6. Unity integration: application ID versus client IDs

1. Install the official Google Play Games Unity plugin as UPM, and use its Android Setup wizard (Window > Google Play Games > Setup > Android setup, depending on plugin version) to import the game's Android resources.
2. Verify the generated manifest's `com.google.android.gms.games.APP_ID` is the Play Games numeric Application ID. This is NOT an Android OAuth client ID ending in `.apps.googleusercontent.com`.
3. Basic platform authentication requires the Android clients/credentials above. Do NOT paste an Android client ID into a Web Client ID field. A separate Web/game-server client is needed only when intentionally using server-side auth-code/backend features; follow that feature's setup separately.
4. Call `NativeSocial.AutoConnect(...)` at startup, initialize the game's maps, and wire the achievements button to `NativeSocial.OpenAchievementsUI(...)` so users can explicitly retry sign-in.
5. If only remote OAuth/Play Games configuration changed, a rebuild is normally unnecessary. Rebuild when the package, manifest Application ID, SDK/configuration or signing key in the artifact actually changed.

## 7. Verify local AND Store installations

- Direct install: verify package and SHA-1 from runtime diagnostics against the TEST or UPLOAD client actually signing that APK. Sign in, open achievements and verify progress submission.
- Store install: install from an appropriate Google Play release track and repeat the checks against the STORE certificate. A successful ADB install does not prove a Play install works, or vice versa.
- Avoid uninstalling/clearing data as a first troubleshooting step: it can remove local progress. Do not wipe Play Services or remove the user's Google accounts to work around an unverified configuration problem.
- Record the timestamp, installed version, install source, returned status and relevant Android error. Unity audit success and a checked manual checklist are not remote authentication tests.

## 8. If sign-in still fails

Capture a fresh attempt, not an old log entry:

```powershell
adb logcat -d -v time -s Unity 'PlayGamesServices[SignInAuthenticator]' SignInChimeraActivity
```

| Evidence | Next action |
|---|---|
| `DEVELOPER_ERROR` / `APP NOT CORRECTLY CONFIGURED` | Compare the package, **Cert SHA1 fingerprint** and **App ID from manifest** printed by Google with the linked Play Games credential; then check tester/publication access |
| `Canceled` in Unity | Does not prove the player cancelled. The Google plugin can map configuration failures to Canceled; inspect the native Android log at the same timestamp |
| NativeSocial says startup prompt was dismissed before | Startup intentionally suppresses another automatic manual prompt. Tap the game's achievements/sign-in button (`OpenAchievementsUI`) to trigger a fresh manual attempt; do not delete progress |
| Client exists in Cloud but is absent in Play Games | Verify the linked Cloud project, refresh OAuth clients and add the Android credential |
| Works locally, fails from Play | Check STORE app signing SHA-1, not upload SHA-1; verify supported signing-key variants |
| Works from Play, fails locally | Check actual local APK SHA-1, not Store SHA-1 |
| Account token `BAD_AUTHENTICATION` outside the game | Investigate Google account/Play Games access too, but do not ignore a simultaneous game-specific DEVELOPER_ERROR |

## Official references

- [Play Games setup: create Android clients AND link credentials](https://developer.android.com/games/pgs/console/setup)
- [Protected with Play: current home of Play Store protection and app signing](https://support.google.com/googleplay/android-developer/answer/13857328?hl=en)
- [Play app signing: upload versus app signing certificates](https://support.google.com/googleplay/android-developer/answer/9842756)
- [Google Play Services client authentication and SHA-1](https://developers.google.com/android/guides/client-auth)
- [Play Games troubleshooting](https://developer.android.com/games/pgs/android/troubleshooting)
