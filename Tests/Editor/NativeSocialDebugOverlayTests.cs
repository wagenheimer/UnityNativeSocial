using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.NativeSocial;
using Wagenheimer.NativeSocial.UI;

namespace Wagenheimer.NativeSocial.Tests
{
    public class NativeSocialDebugOverlayTests
    {
        [SetUp]
        public void SetUp()
        {
            var android = new Dictionary<string, string> { { "ach_test", "CgkI_test" } };
            var ios = new Dictionary<string, string> { { "ach_test", "com.test.ach" } };
            NativeSocial.Initialize(android, ios);
        }

        [Test]
        public void NativeSocial_IsInitialized_ReturnsTrue()
        {
            Assert.IsTrue(NativeSocial.IsInitialized);
            Assert.AreEqual(1, NativeSocial.AndroidMap.Count);
            Assert.AreEqual("CgkI_test", NativeSocial.AndroidMap["ach_test"]);
            Assert.AreEqual(1, NativeSocial.IosMap.Count);
            Assert.AreEqual("com.test.ach", NativeSocial.IosMap["ach_test"]);
        }

        [Test]
        public void NativeSocial_OnReport_FiresEventWithCorrectArgs()
        {
            string reportedLoc = null;
            int reportedDelta = 0;
            int reportedCurrent = 0;
            int reportedTotal = 0;
            bool reportedCompleted = false;

            System.Action<string, int, int, int, bool> handler = (loc, delta, cur, tot, comp) =>
            {
                reportedLoc = loc;
                reportedDelta = delta;
                reportedCurrent = cur;
                reportedTotal = tot;
                reportedCompleted = comp;
            };

            NativeSocial.OnReport += handler;
            try
            {
                NativeSocial.Report("ach_test", 5, 20, 100, false);
                Assert.AreEqual("ach_test", reportedLoc);
                Assert.AreEqual(5, reportedDelta);
                Assert.AreEqual(20, reportedCurrent);
                Assert.AreEqual(100, reportedTotal);
                Assert.IsFalse(reportedCompleted);
            }
            finally
            {
                NativeSocial.OnReport -= handler;
            }
        }

        [Test]
        public void NativeSocial_OnSubmitScore_FiresEventWithCorrectArgs()
        {
            string reportedLoc = null;
            long reportedScore = 0;

            System.Action<string, long> handler = (loc, sc) =>
            {
                reportedLoc = loc;
                reportedScore = sc;
            };

            NativeSocial.OnSubmitScore += handler;
            try
            {
                NativeSocial.SubmitScore("lb_test", 9999);
                Assert.AreEqual("lb_test", reportedLoc);
                Assert.AreEqual(9999, reportedScore);
            }
            finally
            {
                NativeSocial.OnSubmitScore -= handler;
            }
        }

        [Test]
        public void NativeSocialDebugOverlay_EnsureOverlay_CreatesComponent()
        {
            var overlay = NativeSocialDebugOverlay.EnsureOverlay();
            Assert.IsNotNull(overlay);
            Assert.AreEqual(KeyCode.F7, overlay.toggleKey);
            Assert.IsTrue(overlay.showFloatingButton);

            Object.DestroyImmediate(overlay.gameObject);
        }
    }
}
