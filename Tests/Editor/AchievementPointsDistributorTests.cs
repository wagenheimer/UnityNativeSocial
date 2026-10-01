using System.Linq;

using NUnit.Framework;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Tests
{
    public class AchievementPointsDistributorTests
    {
        private static double[] ThreeTiersTimes(int trophies) => Enumerable.Range(0, trophies).SelectMany(_ => new[] { 1d, 2d, 3d }).ToArray();

        [Test]
        public void Distribute_FillsTheBudgetExactly_WithMultiplesOfFiveWithinLimits()
        {
            var result = AchievementPointsDistributor.Distribute(ThreeTiersTimes(18), 1000);

            Assert.AreEqual(1000, result.Sum());
            Assert.IsTrue(result.All(p => p % 5 == 0 && p >= 5 && p <= 100));
        }

        [Test]
        public void Distribute_KeepsHigherTiersWorthMore()
        {
            var result = AchievementPointsDistributor.Distribute(ThreeTiersTimes(18), 1000);

            Assert.Less(result[0], result[2]);
            Assert.LessOrEqual(result[0], result[1]);
        }

        [Test]
        public void Distribute_NeverExceedsThePerAchievementMaximum()
        {
            var result = AchievementPointsDistributor.Distribute(new[] { 1d, 1d }, 1000);

            Assert.IsTrue(result.All(p => p == 100));
        }

        [Test]
        public void Distribute_GivesEveryTierAtLeastTheMinimum_EvenWhenTheBudgetIsTiny()
        {
            var result = AchievementPointsDistributor.Distribute(new[] { 1d, 1d, 1d }, 5);

            Assert.IsTrue(result.All(p => p >= 5));
        }

        [Test]
        public void Validate_ReportsEachRuleTheStoresEnforce()
        {
            Assert.IsNull(AchievementPointsRules.Validate(new[] { 10, 20, 30 }));
            StringAssert.Contains("limit", AchievementPointsRules.Validate(Enumerable.Repeat(20, 60).ToArray()));
            StringAssert.Contains("0 points", AchievementPointsRules.Validate(new[] { 0, 10 }));
            StringAssert.Contains("multiple of 5", AchievementPointsRules.Validate(new[] { 7 }));
            StringAssert.Contains("over 100", AchievementPointsRules.Validate(new[] { 150 }));
        }
    }
}
