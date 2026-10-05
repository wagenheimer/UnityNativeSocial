using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace Wagenheimer.NativeSocial
{
    /// <summary>
    /// One tier of one achievement: the game-defined key (<see cref="LocId"/>) plus the per-platform IDs it
    /// maps to. A "tier" models a stepped/incremental achievement (e.g. bronze/silver/gold) — a single-tier
    /// achievement is just one entry with <see cref="Tier"/> = 1.
    /// </summary>
    [Serializable]
    public struct AchievementTierEntry
    {
        [Tooltip("The trophy/achievement number in your own game data (e.g. Storm-Tale2's 1-18 trophy numbers).")]
        public int TrophyNumber;

        [Tooltip("1-based tier within that achievement (1 = first/lowest tier).")]
        public int Tier;

        [Tooltip("Steamworks achievement stat name for this tier (e.g. \"Trophy1_1_Status\"), or empty if this game doesn't ship on Steam / doesn't track this tier via a stat.")]
        public string SteamStat;

        [Tooltip("Steamworks achievement API name for this tier, when it is unlocked explicitly by the game (Steam Unlock Mode = Explicit Achievement on the map). Leave empty to fall back to the stat name; ignored entirely in Stat Threshold mode.")]
        public string SteamAchievement;

        [Tooltip("Google Play Games achievement ID for this tier (from the Google Play Console), or empty until that console listing exists.")]
        public string GooglePlayId;

        [Tooltip("Apple Game Center achievement ID for this tier (from App Store Connect), or empty until that console listing exists.")]
        public string AppleId;

        [Tooltip("I2 Localization term holding this trophy's NAME (normally shared by all its tiers — the tier numeral I/II/III is appended automatically). Default: \"trophy{N}\". Empty = use the literal DisplayName below.")]
        public string NameTerm;

        [Tooltip("I2 Localization term for the text shown once this tier is earned. Empty = use the literal EarnedDescription below. The Achievements tab can generate missing terms for you.")]
        public string EarnedDescriptionTerm;

        [Tooltip("I2 Localization term for the hint/goal text shown before this tier is earned. Empty = use the literal NotEarnedDescription below.")]
        public string NotEarnedDescriptionTerm;

        [Tooltip("Literal fallback name for this tier, used only when NameTerm is empty or I2 Localization isn't available. Only used by the \"Export for AppDeployHub\" button — not read by NativeSocial itself.")]
        public string DisplayName;

        [Tooltip("Player-facing description shown once the achievement is earned. Only used by the \"Export for AppDeployHub\" button.")]
        public string EarnedDescription;

        [Tooltip("Player-facing description shown before the achievement is earned (hint/goal text). Only used by the \"Export for AppDeployHub\" button.")]
        public string NotEarnedDescription;

        [Tooltip("Points awarded for this tier on Google Play / Apple Game Center. Only used by the \"Export for AppDeployHub\" button.")]
        public int Points;

        [Tooltip("Hidden until earned, on Google Play / Apple Game Center. Only used by the \"Export for AppDeployHub\" button.")]
        public bool IsHidden;

        [Tooltip("Whether this achievement is incremental (counter/progress steps) on Google Play Games.")]
        public bool IsIncremental;

        [Tooltip("Number of steps to unlock this achievement if IsIncremental is true (Google Play Games).")]
        public int StepsToUnlock;
    }

    /// <summary>
    /// Reusable per-game data asset: the full list of achievement tiers and their per-platform IDs, built once
    /// from the game's own achievement/trophy design and consumed at startup to build the maps
    /// <see cref="NativeSocial.Initialize"/> needs. Ships empty — a game creates its own instance
    /// (<c>Assets > Create > Wagenheimer > Native Social > Achievement Tier Map</c>) and fills it in.
    ///
    /// Google Play / Apple IDs are commonly unknown at first (those consoles' achievement listings often
    /// don't exist yet when a game starts integrating this). Leaving a cell empty is always safe: the maps
    /// built here simply omit that entry, and <see cref="NativeSocial.Report"/> already no-ops for a LocId
    /// with no mapping on the active platform.
    /// </summary>
    [CreateAssetMenu(fileName = "AchievementTierMap", menuName = "Wagenheimer/Native Social/Achievement Tier Map")]
    public class AchievementTierMap : ScriptableObject
    {
        public List<AchievementTierEntry> Entries = new List<AchievementTierEntry>();

        [Tooltip("When a name comes from an I2 term shared by all tiers of a trophy, append the tier numeral (I, II, III) so each tier is a distinct achievement on the stores. Ignored for a trophy with a single tier.")]
        public bool AppendTierNumeral = true;

        [Tooltip("Default Steam unlock model for every entry. Stat Threshold = game only writes the stat and Steamworks auto-unlocks the achievement at its threshold (legacy Storm Tale 2 model); Explicit Achievement = game calls SetAchievement with SteamAchievement (or the stat name). Defaults to Explicit Achievement to keep pre-existing maps behaving exactly as before.")]
        public SteamUnlockMode SteamDefaultUnlockMode = SteamUnlockMode.ExplicitAchievement;

        [Tooltip("Default for every entry. True = Report writes the absolute counter value to the Steam stat (legacy model); false = Report adds the reported delta to the stat.")]
        public bool SteamSetStatAbsolute = false;

        /// <summary>Default I2 term for a trophy's name (shared by its tiers), matching the convention <c>trophy{N}</c>.</summary>
        public static string DefaultNameTerm(int trophyNumber) => $"trophy{trophyNumber}";

        /// <summary>Default I2 term generated for a tier's "earned" text.</summary>
        public static string DefaultEarnedTerm(int trophyNumber, int tier) => $"Achievements/Trophy{trophyNumber}_{tier}/Earned";

        /// <summary>Default I2 term generated for a tier's "not earned yet" hint text.</summary>
        public static string DefaultNotEarnedTerm(int trophyNumber, int tier) => $"Achievements/Trophy{trophyNumber}_{tier}/NotEarned";

        /// <summary>1 → "I", 2 → "II", 4 → "IV"... Falls back to the plain number above 3999 or for values below 1.</summary>
        public static string RomanNumeral(int value)
        {
            if (value < 1 || value > 3999) return value.ToString();

            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (value >= values[i])
                {
                    value -= values[i];
                    result.Append(symbols[i]);
                }
            }
            return result.ToString();
        }

        /// <summary>How many tiers the given trophy has in this map.</summary>
        public int TierCount(int trophyNumber) => Entries.Count(e => e.TrophyNumber == trophyNumber);

        /// <summary>
        /// The canonical <c>NativeSocial</c> LocId for one tier, shared by every map built from this asset and
        /// by any <c>NativeSocial.Report</c>/<c>SyncCompleted</c> call site — define it once here so the game
        /// and the maps never drift apart on the string format.
        /// </summary>
        public static string LocId(int trophyNumber, int tier) => $"Trophy{trophyNumber}_{tier}";

        public bool TryGetEntry(int trophyNumber, int tier, out AchievementTierEntry entry)
        {
            foreach (var e in Entries)
            {
                if (e.TrophyNumber != trophyNumber || e.Tier != tier) continue;
                entry = e;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>LocId -> Google Play achievement ID, for entries with a non-empty <see cref="AchievementTierEntry.GooglePlayId"/>.</summary>
        public Dictionary<string, string> BuildAndroidMap() =>
            Entries.Where(e => !string.IsNullOrEmpty(e.GooglePlayId))
                .ToDictionary(e => LocId(e.TrophyNumber, e.Tier), e => e.GooglePlayId);

        /// <summary>LocId -> Apple Game Center achievement ID, for entries with a non-empty <see cref="AchievementTierEntry.AppleId"/>.</summary>
        public Dictionary<string, string> BuildIosMap() =>
            Entries.Where(e => !string.IsNullOrEmpty(e.AppleId))
                .ToDictionary(e => LocId(e.TrophyNumber, e.Tier), e => e.AppleId);

        /// <summary>
        /// LocId -> <see cref="SteamEntry"/> for entries with a non-empty <see cref="AchievementTierEntry.SteamStat"/>,
        /// honoring <see cref="SteamDefaultUnlockMode"/>/<see cref="SteamSetStatAbsolute"/> and the optional
        /// per-entry <see cref="AchievementTierEntry.SteamAchievement"/> name. In <see cref="SteamUnlockMode.StatThreshold"/>
        /// the achievement name is left empty so <c>NativeSocial.Report</c> never calls SetAchievement (Steamworks
        /// unlocks it from the stat threshold). Pass a custom map to <c>NativeSocial.Initialize</c> for anything more exotic.
        /// </summary>
        public Dictionary<string, SteamEntry> BuildSteamMap() =>
            Entries.Where(e => !string.IsNullOrEmpty(e.SteamStat))
                .ToDictionary(
                    e => LocId(e.TrophyNumber, e.Tier),
                    e => new SteamEntry(
                        stat: e.SteamStat,
                        achievement: SteamDefaultUnlockMode == SteamUnlockMode.StatThreshold
                            ? string.Empty
                            : string.IsNullOrEmpty(e.SteamAchievement) ? e.SteamStat : e.SteamAchievement,
                        mode: SteamDefaultUnlockMode,
                        setStatAbsolute: SteamSetStatAbsolute));

        /// <summary>Number of entries missing a Google Play ID — for a Setup Audit "N of M achievement IDs still need to be filled in" check.</summary>
        public int CountMissingGooglePlay() => Entries.Count(e => string.IsNullOrEmpty(e.GooglePlayId));

        /// <summary>Number of entries missing an Apple Game Center ID — for a Setup Audit "N of M achievement IDs still need to be filled in" check.</summary>
        public int CountMissingApple() => Entries.Count(e => string.IsNullOrEmpty(e.AppleId));
    }
}
