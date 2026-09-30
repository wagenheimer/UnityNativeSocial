using NUnit.Framework;

using UnityEngine;

using Wagenheimer.NativeSocial;

namespace Wagenheimer.NativeSocial.Tests
{
    public class AchievementTierMapTests
    {
        private AchievementTierMap _map;

        [SetUp]
        public void SetUp()
        {
            _map = ScriptableObject.CreateInstance<AchievementTierMap>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_map);
        }

        [Test]
        public void LocId_FormatsAsTrophyNumberUnderscoreTier()
        {
            Assert.AreEqual("Trophy4_2", AchievementTierMap.LocId(4, 2));
            Assert.AreEqual("Trophy1_1", AchievementTierMap.LocId(1, 1));
        }

        [TestCase(1, "I")]
        [TestCase(2, "II")]
        [TestCase(3, "III")]
        [TestCase(4, "IV")]
        [TestCase(9, "IX")]
        [TestCase(14, "XIV")]
        [TestCase(1994, "MCMXCIV")]
        public void RomanNumeral_ConvertsKnownValues(int value, string expected)
        {
            Assert.AreEqual(expected, AchievementTierMap.RomanNumeral(value));
        }

        [TestCase(0)]
        [TestCase(-3)]
        [TestCase(4000)]
        public void RomanNumeral_FallsBackToPlainNumber_OutsideSupportedRange(int value)
        {
            Assert.AreEqual(value.ToString(), AchievementTierMap.RomanNumeral(value));
        }

        [Test]
        public void DefaultTermKeys_FollowTheConvention()
        {
            Assert.AreEqual("trophy4", AchievementTierMap.DefaultNameTerm(4));
            Assert.AreEqual("Achievements/Trophy4_3/Earned", AchievementTierMap.DefaultEarnedTerm(4, 3));
            Assert.AreEqual("Achievements/Trophy4_3/NotEarned", AchievementTierMap.DefaultNotEarnedTerm(4, 3));
        }

        [Test]
        public void TierCount_CountsOnlyTheRequestedTrophy()
        {
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 1, Tier = 1 });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 1, Tier = 2 });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 2, Tier = 1 });

            Assert.AreEqual(2, _map.TierCount(1));
            Assert.AreEqual(1, _map.TierCount(2));
            Assert.AreEqual(0, _map.TierCount(99));
        }

        [Test]
        public void AppendTierNumeral_DefaultsToTrue_ForOldAssets()
        {
            Assert.IsTrue(_map.AppendTierNumeral);
        }

        [Test]
        public void TryGetEntry_FindsMatchingTrophyAndTier()
        {
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 3, Tier = 2, GooglePlayId = "gp_3_2" });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 3, Tier = 1, GooglePlayId = "gp_3_1" });

            var found = _map.TryGetEntry(3, 2, out var entry);

            Assert.IsTrue(found);
            Assert.AreEqual("gp_3_2", entry.GooglePlayId);
        }

        [Test]
        public void TryGetEntry_ReturnsFalse_WhenNoMatch()
        {
            var found = _map.TryGetEntry(99, 1, out var entry);

            Assert.IsFalse(found);
            Assert.AreEqual(default(AchievementTierEntry), entry);
        }

        [Test]
        public void BuildAndroidMap_SkipsEntriesWithEmptyGooglePlayId()
        {
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 1, Tier = 1, GooglePlayId = "gp_1_1" });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 1, Tier = 2, GooglePlayId = "" });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 1, Tier = 3, GooglePlayId = null });

            var map = _map.BuildAndroidMap();

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual("gp_1_1", map["Trophy1_1"]);
        }

        [Test]
        public void BuildIosMap_SkipsEntriesWithEmptyAppleId()
        {
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 2, Tier = 1, AppleId = "apple_2_1" });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 2, Tier = 2, AppleId = "" });

            var map = _map.BuildIosMap();

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual("apple_2_1", map["Trophy2_1"]);
        }

        [Test]
        public void BuildSteamMap_SkipsEntriesWithEmptySteamStat_AndUsesStatNameForBoth()
        {
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 5, Tier = 1, SteamStat = "Trophy5_1_Status" });
            _map.Entries.Add(new AchievementTierEntry { TrophyNumber = 5, Tier = 2, SteamStat = "" });

            var map = _map.BuildSteamMap();

            Assert.AreEqual(1, map.Count);
            var entry = map["Trophy5_1"];
            Assert.AreEqual("Trophy5_1_Status", entry.Stat);
            Assert.AreEqual("Trophy5_1_Status", entry.Achievement);
        }

        [Test]
        public void CountMissingGooglePlay_CountsOnlyEmptyOrNullIds()
        {
            _map.Entries.Add(new AchievementTierEntry { GooglePlayId = "id1" });
            _map.Entries.Add(new AchievementTierEntry { GooglePlayId = "" });
            _map.Entries.Add(new AchievementTierEntry { GooglePlayId = null });

            Assert.AreEqual(2, _map.CountMissingGooglePlay());
        }

        [Test]
        public void CountMissingApple_CountsOnlyEmptyOrNullIds()
        {
            _map.Entries.Add(new AchievementTierEntry { AppleId = "id1" });
            _map.Entries.Add(new AchievementTierEntry { AppleId = "id2" });
            _map.Entries.Add(new AchievementTierEntry { AppleId = "" });

            Assert.AreEqual(1, _map.CountMissingApple());
        }
    }
}
