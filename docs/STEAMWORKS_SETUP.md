# Steamworks Setup: Achievements and Stats Guide

Complete checklist for setting up Steam achievements and progress stats with NativeSocial.

---

## 1. Overview of the Architecture

NativeSocial unifies achievements across Steam, Android (Google Play Games), and Apple (Game Center):
- **Achievement Tier Map (`AchievementTierMap.asset`)**: Defines each trophy, its tiers, platform IDs, progress targets, and I2 Localization terms.
- **Steam Default Unlock Model**:
  - **`ExplicitAchievement` (Recommended / Default)**: The game calls `SteamUserStats.SetAchievement("API_NAME")` directly when a tier is completed. This is the most reliable model because it guarantees immediate unlock for both progressive and single-event (story/boolean) achievements.
  - **`StatThreshold`**: The game only increments stats, and Steamworks unlocks achievements based on threshold rules configured on the Steamworks partner site.

---

## 2. Step 1: Create the Stats in Steamworks Partner

Steamworks has **no bulk import for stats**. Progress stats must exist in Steamworks before you import the achievements definitions.

1. Open **[Steamworks Partner](https://partner.steamgames.com)** and select your game app.
2. Navigate to **Edit Steamworks Settings > Stats & Achievements > Stats**.
3. Create each stat row required by your game (you can view and copy the exact list directly from Unity in **Tools > Wagenheimer > Native Social > Steam Setup**):

| Column | Recommended Value | Description |
|---|---|---|
| **API Name** | e.g. `stat_levels` | Must match the `SteamStat` in your `AchievementTierMap`. |
| **Type** | `INT` | 32-bit integer counter. |
| **Set By** | `Client` | Allows the game client to report progress. |
| **Increment Only** | `yes` | Ensures progress never regresses accidentally. |
| **Default** | `0` | Starting value for new players. |
| **Min** | `0` | Minimum value. |
| **Max** | Leave blank or set $\ge$ highest target | Upper limit (must be at or above your highest achievement target). |

---

## 3. Step 2: Export and Bulk-Import Achievements

1. In Unity, open **Tools > Wagenheimer > Native Social > Steam Setup** (or the **Achievements** tab).
2. Click **🎮 Export Steam Pack** and select an export destination folder.
3. NativeSocial generates:
   - `{AppID}-achievements-definitions.csv`
   - `{AppID}-achievements-localization.csv` (automatically populated with all translations from I2 Localization)
   - `icons/` folder with both achieved and dimmed unachieved icons
   - `stats-to-create.csv`
   - `README.md`
4. In Steamworks Partner, navigate to **Stats & Achievements > Achievements**, tab **Bulk Import/Export**:
   - Click **Configure Languages** to ensure languages match the columns of `{AppID}-achievements-localization.csv`.
   - Drag and drop `{AppID}-achievements-definitions.csv`, `{AppID}-achievements-localization.csv`, and all files inside `icons/` into the upload dropzone.
   - Ensure **Delete these achievements** is **NOT** checked.
   - Click **Save imported data**.

---

## 4. Step 3: Publish Changes

1. In Steamworks Partner, go to the **Publish** tab under Edit Steamworks Settings.
2. Review the diff (stats and achievements created/updated).
3. Click **Prepare for Publishing** and then **Publish to Steam**.

---

## 5. Verification Checklist in Unity

Run **Tools > Wagenheimer > Native Social > Verify Setup**:
- [x] **Steamworks SDK**: `com.rlabrecque.steamworks.net` package installed.
- [x] **App ID**: `SteamAppId` configured in `AchievementTierMap.asset` and matching `steam_appid.txt`.
- [x] **Steam Stat Names**: All achievement tiers have a valid `SteamStat` name assigned.
- [x] **Unlock Model**: `SteamDefaultUnlockMode` set to `ExplicitAchievement`.
