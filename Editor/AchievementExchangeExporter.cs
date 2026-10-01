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
        internal class ExchangeLocalization
        {
            public string locale;
            public string name;
            public string earnedDescription;
            public string notEarnedDescription;
        }

        [Serializable]
        internal class ExchangeEntry
        {
            public string key;
            public string displayName;
            public string earnedDescription;
            public string notEarnedDescription;
            public int points;
            public bool isHidden;
            public bool isIncremental;
            public int stepsToUnlock;
            public string steamStat;
            public string googlePlayId;
            public string appleId;

            /// <summary>Extra translations beyond the primary-locale texts above (optional, additive in the v1 format).</summary>
            public List<ExchangeLocalization> localizations;
        }

        [Serializable]
        internal class ExchangeFile
        {
            public string formatId;
            public string gameName;
            public string locale;
            public List<ExchangeEntry> entries;
        }

        /// <summary>Builds the exchange entries for every row in <paramref name="map"/>, in file order, with texts resolved for <paramref name="language"/> (an I2 language name; defaults to English).</summary>
        internal static List<ExchangeEntry> BuildEntries(AchievementTierMap map, string language = null)
        {
            language = string.IsNullOrEmpty(language) ? I2Bridge.DefaultLanguage : language;
            return map.Entries.Select(e =>
            {
                var earned = AchievementTextResolver.Earned(e, language);
                var notEarned = AchievementTextResolver.NotEarned(e, language);
                return new ExchangeEntry
                {
                    localizations = BuildLocalizations(map, e, language),
                    key = AchievementTierMap.LocId(e.TrophyNumber, e.Tier),
                    displayName = AchievementTextResolver.Name(map, e, language).Text ?? string.Empty,
                    earnedDescription = string.IsNullOrEmpty(earned.Text) ? null : earned.Text,
                    notEarnedDescription = string.IsNullOrEmpty(notEarned.Text) ? null : notEarned.Text,
                    points = e.Points,
                    isHidden = e.IsHidden,
                    isIncremental = e.IsIncremental,
                    stepsToUnlock = e.StepsToUnlock,
                    steamStat = string.IsNullOrEmpty(e.SteamStat) ? null : e.SteamStat,
                    googlePlayId = string.IsNullOrEmpty(e.GooglePlayId) ? null : e.GooglePlayId,
                    appleId = string.IsNullOrEmpty(e.AppleId) ? null : e.AppleId
                };
            }).ToList();
        }

        /// <summary>
        /// Every OTHER I2 language that has a real translation of this tier's name, strictly (no English fallback:
        /// English text labeled as pt-BR would be worse than no pt-BR at all). Empty without I2.
        /// </summary>
        internal static List<ExchangeLocalization> BuildLocalizations(AchievementTierMap map, AchievementTierEntry entry, string primaryLanguage)
        {
            var result = new List<ExchangeLocalization>();
            if (!I2Bridge.IsAvailable) return result;

            var seenLocales = new HashSet<string> { LocaleFor(primaryLanguage) };
            foreach (var language in I2Bridge.GetLanguages())
            {
                if (language == primaryLanguage) continue;

                var name = AchievementTextResolver.Name(map, entry, language, allowEnglishFallback: false);
                if (name.Source != TextSource.I2) continue;

                var locale = LocaleFor(language);
                if (!seenLocales.Add(locale)) continue; // two I2 languages mapping to one store locale: first wins

                var earned = AchievementTextResolver.Earned(entry, language, allowEnglishFallback: false);
                var notEarned = AchievementTextResolver.NotEarned(entry, language, allowEnglishFallback: false);
                result.Add(new ExchangeLocalization
                {
                    locale = locale,
                    name = name.Text,
                    earnedDescription = earned.Source == TextSource.I2 ? earned.Text : null,
                    notEarnedDescription = notEarned.Source == TextSource.I2 ? notEarned.Text : null
                });
            }
            return result;
        }

        /// <summary>BCP-47 style locale for the exchange file from an I2 language name/code. Falls back to "en-US".</summary>
        internal static string LocaleFor(string language)
        {
            var code = I2Bridge.IsAvailable ? I2Bridge.GetLanguageCode(language) : null;
            if (string.IsNullOrEmpty(code)) return "en-US";
            if (code.Contains("-")) return code;

            switch (code.ToLowerInvariant())
            {
                case "en": return "en-US";
                case "pt": return "pt-BR";
                case "de": return "de-DE";
                case "fr": return "fr-FR";
                case "es": return "es-ES";
                case "it": return "it-IT";
                case "nl": return "nl-NL";
                case "ru": return "ru-RU";
                case "pl": return "pl-PL";
                case "cs": return "cs-CZ";
                case "ja": return "ja-JP";
                case "zh": return "zh-CN";
                default: return code;
            }
        }

        /// <summary>Serializes <paramref name="map"/> to the exchange JSON text (no file I/O — used directly by tests).</summary>
        internal static string BuildJson(AchievementTierMap map, string gameName, string locale, string language = null)
        {
            var file = new ExchangeFile
            {
                formatId = FormatId,
                gameName = gameName,
                locale = locale,
                entries = BuildEntries(map, language)
            };
            return JsonUtility.ToJson(file, prettyPrint: true);
        }

        /// <summary>"Export for AppDeployHub" button entry point: prompts for a save path and writes the file.</summary>
        public static void ExportToFile(AchievementTierMap map, string gameName, string language = null)
        {
            if (map == null || map.Entries == null || map.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to export", "This AchievementTierMap has no entries.", "OK");
                return;
            }

            language = string.IsNullOrEmpty(language) ? I2Bridge.DefaultLanguage : language;
            var missingNames = map.Entries.Count(e => string.IsNullOrEmpty(AchievementTextResolver.Name(map, e, language).Text));
            if (missingNames > 0 &&
                !EditorUtility.DisplayDialog("Missing display names",
                    $"{missingNames} of {map.Entries.Count} entries have no name (neither an I2 translation in '{language}' nor a literal DisplayName). They will still be " +
                    "exported (with an empty name) — AppDeployHub's import expects one, so fill it in first if possible. Export anyway?",
                    "Export anyway", "Cancel"))
            {
                return;
            }

            var defaultName = string.IsNullOrEmpty(gameName) ? "achievements" : gameName;
            var path = EditorUtility.SaveFilePanel("Export achievements for AppDeployHub", "", $"{defaultName}-achievements.json", "json");
            if (string.IsNullOrEmpty(path))
                return;

            File.WriteAllText(path, BuildJson(map, gameName, LocaleFor(language), language));
            Debug.Log($"[NativeSocial] Exported {map.Entries.Count} achievement tier(s) to '{path}' ({language}) for AppDeployHub import.");
            EditorUtility.RevealInFinder(path);
        }
    }
}
