using NUnit.Framework;

using UnityEngine;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Tests
{
    /// <summary>
    /// Text resolution, the AppDeployHub exchange JSON and the API client's pure helpers. No I2 term is used here,
    /// so these behave the same whether or not the consuming project has I2 Localization installed.
    /// </summary>
    public class AchievementExportAndClientTests
    {
        private AchievementTierMap _map;

        [SetUp]
        public void SetUp()
        {
            _map = ScriptableObject.CreateInstance<AchievementTierMap>();
            _map.Entries.Add(new AchievementTierEntry
            {
                TrophyNumber = 4, Tier = 2, SteamStat = "Trophy4_2_Status", Points = 20,
                DisplayName = "The Builder II", EarnedDescription = "Bought 7.", NotEarnedDescription = "Buy 7."
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_map);

        [Test]
        public void Resolve_UsesTheLiteral_WhenThereIsNoTerm()
        {
            var resolved = AchievementTextResolver.Resolve(string.Empty, "Hello", "English");

            Assert.AreEqual(TextSource.Literal, resolved.Source);
            Assert.AreEqual("Hello", resolved.Text);
        }

        [Test]
        public void Resolve_IsMissing_WhenThereIsNeitherTermNorLiteral()
        {
            var resolved = AchievementTextResolver.Resolve(string.Empty, string.Empty, "English");

            Assert.AreEqual(TextSource.Missing, resolved.Source);
            Assert.IsFalse(resolved.IsOk);
        }

        [Test]
        public void Resolve_NeverReusesAnEnglishLiteralForAnotherLanguage_WhenStrict()
        {
            var resolved = AchievementTextResolver.Resolve(string.Empty, "Hello", "Portuguese", allowEnglishFallback: false);

            Assert.AreEqual(TextSource.Missing, resolved.Source);
        }

        [Test]
        public void BuildEntries_CarriesLiteralTextsAndPlatformData()
        {
            var entries = AchievementExchangeExporter.BuildEntries(_map);

            var entry = entries[0];
            Assert.AreEqual("Trophy4_2", entry.key);
            Assert.AreEqual("The Builder II", entry.displayName);
            Assert.AreEqual("Bought 7.", entry.earnedDescription);
            Assert.AreEqual("Buy 7.", entry.notEarnedDescription);
            Assert.AreEqual(20, entry.points);
            Assert.AreEqual("Trophy4_2_Status", entry.steamStat);
            Assert.IsNull(entry.googlePlayId);
        }

        [Test]
        public void BuildJson_IsTheVersionedFormat_WithALocalizationsList()
        {
            var json = AchievementExchangeExporter.BuildJson(_map, "Storm Tale 2", "en-US");

            StringAssert.Contains("\"formatId\": \"appdeployhub-achievements/v1\"", json);
            StringAssert.Contains("\"locale\": \"en-US\"", json);
            StringAssert.Contains("\"localizations\"", json);
        }

        [TestCase("https://hub.example.com", null)]
        [TestCase("http://localhost:5000", null)]
        [TestCase("http://127.0.0.1:5000", null)]
        public void ValidateBaseUrl_AcceptsHttpsAndLocalhost(string url, string expected)
        {
            Assert.AreEqual(expected, AppDeployHubClient.ValidateBaseUrl(url));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not a url")]
        [TestCase("http://hub.example.com")]
        public void ValidateBaseUrl_RejectsEmptyMalformedAndPlainRemoteHttp(string url)
        {
            Assert.IsNotNull(AppDeployHubClient.ValidateBaseUrl(url));
        }

        [Test]
        public void Explain_GivesActionableMessages()
        {
            StringAssert.Contains("revoked", AppDeployHubClient.Explain(401, string.Empty));
            StringAssert.Contains("App not found", AppDeployHubClient.Explain(404, "{\"error\":\"App not found.\"}"));
            StringAssert.Contains("boom", AppDeployHubClient.Explain(500, "{\"error\":\"boom\"}"));
        }

        [Test]
        public void ParseApps_ReadsATopLevelJsonArray()
        {
            var apps = AppDeployHubClient.ParseApps("[{\"id\":\"1\",\"name\":\"Storm Tale 2\",\"packageName\":\"com.a.b\",\"hasGooglePlayGamesApplicationId\":true}]");

            Assert.AreEqual(1, apps.Length);
            Assert.AreEqual("Storm Tale 2", apps[0].name);
            Assert.AreEqual("com.a.b", apps[0].packageName);
        }

        [Test]
        public void ParseImportResult_ReadsCountsAndLocales()
        {
            var result = AppDeployHubClient.ParseImportResult(
                "{\"googlePlayCreated\":3,\"googlePlayUpdated\":1,\"gameCenterCreated\":3,\"gameCenterUpdated\":1,\"locales\":[\"en-US\",\"pt-BR\"],\"googlePlayPushQueued\":false,\"gameCenterPushQueued\":false}");

            Assert.AreEqual(3, result.googlePlayCreated);
            Assert.AreEqual(2, result.locales.Length);
            Assert.IsFalse(result.googlePlayPushQueued);
        }
    }
}
