using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// The "Android resources" XML of Play Console (Play Games Services &gt; Configuration &gt; Achievements &gt; Get resources &gt; Android). It carries
    /// the application id (<c>app_id</c>), the package it was configured for (<c>package_name</c>) and one <c>achievement_*</c> entry per achievement.
    /// Google's own setup window pastes it only to write a constants class (<c>GPGSIds</c>) that NativeSocial does not need, but the data is a reliable
    /// source of truth to verify the ids in the Tier Map against.
    /// </summary>
    internal sealed class GpgsResourcesXml
    {
        private static readonly Regex ClientIdPattern = new Regex(@"^(?<project>\d+)-[a-zA-Z0-9]+\.apps\.googleusercontent\.com$", RegexOptions.Compiled);

        public string AppId { get; private set; }
        public string PackageName { get; private set; }

        /// <summary>Resource name (for example <c>achievement_first_win</c>) to Play Games achievement id.</summary>
        public IReadOnlyDictionary<string, string> Achievements => _achievements;

        private readonly Dictionary<string, string> _achievements = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Parses the pasted text; false with a reason when it is not usable. DTDs are prohibited, the text is untrusted.</summary>
        public static bool TryParse(string xml, out GpgsResourcesXml result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(xml))
            {
                error = "Nothing pasted yet.";
                return false;
            }

            var parsed = new GpgsResourcesXml();
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xml), settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "string") continue;

                        var name = reader.GetAttribute("name");
                        var value = reader.ReadElementContentAsString().Trim();
                        if (string.IsNullOrEmpty(name)) continue;

                        if (name == "app_id") parsed.AppId = value;
                        else if (name == "package_name") parsed.PackageName = value;
                        else if (name.StartsWith("achievement_", StringComparison.Ordinal)) parsed._achievements[name] = value;
                    }
                }
            }
            catch (XmlException ex)
            {
                error = "The text is not valid XML: " + ex.Message;
                return false;
            }

            if (string.IsNullOrEmpty(parsed.AppId) || !parsed.AppId.All(char.IsDigit))
            {
                error = "The XML has no numeric app_id element. Copy it again from Play Console (Get resources > Android).";
                return false;
            }

            result = parsed;
            return true;
        }

        /// <summary>Achievement ids of <paramref name="mapIds"/> that the XML does not list, so they would not unlock on Android.</summary>
        public IReadOnlyList<string> MissingFromXml(IEnumerable<string> mapIds)
        {
            var known = new HashSet<string>(_achievements.Values, StringComparer.Ordinal);
            return mapIds.Where(id => !string.IsNullOrEmpty(id) && !known.Contains(id)).Distinct().ToList();
        }

        /// <summary>Resource names whose id is not used by any entry in the map (achievements in Play Console that the game never reports).</summary>
        public IReadOnlyList<string> UnusedByMap(IEnumerable<string> mapIds)
        {
            var used = new HashSet<string>(mapIds.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            return _achievements.Where(a => !used.Contains(a.Value)).Select(a => a.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Checks the optional Web client id: empty is fine (it is only needed for server-side access, NativeSocial.GetServerAuthCode), otherwise it must be an
        /// OAuth "Web application" client of the same Google Cloud project as the application id, which Google's setup also enforces.
        /// </summary>
        public static string ValidateWebClientId(string clientId, string appId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return null;

            var match = ClientIdPattern.Match(clientId.Trim());
            if (!match.Success)
                return "A Web client id looks like 123456789012-abcdefghijklm.apps.googleusercontent.com.";

            return !string.IsNullOrEmpty(appId) && match.Groups["project"].Value != appId
                ? $"The number before the dash ({match.Groups["project"].Value}) must equal the application id ({appId}); the client must belong to the same Play Games project."
                : null;
        }
    }
}
