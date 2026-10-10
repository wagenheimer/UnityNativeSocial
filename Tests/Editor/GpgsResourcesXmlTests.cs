using NUnit.Framework;

using Wagenheimer.NativeSocial.Editor;

namespace Wagenheimer.NativeSocial.Tests
{
    public class GpgsResourcesXmlTests
    {
        private const string Sample = @"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
  <!--app_id-->
  <string name=""app_id"" translatable=""false"">148394552703</string>
  <!--package_name-->
  <string name=""package_name"" translatable=""false"">com.greensaucegames.stormtale2</string>
  <!--achievement Trophy1_1-->
  <string name=""achievement_trophy1_1"" translatable=""false"">AAA</string>
  <string name=""achievement_trophy1_2"" translatable=""false"">BBB</string>
</resources>";

        [Test]
        public void Parses_AppId_Package_AndAchievements()
        {
            Assert.IsTrue(GpgsResourcesXml.TryParse(Sample, out var xml, out _));
            Assert.AreEqual("148394552703", xml.AppId);
            Assert.AreEqual("com.greensaucegames.stormtale2", xml.PackageName);
            Assert.AreEqual(2, xml.Achievements.Count);
        }

        [TestCase("")]
        [TestCase("not xml")]
        [TestCase("<resources><string name=\"package_name\">x</string></resources>")]
        public void Rejects_UnusableText(string text)
        {
            Assert.IsFalse(GpgsResourcesXml.TryParse(text, out var xml, out var error));
            Assert.IsNull(xml);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Rejects_DtdInsteadOfResolvingIt()
        {
            var hostile = "<!DOCTYPE r [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><resources><string name=\"app_id\">&x;</string></resources>";
            Assert.IsFalse(GpgsResourcesXml.TryParse(hostile, out _, out _));
        }

        [Test]
        public void Compares_MapIdsAgainstTheXml()
        {
            GpgsResourcesXml.TryParse(Sample, out var xml, out _);
            CollectionAssert.AreEqual(new[] { "ZZZ" }, xml.MissingFromXml(new[] { "AAA", "ZZZ", "", null }));
            CollectionAssert.AreEqual(new[] { "achievement_trophy1_2" }, xml.UnusedByMap(new[] { "AAA" }));
        }

        [TestCase("", "148394552703", null)]
        [TestCase("148394552703-abc123.apps.googleusercontent.com", "148394552703", null)]
        public void WebClientId_Valid(string clientId, string appId, string expected) =>
            Assert.AreEqual(expected, GpgsResourcesXml.ValidateWebClientId(clientId, appId));

        [TestCase("garbage", "148394552703")]
        [TestCase("999999999999-abc.apps.googleusercontent.com", "148394552703")]
        public void WebClientId_Invalid(string clientId, string appId) =>
            Assert.IsNotNull(GpgsResourcesXml.ValidateWebClientId(clientId, appId));
    }
}
