using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEditor;

namespace Wagenheimer.NativeSocial.Editor
{
    internal enum TextSource
    {
        /// <summary>No term translation and no literal text: nothing to show/export.</summary>
        Missing,

        /// <summary>The literal fallback field on the entry (I2 unavailable, term empty, or term has no translation).</summary>
        Literal,

        /// <summary>Resolved from an I2 Localization term.</summary>
        I2
    }

    internal struct ResolvedText
    {
        public string Text;
        public TextSource Source;

        /// <summary>The I2 term key configured on the entry (may be empty).</summary>
        public string Term;

        /// <summary>A term key is configured but I2 has no translation for it (term missing, or empty in this language).</summary>
        public bool TermUnresolved;

        public bool IsOk => Source != TextSource.Missing;
    }

    /// <summary>
    /// Turns an <see cref="AchievementTierEntry"/>'s I2 term keys (with literal fallbacks) into the actual
    /// name/earned/not-earned strings for a given I2 language — used by the editor UI preview and by the
    /// AppDeployHub exporter, so both always agree. I2 is optional: without it everything falls back to the
    /// literal fields, exactly like before this feature existed.
    /// </summary>
    internal static class AchievementTextResolver
    {
        public static ResolvedText Resolve(string term, string literal, string language)
        {
            var result = new ResolvedText { Term = term };

            if (!string.IsNullOrEmpty(term) && I2Bridge.IsAvailable)
            {
                var text = I2Bridge.GetTranslation(term, language);
                if (text == null && language != I2Bridge.DefaultLanguage)
                    text = I2Bridge.GetTranslation(term, I2Bridge.DefaultLanguage);

                if (text != null)
                {
                    result.Text = text;
                    result.Source = TextSource.I2;
                    return result;
                }
                result.TermUnresolved = true;
            }

            if (!string.IsNullOrEmpty(literal))
            {
                result.Text = literal;
                result.Source = TextSource.Literal;
                return result;
            }

            result.Source = TextSource.Missing;
            return result;
        }

        public static ResolvedText Name(AchievementTierMap map, AchievementTierEntry entry, string language)
        {
            var resolved = Resolve(entry.NameTerm, entry.DisplayName, language);
            if (resolved.Source == TextSource.I2 && map.AppendTierNumeral && map.TierCount(entry.TrophyNumber) > 1)
                resolved.Text += " " + AchievementTierMap.RomanNumeral(entry.Tier);
            return resolved;
        }

        public static ResolvedText Earned(AchievementTierEntry entry, string language) =>
            Resolve(entry.EarnedDescriptionTerm, entry.EarnedDescription, language);

        public static ResolvedText NotEarned(AchievementTierEntry entry, string language) =>
            Resolve(entry.NotEarnedDescriptionTerm, entry.NotEarnedDescription, language);
    }

    /// <summary>Bulk I2 term maintenance for an <see cref="AchievementTierMap"/>: default keys + generating the missing terms.</summary>
    internal static class AchievementI2Tools
    {
        /// <summary>Fills every empty term-key field with the conventional default. Returns how many fields were set.</summary>
        public static int FillDefaultTermKeys(AchievementTierMap map)
        {
            Undo.RecordObject(map, "Fill default I2 term keys");
            int changed = 0;
            for (int i = 0; i < map.Entries.Count; i++)
            {
                var e = map.Entries[i];
                if (string.IsNullOrEmpty(e.NameTerm)) { e.NameTerm = AchievementTierMap.DefaultNameTerm(e.TrophyNumber); changed++; }
                if (string.IsNullOrEmpty(e.EarnedDescriptionTerm)) { e.EarnedDescriptionTerm = AchievementTierMap.DefaultEarnedTerm(e.TrophyNumber, e.Tier); changed++; }
                if (string.IsNullOrEmpty(e.NotEarnedDescriptionTerm)) { e.NotEarnedDescriptionTerm = AchievementTierMap.DefaultNotEarnedTerm(e.TrophyNumber, e.Tier); changed++; }
                map.Entries[i] = e;
            }
            if (changed > 0) EditorUtility.SetDirty(map);
            return changed;
        }

        /// <summary>Every configured term that I2 doesn't know yet, with the English text it would be seeded with.</summary>
        public static List<KeyValuePair<string, string>> FindMissingTerms(AchievementTierMap map)
        {
            var missing = new Dictionary<string, string>();
            if (!I2Bridge.IsAvailable) return missing.ToList();

            void Consider(string term, string seed)
            {
                if (string.IsNullOrEmpty(term) || missing.ContainsKey(term) || I2Bridge.TermExists(term)) return;
                missing[term] = seed ?? string.Empty;
            }

            foreach (var e in map.Entries)
            {
                // A trophy's shared description term ("trophy{N}description") is the best English seed when the
                // entry has no literal text of its own.
                var sharedDescription = I2Bridge.GetTranslation($"trophy{e.TrophyNumber}description", I2Bridge.DefaultLanguage);

                Consider(e.NameTerm, StripTrailingNumeral(e.DisplayName));
                Consider(e.EarnedDescriptionTerm, FirstNonEmpty(e.EarnedDescription, sharedDescription));
                Consider(e.NotEarnedDescriptionTerm, FirstNonEmpty(e.NotEarnedDescription, sharedDescription));
            }
            return missing.ToList();
        }

        /// <summary>Creates the missing terms (English text only — other languages stay empty for I2's own translation tools). Returns how many were created.</summary>
        public static int GenerateMissingTerms(AchievementTierMap map)
        {
            var missing = FindMissingTerms(map);
            int created = 0;
            foreach (var pair in missing)
            {
                I2Bridge.EnsureTerm(pair.Key, pair.Value, out var termCreated);
                if (termCreated) created++;
            }
            if (missing.Count > 0) I2Bridge.SaveSources();
            return created;
        }

        private static string FirstNonEmpty(string a, string b) => !string.IsNullOrEmpty(a) ? a : b;

        private static string StripTrailingNumeral(string name) =>
            string.IsNullOrEmpty(name) ? name : Regex.Replace(name, @"\s+[IVXLCDM]+$", string.Empty);
    }
}
