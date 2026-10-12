using NUnit.Framework;

namespace Wagenheimer.NativeSocial.Tests
{
    /// <summary>
    /// The queue that holds achievement progress while Google Play Games / Game Center is signed out: it must keep
    /// only the highest progress and survive a PlayerPrefs round trip, since it is delivered with absolute calls.
    /// </summary>
    public class PendingProgressTests
    {
        [Test]
        public void Merge_KeepsTheHighestProgress()
        {
            var entry = new NativeSocial.PendingProgress { LocId = "Trophy1_1", Current = 50, Total = 120 };

            Assert.IsFalse(NativeSocial.MergePendingProgress(entry, 30, 120, false), "a lower value must not change the entry");
            Assert.AreEqual(50, entry.Current);

            Assert.IsTrue(NativeSocial.MergePendingProgress(entry, 80, 120, false));
            Assert.AreEqual(80, entry.Current);
        }

        [Test]
        public void Merge_CompletedIsSticky()
        {
            var entry = new NativeSocial.PendingProgress { LocId = "Trophy1_1", Current = 120, Total = 120 };

            Assert.IsTrue(NativeSocial.MergePendingProgress(entry, 120, 120, true));
            Assert.IsFalse(NativeSocial.MergePendingProgress(entry, 120, 120, false), "a later non-completed report must not undo completion");
            Assert.IsTrue(entry.Completed);
        }

        [Test]
        public void Merge_ZeroTotalKeepsTheKnownTotal()
        {
            // Android reports without a total; it must not wipe the one Game Center needs.
            var entry = new NativeSocial.PendingProgress { LocId = "Trophy3_2", Current = 4, Total = 14 };

            NativeSocial.MergePendingProgress(entry, 6, 0, false);

            Assert.AreEqual(14, entry.Total);
            Assert.AreEqual(6, entry.Current);
        }

        [Test]
        public void Serialization_RoundTripsEveryEntry()
        {
            var items = new[]
            {
                new NativeSocial.PendingProgress { LocId = "Trophy1_1", Current = 120, Total = 120, Completed = true },
                new NativeSocial.PendingProgress { LocId = "Trophy2_3", Current = 700, Total = 1940 },
            };

            var restored = NativeSocial.DeserializePendingProgress(NativeSocial.SerializePendingProgress(items));

            Assert.AreEqual(2, restored.Count);
            Assert.IsTrue(restored["Trophy1_1"].Completed);
            Assert.AreEqual(700, restored["Trophy2_3"].Current);
            Assert.AreEqual(1940, restored["Trophy2_3"].Total);
        }

        [Test]
        public void Deserialize_EmptyOrMissing_ReturnsEmpty()
        {
            Assert.AreEqual(0, NativeSocial.DeserializePendingProgress(null).Count);
            Assert.AreEqual(0, NativeSocial.DeserializePendingProgress(string.Empty).Count);
            Assert.AreEqual(0, NativeSocial.DeserializePendingProgress("{}").Count);
        }
    }
}
