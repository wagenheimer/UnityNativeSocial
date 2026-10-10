using System.Linq;

using NUnit.Framework;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Tests
{
    /// <summary>Which AppDeployHub record is the source of which store, and how a pull from several apps is merged.</summary>
    public class AchievementPullTests
    {
        private static AchievementPull Pull(string key = "Trophy1_1", string google = "old", string apple = "Trophy1_1") =>
            new AchievementPull(new[] { (key, google, apple) });

        [TestCase("Android", 2, 0)]
        [TestCase("iOS", 0, 2)]
        [TestCase("MacOS", 0, 2)]
        [TestCase("Universal", 0, 2)]
        [TestCase("CrossPlatform", 1, 1)]
        public void Ranks_FollowWhatTheHubWritesPerPlatform(string platform, int google, int apple)
        {
            Assert.AreEqual(google, StoreRoles.GoogleRank(platform));
            Assert.AreEqual(apple, StoreRoles.AppleRank(platform));
        }

        [Test]
        public void ADedicatedRecord_AlwaysWinsOverASharedOne_WhateverTheOrder()
        {
            var sharedFirst = Pull();
            Assert.IsTrue(sharedFirst.OwnsGoogle("Trophy1_1", "CrossPlatform", "SHARED", StoreRoles.SharedSource));
            Assert.IsTrue(sharedFirst.OwnsGoogle("Trophy1_1", "Android", "ANDROID", StoreRoles.DedicatedSource));

            var dedicatedFirst = Pull();
            Assert.IsTrue(dedicatedFirst.OwnsGoogle("Trophy1_1", "Android", "ANDROID", StoreRoles.DedicatedSource));
            Assert.IsFalse(dedicatedFirst.OwnsGoogle("Trophy1_1", "CrossPlatform", "SHARED", StoreRoles.SharedSource));
            Assert.IsEmpty(dedicatedFirst.Conflicts, "A shared record losing to a dedicated one is expected, not a conflict.");
        }

        [Test]
        public void TwoRecordsOfTheSameRank_Disagreeing_AreReportedAndTheFirstIsKept()
        {
            var pull = Pull();
            Assert.IsTrue(pull.OwnsGoogle("Trophy1_1", "Android A", "AAA", StoreRoles.DedicatedSource));
            Assert.IsFalse(pull.OwnsGoogle("Trophy1_1", "Android B", "BBB", StoreRoles.DedicatedSource));

            Assert.AreEqual(1, pull.Conflicts.Count);
            StringAssert.Contains("Android A", pull.Conflicts[0]);
            StringAssert.Contains("Android B", pull.Conflicts[0]);
        }

        [Test]
        public void ARecordThatIsNotASourceForTheStore_NeverOwnsIt()
        {
            var pull = Pull();

            Assert.IsFalse(pull.OwnsGoogle("Trophy1_1", "iOS", "XYZ", StoreRoles.GoogleRank("iOS")));
            Assert.IsFalse(pull.OwnsApple("Trophy1_1", "Android", "XYZ", StoreRoles.AppleRank("Android")));
        }

        [Test]
        public void Counts_AreDistinctAchievementsComparedWithTheMapBeforeThePull()
        {
            var pull = new AchievementPull(new[] { ("A", "g1", "A"), ("B", "g2", "B") });

            // Two apps both wrote A (counts once); B ends up exactly as it started (counts as nothing).
            var after = new[] { ("A", "gNew", "A"), ("B", "g2", "B") };

            Assert.AreEqual(1, pull.CountGoogleChanges(after));
            Assert.AreEqual(0, pull.CountAppleChanges(after));
        }

        [TestCase("fd3430ca-8029-411a-a6be-958791eab10f", true)]
        [TestCase("Trophy1_1", false)]
        [TestCase("com.studio.game.kill_100", false)]
        [TestCase("", false)]
        public void LooksLikeStoreResourceId_RecognisesUuids(string id, bool expected)
        {
            Assert.AreEqual(expected, StoreRoles.LooksLikeStoreResourceId(id));
        }
    }
}
