using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>Interactive Play Mode helper: exercises Authenticate/Report/SubmitScore against whichever platform SDK is active.</summary>
    internal sealed class NativeSocialHelperView
    {
        public VisualElement Root { get; }

        private string _testLocId = "Trophy1_1";
        private int _testDelta = 1;
        private int _testCurrent = 1;
        private int _testTotal = 10;
        private bool _testCompleted;
        private string _testLeaderboardId = "lb_high_score";
        private long _testScore = 1000;

        public NativeSocialHelperView()
        {
            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);
            BuildUI();
        }

        private void BuildUI()
        {
            var headerCard = NativeSocialUIStyle.CreateCard("Interactive Live Helper & Tester",
                "Test authentication, achievement reporting, and leaderboard submissions directly in the Editor.");
            Root.Add(headerCard);

            if (!EditorApplication.isPlaying)
            {
                Root.Add(NativeSocialUIStyle.CreateCallout(
                    "Unity is in Edit Mode. Live calls to native SDKs (GPGS / Game Center / Steam) require Play Mode or standalone runtime. You can still test method invocation and verify code generation.",
                    AuditSeverity.Warning));
            }

            BuildAuthCard();
            BuildAchievementCard();
            BuildLeaderboardCard();
        }

        private void BuildAuthCard()
        {
            var authCard = NativeSocialUIStyle.CreateCard("Authentication Helper", "Triggers native authentication on supported platforms.");
            var authBtnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };

            authBtnRow.Add(NativeSocialUIStyle.CreateButton("Test Authenticate()", () =>
                NativeSocial.Authenticate(success => Debug.Log($"[NativeSocial Helper] Authenticate result: {success}")), primary: true));

            authBtnRow.Add(NativeSocialUIStyle.CreateButton("Test AuthenticateManually()", () =>
                NativeSocial.AuthenticateManually(success => Debug.Log($"[NativeSocial Helper] AuthenticateManually result: {success}"))));

            authBtnRow.Add(NativeSocialUIStyle.CreateButton("Test GetServerAuthCode()", () =>
                NativeSocial.GetServerAuthCode(code => Debug.Log($"[NativeSocial Helper] Server Auth Code: {(string.IsNullOrEmpty(code) ? "null/empty" : code)}"))));

            authCard.Add(authBtnRow);
            Root.Add(authCard);
        }

        private void BuildAchievementCard()
        {
            var achCard = NativeSocialUIStyle.CreateCard("Achievement Report Helper", "Test reporting incremental or completed achievements.");

            var locIdField = new TextField("LocID Key") { value = _testLocId };
            locIdField.RegisterValueChangedCallback(evt => _testLocId = evt.newValue);
            achCard.Add(locIdField);

            var deltaField = new IntegerField("Delta (Android/Steam)") { value = _testDelta };
            deltaField.RegisterValueChangedCallback(evt => _testDelta = evt.newValue);
            achCard.Add(deltaField);

            var curField = new IntegerField("Current Progress (iOS)") { value = _testCurrent };
            curField.RegisterValueChangedCallback(evt => _testCurrent = evt.newValue);
            achCard.Add(curField);

            var totalField = new IntegerField("Total Required (iOS)") { value = _testTotal };
            totalField.RegisterValueChangedCallback(evt => _testTotal = evt.newValue);
            achCard.Add(totalField);

            var compToggle = new Toggle("Completed Immediately") { value = _testCompleted };
            compToggle.RegisterValueChangedCallback(evt => _testCompleted = evt.newValue);
            achCard.Add(compToggle);

            var reportRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 10 } };

            reportRow.Add(NativeSocialUIStyle.CreateButton("Report Achievement", () =>
            {
                Debug.Log($"[NativeSocial Helper] Reporting: LocID={_testLocId}, Delta={_testDelta}, Current={_testCurrent}, Total={_testTotal}, Completed={_testCompleted}");
                NativeSocial.Report(_testLocId, _testDelta, _testCurrent, _testTotal, _testCompleted);
            }, primary: true));

            reportRow.Add(NativeSocialUIStyle.CreateButton("Show Platform Achievements UI", () =>
                Debug.Log($"[NativeSocial Helper] ShowAchievementsUI returned: {NativeSocial.ShowAchievementsUI()}")));

            achCard.Add(reportRow);
            Root.Add(achCard);
        }

        private void BuildLeaderboardCard()
        {
            var lbCard = NativeSocialUIStyle.CreateCard("Leaderboard Submit Helper", "Test posting leaderboard scores to platform services.");

            var lbField = new TextField("Leaderboard LocID") { value = _testLeaderboardId };
            lbField.RegisterValueChangedCallback(evt => _testLeaderboardId = evt.newValue);
            lbCard.Add(lbField);

            var scoreField = new LongField("Score") { value = _testScore };
            scoreField.RegisterValueChangedCallback(evt => _testScore = evt.newValue);
            lbCard.Add(scoreField);

            var lbRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 10 } };

            lbRow.Add(NativeSocialUIStyle.CreateButton("Submit Score", () =>
            {
                Debug.Log($"[NativeSocial Helper] Submitting Score: LocID={_testLeaderboardId}, Score={_testScore}");
                NativeSocial.SubmitScore(_testLeaderboardId, _testScore);
            }, primary: true));

            lbRow.Add(NativeSocialUIStyle.CreateButton("Show Leaderboard UI", () =>
                Debug.Log($"[NativeSocial Helper] ShowLeaderboardUI returned: {NativeSocial.ShowLeaderboardUI(_testLeaderboardId)}")));

            lbCard.Add(lbRow);
            Root.Add(lbCard);
        }
    }
}
