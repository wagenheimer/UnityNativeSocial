using System.Linq;

using NUnit.Framework;

using Wagenheimer.NativeSocial.Editor;
using Wagenheimer.NativeSocial.Editor.UI;

namespace Wagenheimer.NativeSocial.Tests
{
    public class AppDeployHubAppPickerTests
    {
        private static AppDeployHubClient.AppSummary App(string id, string platform, string package = null, string bundle = null, string name = "Storm Tale 2", string studio = "s1", string group = null) =>
            new AppDeployHubClient.AppSummary { id = id, name = name, platform = platform, packageName = package, bundleId = bundle, studioId = studio, studioName = "Green Sauce", groupKey = group };

        private static readonly AppDeployHubClient.AppSummary[] Apps =
        {
            App("ios", "iOS", bundle: "com.greensaucegames.stormtale2"),
            App("and", "Android", package: "com.greensaucegames.stormtale2"),
            App("other", "Android", package: "com.other", name: "Other Game"),
        };

        [Test]
        public void StoreLabel_AndroidGetsGooglePlay_IosGetsGameCenter()
        {
            Assert.AreEqual("Google Play", AppDeployHubAppPicker.StoreLabel(Apps[1]));
            Assert.AreEqual("Apple Game Center", AppDeployHubAppPicker.StoreLabel(Apps[0]));
            Assert.AreEqual("Google Play + Apple Game Center", AppDeployHubAppPicker.StoreLabel(App("x", "CrossPlatform")));
        }

        [Test]
        public void Identifier_PrefersPackageName_ThenBundleId()
        {
            Assert.AreEqual("com.greensaucegames.stormtale2", AppDeployHubAppPicker.Identifier(Apps[1]));
            Assert.AreEqual("com.greensaucegames.stormtale2", AppDeployHubAppPicker.Identifier(Apps[0]));
        }

        [Test]
        public void Group_PutsAGamesStoreRecordsTogether()
        {
            var groups = AppDeployHubAppPicker.Group(Apps);

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(2, groups.Single(g => g[0].name == "Storm Tale 2").Count);
        }

        [Test]
        public void WithSiblings_SelectingOneStoreRecordSelectsTheOther()
        {
            var selected = AppDeployHubAppPicker.WithSiblings(Apps, new[] { "and" });

            CollectionAssert.AreEquivalent(new[] { "and", "ios" }, selected);
        }

        [Test]
        public void WithSiblings_DoesNotMergeSameNameInDifferentStudios()
        {
            var apps = new[] { App("a", "Android", studio: "s1"), App("b", "iOS", studio: "s2") };

            CollectionAssert.AreEquivalent(new[] { "a" }, AppDeployHubAppPicker.WithSiblings(apps, new[] { "a" }));
        }

        [Test]
        public void Matches_SearchesNameStudioIdentifierAndPlatform()
        {
            Assert.IsTrue(AppDeployHubAppPicker.Matches(Apps[1], "storm android"));
            Assert.IsTrue(AppDeployHubAppPicker.Matches(Apps[1], "greensauce"));
            Assert.IsFalse(AppDeployHubAppPicker.Matches(Apps[1], "ios"));
            Assert.IsTrue(AppDeployHubAppPicker.Matches(Apps[1], ""));
        }

        [Test]
        public void ParseApps_ReadsPlatformBundleAndGroupKey()
        {
            var apps = AppDeployHubClient.ParseApps("[{\"id\":\"1\",\"name\":\"G\",\"platform\":\"iOS\",\"bundleId\":\"com.g\",\"groupKey\":\"k\",\"studioId\":\"s\",\"studioName\":\"S\"}]");

            Assert.AreEqual("iOS", apps[0].platform);
            Assert.AreEqual("com.g", apps[0].bundleId);
            Assert.AreEqual("k", apps[0].groupKey);
        }
    }
}
