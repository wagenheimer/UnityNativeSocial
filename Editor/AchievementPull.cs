using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Which store each AppDeployHub app record is the source of. The hub writes Google Play rows only to Android and Cross-platform records and Game Center
    /// rows to every record that is not Android (see its AchievementImportService), so an Android or iOS record is the <i>dedicated</i> source of its store
    /// (iPhone / iPad / Mac) and Universal are Game Center only; only a Cross-platform record is a <i>shared</i> one that also holds rows for the other store.
    /// </summary>
    internal static class StoreRoles
    {
        internal const int NotASource = 0;
        internal const int SharedSource = 1;
        internal const int DedicatedSource = 2;

        internal static int GoogleRank(string platform) => (platform ?? string.Empty) switch
        {
            "Android" => DedicatedSource,
            "iOS" or "MacOS" or "Universal" => NotASource,
            _ => SharedSource
        };

        internal static int AppleRank(string platform) => (platform ?? string.Empty) switch
        {
            "iOS" or "MacOS" or "Universal" => DedicatedSource,
            "Android" => NotASource,
            _ => SharedSource
        };

        /// <summary>
        /// App Store Connect's internal resource id is a UUID. Game Center reports progress by the achievement's own Achievement ID (the vendor
        /// identifier, the same key the map uses), so a UUID must never be written as the in-game Apple id.
        /// </summary>
        internal static bool LooksLikeStoreResourceId(string id) => !string.IsNullOrEmpty(id) && Guid.TryParse(id, out _);

        /// <summary>One-line explanation of what a record contributes, for the UI.</summary>
        internal static string Describe(string platform)
        {
            var google = GoogleRank(platform);
            var apple = AppleRank(platform);
            if (google == DedicatedSource) return "Google Play IDs";
            if (apple == DedicatedSource) return "Game Center IDs";
            return "Google Play and Game Center IDs (shared record: used only where no Android / iOS record is selected)";
        }
    }

    /// <summary>
    /// Merges what several apps return in one pull without letting the order of the selection decide. A dedicated record (Android for Google Play,
    /// iOS/macOS for Game Center) always wins over a shared one; two sources of the same rank that disagree are reported and the first one is kept.
    /// Counts are taken against a snapshot of the map from before the pull, so an achievement that two apps wrote is one change, and one that ends up
    /// unchanged is none.
    /// </summary>
    internal sealed class AchievementPull
    {
        private readonly struct Owner
        {
            public readonly string App;
            public readonly string Id;
            public readonly int Rank;

            public Owner(string app, string id, int rank)
            {
                App = app;
                Id = id;
                Rank = rank;
            }
        }

        private readonly Dictionary<string, (string Google, string Apple)> _original;
        private readonly Dictionary<string, Owner> _google = new Dictionary<string, Owner>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Owner> _apple = new Dictionary<string, Owner>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _conflicts = new List<string>();

        public AchievementPull(IEnumerable<(string Key, string Google, string Apple)> original)
        {
            _original = original.ToDictionary(e => e.Key, e => (e.Google ?? string.Empty, e.Apple ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> Conflicts => _conflicts;
        public int IgnoredResourceIds { get; private set; }

        public void NoteIgnoredResourceId() => IgnoredResourceIds++;

        /// <summary>Registers a Google Play id seen from <paramref name="app"/>; true when this source is now the winning one and the value should be applied.</summary>
        public bool OwnsGoogle(string key, string app, string id, int rank) => Own(_google, "Google Play", key, app, id, rank);

        public bool OwnsApple(string key, string app, string id, int rank) => Own(_apple, "Game Center", key, app, id, rank);

        private bool Own(Dictionary<string, Owner> owners, string store, string key, string app, string id, int rank)
        {
            if (rank <= StoreRoles.NotASource || string.IsNullOrEmpty(id)) return false;

            if (!owners.TryGetValue(key, out var current) || rank > current.Rank)
            {
                owners[key] = new Owner(app, id, rank);
                return true;
            }

            if (rank == current.Rank && !string.Equals(current.Id, id, StringComparison.Ordinal))
                _conflicts.Add($"{store} {key}: '{current.App}' says {current.Id}, '{app}' says {id} (kept '{current.App}')");

            return false;
        }

        public int CountGoogleChanges(IEnumerable<(string Key, string Google, string Apple)> current) =>
            current.Count(e => _original.TryGetValue(e.Key, out var o) && !string.Equals(o.Google, e.Google ?? string.Empty, StringComparison.Ordinal));

        public int CountAppleChanges(IEnumerable<(string Key, string Google, string Apple)> current) =>
            current.Count(e => _original.TryGetValue(e.Key, out var o) && !string.Equals(o.Apple, e.Apple ?? string.Empty, StringComparison.Ordinal));
    }
}
