using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEngine;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Level Up / Game Stats checks (LU-GS-GAA / LU-GS-GAB): validates the local schema CSVs, the icons and the
    /// code that sends the events. The Play Console itself cannot be read from the Editor, so publication is
    /// reported as a manual step.
    ///
    /// Schema folder convention: the first of <see cref="GameStatsFolders"/> that contains PlayerGameEvent.csv.
    /// </summary>
    public static partial class NativeSocialAudit
    {
        private static readonly string[] GameStatsFolders =
        {
            "docs/GameStats",
            "Documentation/GameStats",
            "Assets/GameStats",
            "StoreAssets/GameStats"
        };

        private static readonly Regex IdentifierPattern = new Regex("^[A-Za-z0-9_]{1,100}$", RegexOptions.Compiled);

        private static readonly HashSet<string> ReservedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "count", "key", "value", "where", "from", "and", "or", "true", "false"
        };

        private static readonly HashSet<string> PropertyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INT64", "DOUBLE", "STRING", "BOOL"
        };

        private static readonly HashSet<string> Aggregations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SUM", "MAX", "MIN", "COUNT"
        };

        private static void AuditGameStats(List<AuditResult> results)
        {
            var folder = GameStatsFolders.FirstOrDefault(d => File.Exists(Path.Combine(d, "PlayerGameEvent.csv")));
            if (folder == null)
            {
                Add(results, CategoryAndroid, "Game Stats (Level Up)", false,
                    string.Empty,
                    "No Game Stats schema found (PlayerGameEvent.csv). Level Up LU-GS-GAA/GAB needs 5 repetitive stats and 1 progression stat.",
                    "Create a GameStats folder with PlayerGameEvent.csv, RepetitiveStatsConfig.csv, ProgressionStatConfig.csv and the 512x512 icons, then import them in Play Console.",
                    failSeverity: AuditSeverity.Info,
                    whatIsThis: "Game Stats are the player stats shown on the Gamer profile and used by Leagues, Quests and Sprints. The console schema and the code must declare identical event/property names.");
                return;
            }

            var events = ReadCsv(Path.Combine(folder, "PlayerGameEvent.csv"));
            var repetitive = ReadCsv(Path.Combine(folder, "RepetitiveStatsConfig.csv"));
            var progression = ReadCsv(Path.Combine(folder, "ProgressionStatConfig.csv"));

            const int issueLimit = 6;
            var eventNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var eventProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Player events (raw schema).
            var eventIssues = new List<string>();
            if (events == null || events.Count == 0)
            {
                eventIssues.Add("PlayerGameEvent.csv is empty or missing.");
            }
            else
            {
                foreach (var row in events)
                {
                    var eventName = Value(row, "Event Name");
                    var propertyName = Value(row, "Property Name");
                    var propertyType = Value(row, "Property Type");

                    if (!IdentifierPattern.IsMatch(eventName))
                        eventIssues.Add($"event '{eventName}' must use only letters, numbers and underscores.");
                    if (!IdentifierPattern.IsMatch(propertyName))
                        eventIssues.Add($"property '{propertyName}' of '{eventName}' must use only letters, numbers and underscores.");
                    if (ReservedPropertyNames.Contains(propertyName))
                        eventIssues.Add($"property '{eventName}.{propertyName}' uses a reserved keyword.");
                    if (!PropertyTypes.Contains(propertyType))
                        eventIssues.Add($"'{eventName}.{propertyName}' has type '{propertyType}' (use INT64, DOUBLE, STRING or BOOL).");

                    eventNames.Add(eventName);
                    eventProperties.Add(eventName + "|" + propertyName);
                }

                if (!eventProperties.Contains("progressUpdate|currentProgress"))
                    eventIssues.Add("progressUpdate must declare a currentProgress property (INT64 or STRING).");
            }

            Add(results, CategoryAndroid, "Game Stats events schema", eventIssues.Count == 0,
                $"{eventNames.Count} event(s), {eventProperties.Count} property(ies) in {folder}/PlayerGameEvent.csv.",
                string.Join(" ", eventIssues.Take(issueLimit)) + (eventIssues.Count > issueLimit ? " ..." : string.Empty),
                "Fix PlayerGameEvent.csv before importing it: Play Console rejects invalid names/types and a progressUpdate without currentProgress.",
                failSeverity: AuditSeverity.Fail,
                whatIsThis: "The raw event schema. Names are case-sensitive and must match the strings passed to NativeSocial.RecordGameEvent.");

            // 2. Repetitive stats (Level Up requires at least 5).
            var statIssues = new List<string>();
            var icons = new List<string>();
            var statIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (repetitive == null || repetitive.Count < 5)
                statIssues.Add($"RepetitiveStatsConfig.csv has {repetitive?.Count ?? 0} stat(s); Level Up requires at least 5.");

            if (repetitive != null)
            {
                foreach (var row in repetitive)
                {
                    var statId = Value(row, "Stat Id");
                    var eventName = Value(row, "Event Name");
                    var propertyName = Value(row, "Event Property Name");
                    var aggregation = Value(row, "Aggregation Type");
                    var icon = Value(row, "Icon File Name");
                    var filterProperty = Value(row, "Filter Property");
                    var competitive = Value(row, "Is Competitive");

                    if (!IdentifierPattern.IsMatch(statId)) statIssues.Add($"stat id '{statId}' is invalid.");
                    if (!statIds.Add(statId)) statIssues.Add($"stat id '{statId}' is duplicated.");
                    if (!Aggregations.Contains(aggregation)) statIssues.Add($"stat '{statId}' uses aggregation '{aggregation}' (use SUM, MAX, MIN or COUNT).");
                    if (!eventNames.Contains(eventName)) statIssues.Add($"stat '{statId}' references undeclared event '{eventName}'.");
                    if (!eventProperties.Contains(eventName + "|" + propertyName)) statIssues.Add($"stat '{statId}' references '{eventName}.{propertyName}', which is not declared.");
                    if (!string.IsNullOrEmpty(filterProperty) && !eventProperties.Contains(eventName + "|" + filterProperty))
                        statIssues.Add($"stat '{statId}' filters on undeclared property '{filterProperty}'.");

                    if (!bool.TryParse(competitive, out var isCompetitive))
                        statIssues.Add($"stat '{statId}' has Is Competitive='{competitive}' (use true/false).");
                    else if (isCompetitive && (string.IsNullOrWhiteSpace(Value(row, "Min limit")) || string.IsNullOrWhiteSpace(Value(row, "Max limit"))))
                        statIssues.Add($"competitive stat '{statId}' needs Min limit and Max limit.");

                    if (string.IsNullOrEmpty(icon)) statIssues.Add($"stat '{statId}' has no icon file name.");
                    else icons.Add(icon);
                }
            }

            Add(results, CategoryAndroid, "Game Stats repetitive stats (5+)", statIssues.Count == 0,
                $"{repetitive?.Count ?? 0} repetitive stats: {string.Join(", ", statIds)}.",
                string.Join(" ", statIssues.Take(issueLimit)) + (statIssues.Count > issueLimit ? " ..." : string.Empty),
                "Fix RepetitiveStatsConfig.csv: 5 stats, unique ids, declared events/properties, valid aggregation and min/max on competitive ones.",
                failSeverity: AuditSeverity.Fail,
                whatIsThis: "Cumulative stats Play computes from per-run events (SUM/COUNT/MAX). Competitive stats make the title eligible for Leagues and Sprints.");

            // 3. Progression stat (exactly one, backed by progressUpdate.currentProgress).
            var progressionIssues = new List<string>();
            if (progression == null || progression.Count != 1)
            {
                progressionIssues.Add($"ProgressionStatConfig.csv must contain exactly 1 stat (found {progression?.Count ?? 0}).");
            }
            else
            {
                var row = progression[0];
                var statId = Value(row, "Stat Id");
                var property = Value(row, "Event Property Name");
                var icon = Value(row, "Icon File Name");

                if (!IdentifierPattern.IsMatch(statId)) progressionIssues.Add($"stat id '{statId}' is invalid.");
                if (!statIds.Add(statId)) progressionIssues.Add($"stat id '{statId}' collides with a repetitive stat.");
                if (property != "currentProgress") progressionIssues.Add("progression must use currentProgress from the progressUpdate event.");
                if (string.IsNullOrEmpty(icon)) progressionIssues.Add("progression stat has no icon file name.");
                else icons.Add(icon);
            }

            Add(results, CategoryAndroid, "Game Stats progression stat", progressionIssues.Count == 0,
                $"Progression: {Value(progression?[0], "Stat Display Name")} (currentProgress).",
                string.Join(" ", progressionIssues.Take(issueLimit)),
                "ProgressionStatConfig.csv must define exactly one stat backed by progressUpdate.currentProgress; send the highest level reached, never a delta.",
                failSeverity: AuditSeverity.Fail,
                whatIsThis: "The level shown first on the Gamer profile. Replaying an old level must not move it backwards, so the code sends the max level reached.");

            // 4. Icons (512x512, <= 1 MB, no orphans).
            var iconIssues = new List<string>();
            var uniqueIcons = icons.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var icon in uniqueIcons)
            {
                var path = Path.Combine(folder, icon);
                if (!File.Exists(path))
                {
                    iconIssues.Add($"{icon} is missing from {folder}.");
                    continue;
                }

                if (new FileInfo(path).Length > 1024 * 1024) iconIssues.Add($"{icon} is larger than 1 MB.");
                if (!TryReadImageSize(path, out var width, out var height)) iconIssues.Add($"{icon} is not a readable PNG/JPEG.");
                else if (width != 512 || height != 512) iconIssues.Add($"{icon} is {width}x{height}; must be 512x512.");
            }

            var imageExtensions = new[] { ".png", ".jpg", ".jpeg" };
            var orphanIcons = Directory.Exists(folder)
                ? Directory.GetFiles(folder)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .Where(file => !uniqueIcons.Contains(file, StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();

            if (orphanIcons.Count > 0)
                iconIssues.Add("not referenced by any stat: " + string.Join(", ", orphanIcons.Take(issueLimit)));

            Add(results, CategoryAndroid, "Game Stats icons (512x512)", iconIssues.Count == 0,
                $"{uniqueIcons.Count} icon(s) present and 512x512.",
                string.Join(" ", iconIssues.Take(issueLimit)) + (iconIssues.Count > issueLimit ? " ..." : string.Empty),
                "Export each referenced icon as a 512x512 PNG/JPEG (max 1 MB) into the same folder, then put them at the root of the upload ZIP.",
                failSeverity: AuditSeverity.Warning,
                whatIsThis: "Play requires one 512x512 icon per stat, at the root of the ZIP, named exactly as in the CSVs.");

            // 5. Code sends the declared events.
            var code = AnalyzeProjectCode();
            var codeIssues = new List<string>();

            if (!code.HasGameStats || string.IsNullOrEmpty(code.GameStatsPath))
            {
                codeIssues.Add("no NativeSocial.RecordGameEvent call found in project code.");
            }
            else
            {
                string text = null;
                try { text = File.ReadAllText(code.GameStatsPath); } catch { }

                if (string.IsNullOrEmpty(text))
                {
                    codeIssues.Add($"could not read {code.GameStatsPath}.");
                }
                else
                {
                    foreach (var eventName in eventNames.Where(e => e != "progressUpdate").OrderBy(e => e))
                        if (!text.Contains("\"" + eventName + "\"")) codeIssues.Add($"'{eventName}' is declared but not sent by {Path.GetFileName(code.GameStatsPath)}.");

                    if (eventNames.Contains("progressUpdate") && !text.Contains("\"currentProgress\""))
                        codeIssues.Add("progressUpdate is declared but the code never sends currentProgress.");
                }
            }

            Add(results, CategoryAndroid, "Game Stats code events", codeIssues.Count == 0,
                $"Reporting detected in '{code.GameStatsPath}', covering the declared events.",
                string.Join(" ", codeIssues.Take(issueLimit)),
                "Send every declared event with NativeSocial.RecordGameEvent, using the exact event/property strings from PlayerGameEvent.csv.",
                failSeverity: AuditSeverity.Warning,
                whatIsThis: "A stat only updates when the code sends its event with the declared property names; nothing validates that at runtime.");

            // 6. Console reminder (cannot be read from the Editor).
            Add(results, CategoryAndroid, "Game Stats console publication", false,
                string.Empty,
                "Play Console cannot be read from the Editor. Import the events CSV and the stats ZIP in Play Games Services > Game Stats, then click Review and publish; until then the stats stay 'Available to testers'.",
                "Play Console > Grow users > Play Games Services > Setup and management > Game Stats.",
                "Open Play Console", () => Application.OpenURL("https://play.google.com/console"),
                AuditSeverity.Info,
                whatIsThis: "Local files can be perfect and the stats still not appear for players until the Play Games Services configuration is published.");
        }

        private static string Value(Dictionary<string, string> row, string column) =>
            row != null && row.TryGetValue(column, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

        /// <summary>Reads a CSV with a header row into dictionaries. Returns null when the file does not exist.</summary>
        private static List<Dictionary<string, string>> ReadCsv(string path)
        {
            if (!File.Exists(path)) return null;

            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return new List<Dictionary<string, string>>();

            var headers = SplitCsv(lines[0]);
            var rows = new List<Dictionary<string, string>>();

            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var values = SplitCsv(lines[i]);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var column = 0; column < headers.Count; column++)
                    row[headers[column]] = column < values.Count ? values[column] : string.Empty;

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Minimal CSV split: commas outside quotes, with "" as an escaped quote.</summary>
        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var character = line[i];

                if (quoted)
                {
                    if (character == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else quoted = false;
                    }
                    else current.Append(character);
                }
                else if (character == '"') quoted = true;
                else if (character == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
                else current.Append(character);
            }

            fields.Add(current.ToString().Trim());
            return fields;
        }

        /// <summary>Reads the pixel size from a PNG or JPEG header (no image decoding, keeps the audit cheap).</summary>
        private static bool TryReadImageSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                var bytes = File.ReadAllBytes(path);

                // PNG: 8-byte signature, then IHDR (width/height at offsets 16 and 20, big-endian).
                if (bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                    height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                    return true;
                }

                // JPEG: walk the markers until a Start Of Frame segment (C0-CF, excluding C4/C8/CC).
                if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
                {
                    var i = 2;
                    while (i + 9 < bytes.Length)
                    {
                        if (bytes[i] != 0xFF) { i++; continue; }

                        var marker = bytes[i + 1];
                        if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }

                        var length = (bytes[i + 2] << 8) | bytes[i + 3];
                        var isStartOfFrame = (marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7) ||
                                             (marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF);
                        if (isStartOfFrame)
                        {
                            height = (bytes[i + 5] << 8) | bytes[i + 6];
                            width = (bytes[i + 7] << 8) | bytes[i + 8];
                            return true;
                        }

                        i += 2 + length;
                    }
                }
            }
            catch
            {
                // Unreadable file: reported by the caller as "not a readable PNG/JPEG".
            }

            return false;
        }
    }
}
