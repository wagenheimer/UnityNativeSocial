using System.Collections.Generic;

using NUnit.Framework;

namespace Wagenheimer.NativeSocial.Tests
{
    public class ReportProgressTests
    {
        private const string LocId = "Trophy1_1";

        private readonly List<string> _reports = new List<string>();

        [SetUp]
        public void SetUp()
        {
            NativeSocial.Initialize(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, SteamEntry>());
#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            NativeSocial.SteamReady = true;
#endif
            _reports.Clear();
            NativeSocial.OnReport += Record;
        }

        [TearDown]
        public void TearDown()
        {
            NativeSocial.OnReport -= Record;
#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
            NativeSocial.SteamReady = false;
#endif
        }

        [Test]
        public void SendsOnlyWhenProgressGrows()
        {
            NativeSocial.ReportProgress(LocId, 0, 10, false);
            NativeSocial.ReportProgress(LocId, 3, 10, false);
            NativeSocial.ReportProgress(LocId, 3, 10, false);
            NativeSocial.ReportProgress(LocId, 2, 10, false);
            NativeSocial.ReportProgress(LocId, 5, 10, false);

            CollectionAssert.AreEqual(new[] { "3/10 False", "5/10 False" }, _reports);
        }

        [Test]
        public void SendsUnlockOnceAndNoProgressAfterIt()
        {
            NativeSocial.ReportProgress(LocId, 10, 10, true);
            NativeSocial.ReportProgress(LocId, 10, 10, true);
            NativeSocial.ReportProgress(LocId, 12, 10, false);

            CollectionAssert.AreEqual(new[] { "10/10 True" }, _reports);
        }

#if WAGENHEIMER_NATIVESOCIAL_STEAM && !UNITY_ANDROID && !UNITY_IOS
        [Test]
        public void KeepsValueForLaterWhenSteamIsNotReady()
        {
            NativeSocial.SteamReady = false;
            NativeSocial.ReportProgress(LocId, 3, 10, false);
            NativeSocial.SteamReady = true;
            NativeSocial.ReportProgress(LocId, 3, 10, false);

            CollectionAssert.AreEqual(new[] { "3/10 False" }, _reports);
        }
#endif

        private void Record(string locId, int delta, int current, int total, bool completed) =>
            _reports.Add($"{current}/{total} {completed}");
    }
}
