using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>Per-store points rules (as documented by Google Play Games and Apple Game Center).</summary>
    internal static class AchievementPointsRules
    {
        /// <summary>Both stores cap the SUM of all achievements' points at 1000.</summary>
        public const int StoreTotalLimit = 1000;

        /// <summary>Google Play requires multiples of 5; using it for both keeps one value valid everywhere.</summary>
        public const int Step = 5;

        /// <summary>Apple allows 1-100 per achievement (Google Play allows up to 200), so 100 is the common maximum.</summary>
        public const int MaxPerAchievement = 100;

        public static string Validate(IReadOnlyList<int> points)
        {
            var problems = new List<string>();
            var total = points.Sum();
            if (total > StoreTotalLimit) problems.Add($"total {total} is over the {StoreTotalLimit} limit both stores enforce (by {total - StoreTotalLimit})");
            var zero = points.Count(p => p <= 0);
            if (zero > 0) problems.Add($"{zero} tier(s) have 0 points (Apple needs at least 1)");
            var over = points.Count(p => p > MaxPerAchievement);
            if (over > 0) problems.Add($"{over} tier(s) are over {MaxPerAchievement} points (Apple's per-achievement maximum)");
            var notStep = points.Count(p => p > 0 && p % Step != 0);
            if (notStep > 0) problems.Add($"{notStep} tier(s) are not a multiple of {Step} (Google Play requires it)");
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }
    }

    /// <summary>Distributes a points budget over achievements so the totals satisfy both stores.</summary>
    internal static class AchievementPointsDistributor
    {
        /// <summary>
        /// Splits <paramref name="total"/> points (rounded down to a multiple of 5) proportionally to
        /// <paramref name="weights"/>. Every result is a multiple of 5, at least 5 and at most 100, and the results
        /// sum to exactly the budget whenever the limits allow it (largest-remainder rounding).
        /// </summary>
        public static int[] Distribute(IReadOnlyList<double> weights, int total)
        {
            var count = weights.Count;
            if (count == 0) return Array.Empty<int>();

            var step = AchievementPointsRules.Step;
            var maxUnits = AchievementPointsRules.MaxPerAchievement / step;
            var budget = Math.Max(count, Math.Min(total / step, count * maxUnits)); // each needs >= 1 unit and <= maxUnits

            var safe = weights.Select(w => w > 0 ? w : 1d).ToArray();
            var sum = safe.Sum();
            var raw = safe.Select(w => w / sum * budget).ToArray();
            var units = raw.Select(r => Math.Min(maxUnits, Math.Max(1, (int)Math.Floor(r)))).ToArray();

            // Hand out / take back units one at a time, favouring the largest rounding remainder.
            while (units.Sum() < budget)
            {
                var i = Enumerable.Range(0, count).Where(k => units[k] < maxUnits).OrderByDescending(k => raw[k] - units[k]).Select(k => (int?)k).FirstOrDefault() ?? -1;
                if (i < 0) break;
                units[i]++;
            }
            while (units.Sum() > budget)
            {
                var i = Enumerable.Range(0, count).Where(k => units[k] > 1).OrderBy(k => raw[k] - units[k]).Select(k => (int?)k).FirstOrDefault() ?? -1;
                if (i < 0) break;
                units[i]--;
            }
            return units.Select(u => u * step).ToArray();
        }
    }
}
