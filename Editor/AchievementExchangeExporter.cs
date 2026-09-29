using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Exports an <see cref="AchievementTierMap"/> asset to the versioned JSON exchange format
    /// AppDeployHub's Achievements import feature reads
    /// (<c>appdeployhub-achievements/v1</c> — see AppDeployHub's
    /// <c>AppDeployHub.Domain.Models.AchievementExchange</c>, the two must stay in sync since there
    /// is no shared package between the two repos). Editor-only: file dialogs and JSON writing have
    /// no place in the Runtime assembly.
    /// </summary>
    public static class AchievementExchangeExporter
    {
        private const string FormatId = "appdeployhub-achievements/v1";

        [Serializable]
        internal class ExchangeEntry
        {
            public string key;
            public string displayName;
            public string earnedDescription;
            public string notEarnedDescription;
            public int points;
            public bool isHidden;
            public string steamStat;
            public string googlePlayId;
            public string appleId;
        }

        [Serializable]
        internal class ExchangeFile
        {
            public string formatId;
            public string gameName;
            public string locale;
            public List<ExchangeEntry> entries;
        }

        /// <summary>Builds the exchange entries for every row in <paramref name="map"/>, in file order.</summary>
        internal static List<ExchangeEntry> BuildEntries(AchievementTierMap map) =>
            map.Entries.Select(e => new ExchangeEntry
            {
                key = AchievementTierMap.LocId(e.TrophyNumber, e.Tier),
                displayName = e.DisplayName ?? string.Empty,
                earnedDescription = string.IsNullOrEmpty(e.EarnedDescription) ? null : e.EarnedDescription,
                notEarnedDescription = string.IsNullOrEmpty(e.NotEarnedDescription) ? null : e.NotEarnedDescription,
                points = e.Points,
                isHidden = e.IsHidden,
                steamStat = string.IsNullOrEmpty(e.SteamStat) ? null : e.SteamStat,
                googlePlayId = string.IsNullOrEmpty(e.GooglePlayId) ? null : e.GooglePlayId,
                appleId = string.IsNullOrEmpty(e.AppleId) ? null : e.AppleId
            }).ToList();

        /// <summary>Serializes <paramref name="map"/> to the exchange JSON text (no file I/O — used directly by tests).</summary>
        internal static string BuildJson(AchievementTierMap map, string gameName, string locale)
        {
            var file = new ExchangeFile
            {
                formatId = FormatId,
                gameName = gameName,
                locale = locale,
                entries = BuildEntries(map)
            };
            return JsonUtility.ToJson(file, prettyPrint: true);
        }

        /// <summary>"Export for AppDeployHub" button entry point: prompts for a save path and writes the file.</summary>
        public static void ExportToFile(AchievementTierMap map, string gameName, string locale = "en-US")
        {
            if (map == null || map.Entries == null || map.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to export", "This AchievementTierMap has no entries.", "OK");
                return;
            }

            var missingNames = map.Entries.Count(e => string.IsNullOrEmpty(e.DisplayName));
            if (missingNames > 0 &&
                !EditorUtility.DisplayDialog("Missing display names",
                    $"{missingNames} of {map.Entries.Count} entries have no DisplayName set. They will still be " +
                    "exported (with an empty name) — AppDeployHub's import expects one, so fill it in first if possible. Export anyway?",
                    "Export anyway", "Cancel"))
            {
                return;
            }

            var defaultName = string.IsNullOrEmpty(gameName) ? "achievements" : gameName;
            var path = EditorUtility.SaveFilePanel("Export achievements for AppDeployHub", "", $"{defaultName}-achievements.json", "json");
            if (string.IsNullOrEmpty(path))
                return;

            File.WriteAllText(path, BuildJson(map, gameName, locale));
            Debug.Log($"[NativeSocial] Exported {map.Entries.Count} achievement tier(s) to '{path}' for AppDeployHub import.");
            EditorUtility.RevealInFinder(path);
        }
    }
}
