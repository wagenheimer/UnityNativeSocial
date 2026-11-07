using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Writes everything Steamworks needs for an <see cref="AchievementTierMap"/> into one folder: the two
    /// CSV files and the icons accepted by the achievements editor's Bulk Import/Export tab, plus the list of
    /// stats, which Steamworks has no bulk import for and must be created by hand first.
    /// See https://partner.steamgames.com/doc/features/achievements/ach_guide
    /// </summary>
    public static class SteamPackExporter
    {
        private const string Permission = "Client";
        private const float UnachievedBrightness = 0.6f;

        // Steam's language API codes. A language missing here is skipped and reported in the README.
        private static readonly Dictionary<string, string> SteamLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "English", "english" },
            { "German", "german" },
            { "Spanish", "spanish" },
            { "French", "french" },
            { "Italian", "italian" },
            { "Dutch", "dutch" },
            { "Portuguese (Brasil)", "brazilian" },
            { "Portuguese (Brazil)", "brazilian" },
            { "Portuguese", "portuguese" },
            { "Japanese", "japanese" },
            { "Russian", "russian" },
            { "Polish", "polish" },
            { "Czech", "czech" },
            { "Korean", "koreana" },
            { "Chinese", "schinese" },
            { "Chinese (Simplified)", "schinese" },
            { "Chinese (Traditional)", "tchinese" },
            { "Turkish", "turkish" },
        };

        /// <summary>Summary of one export, for the dialog and for callers that verify it.</summary>
        public struct Result
        {
            public string Folder;
            public int Achievements;
            public int Stats;
            public int Icons;
            public List<string> MissingIcons;
            public List<string> SkippedLanguages;
        }

        /// <summary>Asks for a folder and exports the pack there.</summary>
        public static void ExportWithDialog(AchievementTierMap map)
        {
            var folder = EditorUtility.SaveFolderPanel("Export Steam achievements pack", "", "SteamAchievements");
            if (string.IsNullOrEmpty(folder)) return;

            var result = Export(map, folder);
            var message = $"{result.Achievements} achievements, {result.Stats} stats and {result.Icons} icons exported to:\n{result.Folder}\n\nFollow README.md there.";
            if (result.MissingIcons.Count > 0)
                message += $"\n\nMissing icons: {string.Join(", ", result.MissingIcons)}";
            EditorUtility.DisplayDialog("Steam pack exported", message, "OK");
            EditorUtility.RevealInFinder(Path.Combine(result.Folder, "README.md"));
        }

        /// <summary>Writes the pack. Existing files with the same names are replaced; nothing else is deleted.</summary>
        public static Result Export(AchievementTierMap map, string folder)
        {
            var entries = map.Entries.Where(entry => !string.IsNullOrEmpty(entry.SteamAchievement)).ToList();
            var languages = I2Bridge.IsAvailable ? I2Bridge.GetLanguages() : new List<string> { I2Bridge.DefaultLanguage };
            var skipped = languages.Where(language => !SteamLanguages.ContainsKey(language)).ToList();
            languages = languages.Where(SteamLanguages.ContainsKey).ToList();

            Directory.CreateDirectory(folder);
            var iconsFolder = Path.Combine(folder, "icons");
            Directory.CreateDirectory(iconsFolder);

            var prefix = string.IsNullOrWhiteSpace(map.SteamAppId) ? string.Empty : map.SteamAppId.Trim() + "-";
            Write(Path.Combine(folder, $"{prefix}achievements-definitions.csv"), BuildDefinitions(entries));
            Write(Path.Combine(folder, $"{prefix}achievements-localization.csv"), BuildLocalization(map, entries, languages));

            var stats = BuildStats(entries);
            Write(Path.Combine(folder, "stats-to-create.csv"), BuildStatsCsv(stats));

            var missingIcons = new List<string>();
            var icons = 0;
            foreach (var entry in entries)
            {
                var source = string.IsNullOrEmpty(map.IconFolder) ? null : $"{map.IconFolder}/{entry.SteamAchievement}.png";
                if (source == null || !File.Exists(source))
                {
                    missingIcons.Add(entry.SteamAchievement);
                    continue;
                }

                File.Copy(source, Path.Combine(iconsFolder, $"{entry.SteamAchievement}_ACHIEVED.png"), true);
                File.WriteAllBytes(Path.Combine(iconsFolder, $"{entry.SteamAchievement}_UNACHIEVED.png"), BuildUnachievedIcon(source));
                icons++;
            }

            Write(Path.Combine(folder, "README.md"), BuildReadme(entries.Count, stats, icons, missingIcons, skipped, prefix));

            return new Result
            {
                Folder = folder,
                Achievements = entries.Count,
                Stats = stats.Count,
                Icons = icons,
                MissingIcons = missingIcons,
                SkippedLanguages = skipped
            };
        }

        /// <summary>Darkened grayscale copy of the icon, keeping its transparency.</summary>
        private static byte[] BuildUnachievedIcon(string source)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.LoadImage(File.ReadAllBytes(source));
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var gray = (byte)(((pixels[i].r * 77 + pixels[i].g * 150 + pixels[i].b * 29) >> 8) * UnachievedBrightness);
                    pixels[i] = new Color32(gray, gray, gray, pixels[i].a);
                }

                texture.SetPixels32(pixels);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static string BuildDefinitions(List<AchievementTierEntry> entries)
        {
            var csv = new StringBuilder();
            csv.AppendLine("api_name,setid,permission,spoiler,archived,progress_stat_name,progress_stat_min,progress_stat_max");
            foreach (var entry in entries)
            {
                var hasProgress = !string.IsNullOrEmpty(entry.SteamStat) && entry.StepsToUnlock > 1;
                csv.AppendLine(string.Join(",",
                    Cell(entry.SteamAchievement),
                    string.Empty,
                    Permission,
                    entry.IsHidden ? "true" : "false",
                    "false",
                    hasProgress ? Cell(entry.SteamStat) : string.Empty,
                    hasProgress ? "0" : string.Empty,
                    hasProgress ? entry.StepsToUnlock.ToString() : string.Empty));
            }

            return csv.ToString();
        }

        private static string BuildLocalization(AchievementTierMap map, List<AchievementTierEntry> entries, List<string> languages)
        {
            var csv = new StringBuilder();
            csv.AppendLine("api_name,field," + string.Join(",", languages.Select(language => SteamLanguages[language])));
            foreach (var entry in entries)
            {
                csv.AppendLine(Cell(entry.SteamAchievement) + ",name," + string.Join(",", languages.Select(language =>
                    Cell(AchievementTextResolver.Name(map, entry, language).Text))));
                csv.AppendLine(Cell(entry.SteamAchievement) + ",description," + string.Join(",", languages.Select(language =>
                    Cell(AchievementTextResolver.Earned(entry, language).Text))));
            }

            return csv.ToString();
        }

        /// <summary>Stat name to the largest progress target that reads it, in first-seen order.</summary>
        internal static Dictionary<string, int> BuildStats(List<AchievementTierEntry> entries)
        {
            var stats = new Dictionary<string, int>();
            foreach (var entry in entries.Where(item => !string.IsNullOrEmpty(item.SteamStat)))
            {
                stats.TryGetValue(entry.SteamStat, out var largest);
                stats[entry.SteamStat] = Math.Max(largest, entry.StepsToUnlock);
            }

            return stats;
        }

        private static string BuildStatsCsv(Dictionary<string, int> stats)
        {
            var csv = new StringBuilder();
            csv.AppendLine("api_name,type,set_by,increment_only,min_value,default_value,display_name,largest_progress_target");
            foreach (var stat in stats)
                csv.AppendLine($"{Cell(stat.Key)},INT,{Permission},true,0,0,{Cell(stat.Key)},{stat.Value}");

            return csv.ToString();
        }

        private static string BuildReadme(int achievements, Dictionary<string, int> stats, int icons, List<string> missingIcons, List<string> skipped, string prefix)
        {
            var text = new StringBuilder();
            text.AppendLine("# Steam achievements pack");
            text.AppendLine();
            text.AppendLine($"{achievements} achievements, {stats.Count} stats, {icons} icons.");
            text.AppendLine();
            text.AppendLine("## 1. Create the stats by hand");
            text.AppendLine();
            text.AppendLine("Steamworks has no bulk import for stats, and progress stats must exist before the import.");
            text.AppendLine("In Steamworks > Edit Steamworks Settings > Stats & Achievements > Stats, create each row of `stats-to-create.csv`:");
            text.AppendLine();
            text.AppendLine("| API Name | Type | Set By | Increment Only | Min | Default | Max |");
            text.AppendLine("|---|---|---|---|---:|---:|---|");
            foreach (var stat in stats)
                text.AppendLine($"| `{stat.Key}` | INT | {Permission} | yes | 0 | 0 | leave empty (must stay at or above {stat.Value}) |");
            text.AppendLine();
            text.AppendLine("## 2. Import the achievements");
            text.AppendLine();
            text.AppendLine("1. Open Stats & Achievements > Achievements, tab **Bulk Import/Export**.");
            text.AppendLine($"2. Use **Configure Languages** so the languages match the columns of `{prefix}achievements-localization.csv`.");
            text.AppendLine($"3. Drag `{prefix}achievements-definitions.csv`, `{prefix}achievements-localization.csv` and every file in `icons/` onto the upload box.");
            text.AppendLine("4. Do **not** tick **Delete these achievements**.");
            text.AppendLine("5. Click **Save imported data**.");
            text.AppendLine();
            text.AppendLine("## 3. Publish");
            text.AppendLine();
            text.AppendLine("The import only changes the draft. Publish it from the **Publish** tab in Edit Steamworks Settings.");
            if (missingIcons.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"## Missing icons\n\n{string.Join(", ", missingIcons)}");
            }

            if (skipped.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"## Languages without a known Steam code (not exported)\n\n{string.Join(", ", skipped)}");
            }

            return text.ToString();
        }

        /// <summary>UTF-8 without BOM: a BOM would become part of the first column name.</summary>
        private static void Write(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string Cell(string value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
