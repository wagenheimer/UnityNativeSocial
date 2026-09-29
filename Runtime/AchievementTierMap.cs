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

        [Tooltip("Google Play Games achievement ID for this tier (from the Google Play Console), or empty until that console listing exists.")]
        public string GooglePlayId;

        [Tooltip("Apple Game Center achievement ID for this tier (from App Store Connect), or empty until that console listing exists.")]
        public string AppleId;

        [Tooltip("Player-facing display name for this tier (e.g. from your localization system). Only used by the \"Export for AppDeployHub\" button — not read by NativeSocial itself.")]
        public string DisplayName;

        [Tooltip("Player-facing description shown once the achievement is earned. Only used by the \"Export for AppDeployHub\" button.")]
        public string EarnedDescription;

        [Tooltip("Player-facing description shown before the achievement is earned (hint/goal text). Only used by the \"Export for AppDeployHub\" button.")]
        public string NotEarnedDescription;

        [Tooltip("Points awarded for this tier on Google Play / Apple Game Center. Only used by the \"Export for AppDeployHub\" button.")]
        public int Points;

        [Tooltip("Hidden until earned, on Google Play / Apple Game Center. Only used by the \"Export for AppDeployHub\" button.")]
        public bool IsHidden;
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

        /// <summary>LocId -> Steam stat/achievement pair, for entries with a non-empty <see cref="AchievementTierEntry.SteamStat"/>. The Steamworks achievement API name is assumed to equal the stat name; pass a custom map to <c>NativeSocial.Initialize</c> instead if a game's Steam achievement names differ from its stat names.</summary>
        public Dictionary<string, SteamEntry> BuildSteamMap() =>
            Entries.Where(e => !string.IsNullOrEmpty(e.SteamStat))
                .ToDictionary(e => LocId(e.TrophyNumber, e.Tier), e => new SteamEntry(e.SteamStat, e.SteamStat));

        /// <summary>Number of entries missing a Google Play ID — for a Setup Audit "N of M achievement IDs still need to be filled in" check.</summary>
        public int CountMissingGooglePlay() => Entries.Count(e => string.IsNullOrEmpty(e.GooglePlayId));

        /// <summary>Number of entries missing an Apple Game Center ID — for a Setup Audit "N of M achievement IDs still need to be filled in" check.</summary>
        public int CountMissingApple() => Entries.Count(e => string.IsNullOrEmpty(e.AppleId));
    }
}
