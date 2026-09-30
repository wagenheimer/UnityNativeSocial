using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    /// <summary>
    /// The achievement editor: replaces Unity's raw "Entries" list (one flat wall of fields per tier) with a
    /// card per trophy, a compact row per tier showing the resolved name / earned / not-earned texts and
    /// per-platform + I2 status at a glance, filters and search, and a collapsible edit panel per tier bound
    /// straight to the asset's SerializedProperty. Used by both the AchievementTierMap Inspector and the
    /// Dashboard's Achievements tab, so they can never drift apart.
    ///
    /// Texts come from I2 Localization terms when configured (with literal fallbacks), previewed in the chosen
    /// I2 language — the same resolution the AppDeployHub exporter uses.
    /// </summary>
    internal sealed class AchievementMapEditorView
    {
        private enum Filter { All, MissingSteam, MissingGoogle, MissingApple, MissingI2, Hidden }

        private static readonly HashSet<(int trophy, int tier)> Expanded = new HashSet<(int, int)>();

        public VisualElement Root { get; }

        private readonly AchievementTierMap _map;
        private readonly SerializedObject _so;
        private readonly List<Action> _rowUpdaters = new List<Action>();

        private VisualElement _summary;
        private VisualElement _list;
        private Label _dirtyLabel;
        private string _language = I2Bridge.DefaultLanguage;
        private string _search = string.Empty;
        private Filter _filter = Filter.All;

        public AchievementMapEditorView(AchievementTierMap map)
        {
            _map = map;
            _so = new SerializedObject(map);

            Root = new VisualElement();
            NativeSocialUIStyle.Apply(Root);

            if (I2Bridge.IsAvailable)
            {
                var languages = I2Bridge.GetLanguages();
                if (languages.Count > 0 && !languages.Contains(_language)) _language = languages[0];
            }

            Root.Add(BuildToolbar());
            _hubContainer = new VisualElement();
            Root.Add(_hubContainer);
            RebuildHubCard();
            _summary = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 8 } };
            Root.Add(_summary);
            _list = new VisualElement();
            Root.Add(_list);

            Root.TrackSerializedObjectValue(_so, _ => { RefreshSummary(); foreach (var update in _rowUpdaters) update(); });
            Root.schedule.Execute(() => { if (_dirtyLabel != null) _dirtyLabel.style.display = EditorUtility.IsDirty(_map) ? DisplayStyle.Flex : DisplayStyle.None; }).Every(400);

            Refresh();
        }

        // ── Toolbar ──────────────────────────────────────────────────────────────

        private VisualElement BuildToolbar()
        {
            var card = NativeSocialUIStyle.CreateCard($"🏆 {_map.name}",
                "Each trophy tier becomes one achievement on Steam / Google Play / Apple. Names and descriptions come from your I2 Localization terms; the literal fields are only a fallback.");

            var top = Row();
            _dirtyLabel = new Label("● unsaved changes") { style = { color = NativeSocialUIStyle.ColorWarning, fontSize = 10, marginRight = 8, display = DisplayStyle.None } };
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            top.Add(_dirtyLabel);
            top.Add(NativeSocialUIStyle.CreateButton("💾 Save", () => { EditorUtility.SetDirty(_map); AssetDatabase.SaveAssetIfDirty(_map); }, primary: true));
            top.Add(NativeSocialUIStyle.CreateButton("📍 Select asset", () => { Selection.activeObject = _map; EditorGUIUtility.PingObject(_map); }));
            card.Add(top);

            var filters = Row();
            var search = new TextField("Search") { value = _search, style = { flexGrow = 1, minWidth = 160, marginRight = 8 } };
            search.RegisterValueChangedCallback(e => { _search = e.newValue ?? string.Empty; RebuildList(); });
            filters.Add(search);

            if (I2Bridge.IsAvailable)
            {
                var languages = I2Bridge.GetLanguages();
                if (languages.Count > 0)
                {
                    var popup = new PopupField<string>("Preview language", languages, Mathf.Max(0, languages.IndexOf(_language)));
                    popup.RegisterValueChangedCallback(e => { _language = e.newValue; foreach (var update in _rowUpdaters) update(); });
                    filters.Add(popup);
                }
            }
            card.Add(filters);

            var chips = Row();
            void AddChip(string label, Filter filter)
            {
                var chip = NativeSocialUIStyle.CreateButton(label, () => { _filter = filter; RebuildList(); UpdateChipStyles(chips); });
                chip.userData = filter;
                chips.Add(chip);
            }
            AddChip("All", Filter.All);
            AddChip("Missing Steam", Filter.MissingSteam);
            AddChip("Missing Google Play", Filter.MissingGoogle);
            AddChip("Missing Apple", Filter.MissingApple);
            AddChip("Missing I2 terms", Filter.MissingI2);
            AddChip("Hidden", Filter.Hidden);
            UpdateChipStyles(chips);
            card.Add(chips);

            var actions = Row();
            var fillBtn = NativeSocialUIStyle.CreateButton("🔗 Fill default term keys", () =>
            {
                var changed = AchievementI2Tools.FillDefaultTermKeys(_map);
                _so.Update();
                Refresh();
                EditorUtility.DisplayDialog("Term keys", changed == 0 ? "Every entry already has its I2 term keys." : $"Set {changed} empty term key(s) to the default convention (trophy{{N}}, Achievements/Trophy{{N}}_{{tier}}/Earned, .../NotEarned).", "OK");
            });
            fillBtn.tooltip = "Fills empty term-key fields: name = trophy{N} (your existing term), earned/not-earned = Achievements/Trophy{N}_{tier}/Earned | NotEarned.";
            actions.Add(fillBtn);

            var genBtn = NativeSocialUIStyle.CreateButton("🛠 Generate missing I2 terms", GenerateMissingTerms, primary: true);
            genBtn.SetEnabled(I2Bridge.IsAvailable);
            genBtn.tooltip = I2Bridge.IsAvailable
                ? "Creates, in your I2 language source, every configured term that doesn't exist yet — English text only, never overwriting an existing translation."
                : "I2 Localization was not found in this project.";
            actions.Add(genBtn);

            actions.Add(NativeSocialUIStyle.CreateButton("⬆ Export for AppDeployHub", () =>
                AchievementExchangeExporter.ExportToFile(_map, PlayerSettings.productName, _language)));
            actions.Add(NativeSocialUIStyle.CreateButton("➕ Add trophy", AddTrophy));
            card.Add(actions);

            if (!I2Bridge.IsAvailable)
                card.Add(NativeSocialUIStyle.CreateCallout("I2 Localization not found in this project — only the literal text fields are used. Install I2 to drive names/descriptions from localization terms.", AuditSeverity.Warning));

            return card;

            VisualElement Row() => new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 6 } };
        }

        private void UpdateChipStyles(VisualElement chips)
        {
            foreach (var child in chips.Children())
            {
                if (child is not Button b || b.userData is not Filter f) continue;
                b.RemoveFromClassList("ns-btn-primary");
                b.RemoveFromClassList("ns-btn-secondary");
                b.AddToClassList(f == _filter ? "ns-btn-primary" : "ns-btn-secondary");
            }
        }

        // ── AppDeployHub (online) ────────────────────────────────────────────────

        private VisualElement _hubContainer;
        private AppDeployHubClient.AppSummary[] _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
        private bool _hubAppsRequested;
        private bool _hubBusy;
        private bool _pushGooglePlay;
        private bool _pushGameCenter;

        private void RebuildHubCard()
        {
            _hubContainer.Clear();
            _hubContainer.Add(BuildAppDeployHubCard());
        }

        private VisualElement BuildAppDeployHubCard()
        {
            var card = NativeSocialUIStyle.CreateCard("☁ AppDeployHub",
                "Send these achievements straight to AppDeployHub: sign in with your AppDeployHub e-mail and password, pick the app, send. No file to upload and no key to copy. Texts go in English plus every other I2 language that has a real translation.");

            var url = new TextField("Server URL") { value = AppDeployHubSettings.BaseUrl, tooltip = "e.g. https://your-appdeployhub.example.com (https required; plain http only for localhost)" };
            url.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (url.value == AppDeployHubSettings.BaseUrl) return;
                AppDeployHubSettings.BaseUrl = url.value;
                _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
                _hubAppsRequested = false;
                RebuildHubCard(); // a session belongs to one server, so the signed-in state may change
            });
            card.Add(url);

            if (AppDeployHubSettings.IsSignedIn) AddSignedInRow(card);
            else AddSignInForm(card, url);

            if (AppDeployHubSettings.IsSignedIn)
            {
                if (_hubApps.Length == 0 && !_hubAppsRequested)
                {
                    _hubAppsRequested = true;
                    LoadHubApps();
                }
                AddAppAndSendControls(card);
            }

            return card;
        }

        private void AddSignInForm(VisualElement card, TextField url)
        {
            var email = new TextField("E-mail") { value = AppDeployHubSettings.Email };
            var password = new TextField("Password") { isPasswordField = true, tooltip = "Used once to sign in. It is never stored or logged." };
            card.Add(email);
            card.Add(password);

            var signIn = NativeSocialUIStyle.CreateButton("🔑 Sign in", null, primary: true);
            signIn.style.marginTop = 6;
            void DoSignIn()
            {
                if (_hubBusy) return;
                AppDeployHubSettings.BaseUrl = url.value;
                var urlError = AppDeployHubClient.ValidateBaseUrl(url.value);
                if (urlError != null) { EditorUtility.DisplayDialog("AppDeployHub", urlError, "OK"); return; }
                if (string.IsNullOrWhiteSpace(email.value) || string.IsNullOrEmpty(password.value))
                {
                    EditorUtility.DisplayDialog("AppDeployHub", "Enter your AppDeployHub e-mail and password.", "OK");
                    return;
                }

                AppDeployHubSettings.Email = email.value;
                var typedPassword = password.value;
                password.value = string.Empty; // never keep the password around

                _hubBusy = true;
                signIn.SetEnabled(false);
                signIn.text = "Signing in…";
                AppDeployHubClient.RunThen(AppDeployHubClient.LoginAsync(email.value, typedPassword), response =>
                {
                    _hubBusy = false;
                    if (!response.Ok)
                    {
                        signIn.SetEnabled(true);
                        signIn.text = "🔑 Sign in";
                        EditorUtility.DisplayDialog("Sign in failed", response.Error ?? "Unknown error.", "OK");
                        return;
                    }

                    var login = AppDeployHubClient.ParseLogin(response.Body);
                    AppDeployHubSettings.StoreToken(login.token);
                    if (!string.IsNullOrEmpty(login.email)) AppDeployHubSettings.Email = login.email;
                    _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
                    _hubAppsRequested = false;
                    RebuildHubCard();
                });
            }
            signIn.clicked += DoSignIn;
            password.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) DoSignIn();
            });
            card.Add(signIn);

            // CI or an account that can't sign in with a password: paste a studio API key instead.
            var advanced = new Foldout { text = "Advanced: use an API key instead (CI, or an account that can't sign in by password)", value = false, style = { marginTop = 6 } };
            var key = new TextField("API key") { isPasswordField = true, tooltip = "Created in AppDeployHub: Studio > API keys. Stored only in your Editor preferences (or read from the " + AppDeployHubSettings.TokenEnvVar + " environment variable)." };
            advanced.Add(key);
            advanced.Add(NativeSocialUIStyle.CreateButton("Use this key", () =>
            {
                if (string.IsNullOrWhiteSpace(key.value)) return;
                AppDeployHubSettings.BaseUrl = url.value;
                AppDeployHubSettings.StoreToken(key.value);
                _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
                _hubAppsRequested = false;
                RebuildHubCard();
            }));
            card.Add(advanced);
        }

        private void AddSignedInRow(VisualElement card)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 6 } };
            var who = AppDeployHubSettings.TokenFromEnvironment
                ? $"✔ Using the key from the {AppDeployHubSettings.TokenEnvVar} environment variable"
                : $"✔ Signed in{(string.IsNullOrEmpty(AppDeployHubSettings.Email) ? string.Empty : " as " + AppDeployHubSettings.Email)}";
            row.Add(new Label(who) { style = { flexGrow = 1, color = NativeSocialUIStyle.ColorSuccess } });

            if (!AppDeployHubSettings.TokenFromEnvironment)
            {
                row.Add(NativeSocialUIStyle.CreateButton("Sign out", () =>
                {
                    // Tell the server to drop this machine's key (best effort), then forget it locally either way.
                    AppDeployHubClient.RunThen(AppDeployHubClient.LogoutAsync(), _ => { });
                    AppDeployHubSettings.ClearSession();
                    _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
                    _hubAppsRequested = false;
                    RebuildHubCard();
                }));
            }
            card.Add(row);
        }

        private void LoadHubApps()
        {
            AppDeployHubClient.RunThen(AppDeployHubClient.GetAppsAsync(), response =>
            {
                if (!response.Ok) { HandleHubError(response); return; }

                _hubApps = AppDeployHubClient.ParseApps(response.Body);
                if (_hubApps.Length == 0)
                {
                    RebuildHubCard();
                    EditorUtility.DisplayDialog("AppDeployHub", "Signed in, but your account has no apps yet. Add the app in AppDeployHub first.", "OK");
                    return;
                }

                // Prefer the saved app, then the one whose package name matches this project, then the first.
                var selected = _hubApps.FirstOrDefault(a => a.id == AppDeployHubSettings.AppId)
                               ?? _hubApps.FirstOrDefault(a => !string.IsNullOrEmpty(a.packageName) && a.packageName == PlayerSettings.applicationIdentifier)
                               ?? _hubApps[0];
                AppDeployHubSettings.SetApp(selected.id, selected.name);
                RebuildHubCard();
            });
        }

        /// <summary>Shows a failed call; a 401 means the stored session is dead, so drop it and show the sign-in form again.</summary>
        private void HandleHubError(AppDeployHubClient.Response response)
        {
            if (response.Status == 401 && !AppDeployHubSettings.TokenFromEnvironment)
            {
                AppDeployHubSettings.ClearSession();
                _hubApps = Array.Empty<AppDeployHubClient.AppSummary>();
                _hubAppsRequested = false;
                RebuildHubCard();
            }
            EditorUtility.DisplayDialog("AppDeployHub", response.Error ?? "Unknown error.", "OK");
        }

        private void AddAppAndSendControls(VisualElement card)
        {
            var appRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 6 } };

            if (_hubApps.Length > 0)
            {
                var names = _hubApps.Select(a => $"{a.studioName} / {a.name}" + (string.IsNullOrEmpty(a.packageName) ? string.Empty : $"  ({a.packageName})")).ToList();
                var current = Mathf.Max(0, Array.FindIndex(_hubApps, a => a.id == AppDeployHubSettings.AppId));
                var popup = new PopupField<string>("App", names, current) { style = { flexGrow = 1 } };
                popup.RegisterValueChangedCallback(e =>
                {
                    var chosen = _hubApps[names.IndexOf(e.newValue)];
                    AppDeployHubSettings.SetApp(chosen.id, chosen.name);
                });
                appRow.Add(popup);
            }
            else
            {
                appRow.Add(new Label(_hubAppsRequested ? "Loading your apps…" : "No apps loaded.") { style = { flexGrow = 1, color = NativeSocialUIStyle.ColorTextMuted } });
            }

            appRow.Add(NativeSocialUIStyle.CreateButton("↻ Refresh apps", () =>
            {
                _hubAppsRequested = true;
                LoadHubApps();
            }));
            card.Add(appRow);

            var pushGp = new Toggle("Also queue the Google Play push (creates the achievements on the store)") { value = _pushGooglePlay };
            pushGp.RegisterValueChangedCallback(e => _pushGooglePlay = e.newValue);
            var pushGc = new Toggle("Also queue the Apple Game Center push (creates the achievements on the store)") { value = _pushGameCenter };
            pushGc.RegisterValueChangedCallback(e => _pushGameCenter = e.newValue);
            pushGp.style.marginTop = 6;
            card.Add(pushGp);
            card.Add(pushGc);

            var sendBtn = NativeSocialUIStyle.CreateButton("☁ Send to AppDeployHub", null, primary: true);
            sendBtn.style.marginTop = 8;
            sendBtn.clicked += () => SendToAppDeployHub(sendBtn);
            card.Add(sendBtn);
        }

        private void SendToAppDeployHub(Button sendBtn)
        {
            if (_hubBusy) return;

            if (string.IsNullOrEmpty(AppDeployHubSettings.AppId))
            {
                EditorUtility.DisplayDialog("AppDeployHub", "Pick the app first (use \"Refresh apps\" if the list is empty).", "OK");
                return;
            }
            if (_map.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog("AppDeployHub", "This map has no achievements to send.", "OK");
                return;
            }

            var language = I2Bridge.DefaultLanguage;
            var extraLocales = I2Bridge.IsAvailable
                ? _map.Entries.SelectMany(e => AchievementExchangeExporter.BuildLocalizations(_map, e, language)).Select(l => l.locale).Distinct().ToList()
                : new List<string>();

            var message = $"Send {_map.Entries.Count} tier(s) to the AppDeployHub app \"{AppDeployHubSettings.AppName}\"?\n\n" +
                          $"Texts: {language}" + (extraLocales.Count > 0 ? $" + {extraLocales.Count} other language(s) ({string.Join(", ", extraLocales)})" : " only") + ".\n" +
                          "This creates or updates the achievement rows in AppDeployHub (matched by key). Nothing is deleted.";
            if (_pushGooglePlay || _pushGameCenter)
                message += "\n\n⚠ You also chose to QUEUE A STORE PUSH: " +
                           string.Join(" and ", new[] { _pushGooglePlay ? "Google Play" : null, _pushGameCenter ? "Apple Game Center" : null }.Where(x => x != null)) +
                           " — that creates the achievements for real on the store console.";
            if (!EditorUtility.DisplayDialog("Send to AppDeployHub", message, "Send", "Cancel")) return;

            var json = AchievementExchangeExporter.BuildJson(_map, PlayerSettings.productName, AchievementExchangeExporter.LocaleFor(language), language);

            _hubBusy = true;
            sendBtn.SetEnabled(false);
            sendBtn.text = "Sending…";
            AppDeployHubClient.RunThen(
                AppDeployHubClient.ImportAsync(AppDeployHubSettings.AppId, json, _pushGooglePlay, _pushGameCenter),
                response =>
                {
                    _hubBusy = false;
                    sendBtn.SetEnabled(true);
                    sendBtn.text = "☁ Send to AppDeployHub";

                    if (!response.Ok)
                    {
                        HandleHubError(response);
                        return;
                    }

                    var r = AppDeployHubClient.ParseImportResult(response.Body);
                    EditorUtility.DisplayDialog("Sent to AppDeployHub",
                        $"Google Play: {r.googlePlayCreated} created, {r.googlePlayUpdated} updated\n" +
                        $"Apple Game Center: {r.gameCenterCreated} created, {r.gameCenterUpdated} updated\n" +
                        $"Languages: {(r.locales == null ? "?" : string.Join(", ", r.locales))}\n" +
                        (r.googlePlayPushQueued || r.gameCenterPushQueued ? "\nStore push queued — check the job in AppDeployHub." : "\nNothing was pushed to a store: open the app's Achievements page in AppDeployHub to review and push."),
                        "OK");
                });
        }

        // ── Summary ──────────────────────────────────────────────────────────────

        private void RefreshSummary()
        {
            _summary.Clear();
            int total = _map.Entries.Count;
            int trophies = _map.Entries.Select(e => e.TrophyNumber).Distinct().Count();

            void Add(string label, int count)
            {
                var severity = total > 0 && count == total ? AuditSeverity.Pass : AuditSeverity.Warning;
                var badge = NativeSocialUIStyle.CreateBadge($"{label} {count}/{total}", severity);
                badge.style.marginRight = 6;
                badge.style.marginBottom = 4;
                _summary.Add(badge);
            }

            var info = NativeSocialUIStyle.CreateBadge($"🏆 {trophies} trophies · {total} tiers", AuditSeverity.Info);
            info.style.marginRight = 6;
            info.style.marginBottom = 4;
            _summary.Add(info);

            Add("Steam", _map.Entries.Count(e => !string.IsNullOrEmpty(e.SteamStat)));
            Add("Google Play", _map.Entries.Count(e => !string.IsNullOrEmpty(e.GooglePlayId)));
            Add("Apple", _map.Entries.Count(e => !string.IsNullOrEmpty(e.AppleId)));

            if (I2Bridge.IsAvailable)
            {
                Add("I2 names", _map.Entries.Count(e => I2Bridge.TermExists(e.NameTerm)));
                Add("I2 earned", _map.Entries.Count(e => I2Bridge.TermExists(e.EarnedDescriptionTerm)));
                Add("I2 not earned", _map.Entries.Count(e => I2Bridge.TermExists(e.NotEarnedDescriptionTerm)));
            }
        }

        // ── List ─────────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _so.Update();
            RefreshSummary();
            RebuildList();
        }

        private void RebuildList()
        {
            _rowUpdaters.Clear();
            _list.Clear();

            if (_map.Entries.Count == 0)
            {
                _list.Add(NativeSocialUIStyle.CreateCallout("No trophies yet. Use \"➕ Add trophy\" to create the first one.", AuditSeverity.Info));
                return;
            }

            var groups = Enumerable.Range(0, _map.Entries.Count)
                .GroupBy(i => _map.Entries[i].TrophyNumber)
                .OrderBy(g => g.Key);

            int shown = 0;
            foreach (var group in groups)
            {
                var indices = group.Where(i => Matches(_map.Entries[i])).OrderBy(i => _map.Entries[i].Tier).ToList();
                if (indices.Count == 0) continue;
                _list.Add(BuildTrophyCard(group.Key, indices));
                shown++;
            }

            if (shown == 0)
                _list.Add(NativeSocialUIStyle.CreateCallout("No tiers match the current filter/search.", AuditSeverity.Info));

            Root.Bind(_so);
        }

        private bool Matches(AchievementTierEntry e)
        {
            switch (_filter)
            {
                case Filter.MissingSteam when !string.IsNullOrEmpty(e.SteamStat): return false;
                case Filter.MissingGoogle when !string.IsNullOrEmpty(e.GooglePlayId): return false;
                case Filter.MissingApple when !string.IsNullOrEmpty(e.AppleId): return false;
                case Filter.MissingI2 when I2Status(e) == 3: return false;
                case Filter.Hidden when !e.IsHidden: return false;
            }

            if (string.IsNullOrWhiteSpace(_search)) return true;
            var haystack = string.Join(" ", e.TrophyNumber, AchievementTextResolver.Name(_map, e, _language).Text,
                e.SteamStat, e.GooglePlayId, e.AppleId, e.NameTerm, e.EarnedDescriptionTerm, e.NotEarnedDescriptionTerm);
            return haystack.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>How many of the 3 text fields (name/earned/not earned) resolve from a real I2 term: 0..3.</summary>
        private static int I2Status(AchievementTierEntry e) =>
            (I2Bridge.TermExists(e.NameTerm) ? 1 : 0) + (I2Bridge.TermExists(e.EarnedDescriptionTerm) ? 1 : 0) + (I2Bridge.TermExists(e.NotEarnedDescriptionTerm) ? 1 : 0);

        private VisualElement BuildTrophyCard(int trophyNumber, List<int> indices)
        {
            var first = _map.Entries[indices[0]];

            var card = new VisualElement();
            card.AddToClassList("ns-card");
            card.style.marginBottom = 8;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            header.Add(NativeSocialUIStyle.CreateBadge($"#{trophyNumber}", AuditSeverity.Info));
            var title = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginLeft = 8, flexGrow = 1 } };
            header.Add(title);
            card.Add(header);

            void UpdateTitle()
            {
                var baseName = AchievementTextResolver.Resolve(first.NameTerm, first.DisplayName, _language);
                title.text = string.IsNullOrEmpty(baseName.Text) ? "(no name yet)" : Regex.Replace(baseName.Text, @"\s+[IVXLCDM]+$", string.Empty);
            }
            UpdateTitle();
            _rowUpdaters.Add(UpdateTitle);

            foreach (var index in indices)
                card.Add(BuildTierRow(index));

            return card;
        }

        private VisualElement BuildTierRow(int index)
        {
            var entryProp = _so.FindProperty("Entries").GetArrayElementAtIndex(index);

            var container = new VisualElement { style = { marginTop = 4 } };
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.FlexStart, paddingTop = 6, paddingBottom = 6, paddingLeft = 8, paddingRight = 8,
                    backgroundColor = new Color(1f, 1f, 1f, 0.03f), borderLeftWidth = 3
                }
            };
            row.SetRadius(4);

            var tierLabel = new Label { style = { width = 34, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, color = NativeSocialUIStyle.ColorPrimary, unityTextAlign = TextAnchor.MiddleLeft } };
            row.Add(tierLabel);

            var textCol = new VisualElement { style = { flexGrow = 1, flexShrink = 1, marginRight = 8 } };
            var nameLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal } };
            var earnedLabel = new Label { style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, marginTop = 2 } };
            var notEarnedLabel = new Label { style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, marginTop = 1 } };
            textCol.Add(nameLabel);
            textCol.Add(earnedLabel);
            textCol.Add(notEarnedLabel);
            row.Add(textCol);

            var chipCol = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, justifyContent = Justify.FlexEnd, flexShrink = 0, maxWidth = 330 } };
            row.Add(chipCol);

            var editBtn = NativeSocialUIStyle.CreateButton("✎", null);
            editBtn.tooltip = "Edit this tier";
            editBtn.style.marginLeft = 6;
            row.Add(editBtn);
            container.Add(row);

            var key = (_map.Entries[index].TrophyNumber, _map.Entries[index].Tier);
            var panel = BuildEditPanel(index, entryProp);
            panel.style.display = Expanded.Contains(key) ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(panel);
            editBtn.clicked += () =>
            {
                var open = panel.style.display == DisplayStyle.None;
                panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) Expanded.Add(key); else Expanded.Remove(key);
            };

            void Update()
            {
                if (index >= _map.Entries.Count) return;
                var e = _map.Entries[index];
                var name = AchievementTextResolver.Name(_map, e, _language);
                var earned = AchievementTextResolver.Earned(e, _language);
                var notEarned = AchievementTextResolver.NotEarned(e, _language);

                tierLabel.text = AchievementTierMap.RomanNumeral(e.Tier);
                nameLabel.text = $"{(string.IsNullOrEmpty(name.Text) ? "(no name)" : name.Text)}   ·   {e.Points} pts{(e.IsHidden ? "   ·   hidden" : string.Empty)}";
                StyleText(earnedLabel, "✔ Earned: ", earned);
                StyleText(notEarnedLabel, "○ Not earned: ", notEarned);

                chipCol.Clear();
                chipCol.Add(Chip("Steam", !string.IsNullOrEmpty(e.SteamStat)));
                chipCol.Add(Chip("Google", !string.IsNullOrEmpty(e.GooglePlayId)));
                chipCol.Add(Chip("Apple", !string.IsNullOrEmpty(e.AppleId)));
                if (I2Bridge.IsAvailable)
                {
                    int i2 = I2Status(e);
                    var badge = NativeSocialUIStyle.CreateBadge($"I2 {i2}/3", i2 == 3 ? AuditSeverity.Pass : AuditSeverity.Warning);
                    badge.tooltip = "How many of name / earned / not-earned resolve from a real I2 term.";
                    badge.style.marginLeft = 4;
                    chipCol.Add(badge);
                }

                var complete = !string.IsNullOrEmpty(e.SteamStat) && !string.IsNullOrEmpty(e.GooglePlayId) && !string.IsNullOrEmpty(e.AppleId);
                row.style.borderLeftColor = complete ? NativeSocialUIStyle.ColorSuccess : NativeSocialUIStyle.ColorWarning;
            }

            Update();
            _rowUpdaters.Add(Update);
            return container;
        }

        private static void StyleText(Label label, string prefix, ResolvedText text)
        {
            if (text.Source == TextSource.Missing)
            {
                label.text = prefix + "(missing)";
                label.style.color = NativeSocialUIStyle.ColorError;
                return;
            }

            label.text = prefix + text.Text + (text.Source == TextSource.Literal && text.TermUnresolved ? "   [literal — term has no translation]" : text.Source == TextSource.Literal ? "   [literal]" : string.Empty);
            label.style.color = text.Source == TextSource.I2 ? NativeSocialUIStyle.ColorTextMuted : NativeSocialUIStyle.ColorWarning;
        }

        private static Label Chip(string name, bool ok)
        {
            var badge = NativeSocialUIStyle.CreateBadge($"{name} {(ok ? "✔" : "✘")}", ok ? AuditSeverity.Pass : AuditSeverity.Warning);
            badge.style.marginLeft = 4;
            badge.style.marginBottom = 2;
            return badge;
        }

        private VisualElement BuildEditPanel(int index, SerializedProperty entryProp)
        {
            var panel = new VisualElement { style = { marginTop = 2, marginLeft = 12, paddingTop = 8, paddingBottom = 8, paddingLeft = 10, paddingRight = 10, backgroundColor = new Color(0f, 0f, 0f, 0.18f) } };
            panel.SetRadius(4);

            void Section(string title, params string[] fields)
            {
                panel.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 10, color = NativeSocialUIStyle.ColorTextMuted, marginTop = 6 } });
                foreach (var field in fields)
                {
                    var prop = entryProp.FindPropertyRelative(field);
                    if (prop != null) panel.Add(new PropertyField(prop));
                }
            }

            Section("IDENTITY", "TrophyNumber", "Tier", "Points", "IsHidden");
            Section("PLATFORM IDS", "SteamStat", "GooglePlayId", "AppleId");
            Section("I2 LOCALIZATION TERMS (preferred)", "NameTerm", "EarnedDescriptionTerm", "NotEarnedDescriptionTerm");
            Section("LITERAL FALLBACKS (used only when a term is empty or has no translation)", "DisplayName", "EarnedDescription", "NotEarnedDescription");

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            buttons.Add(NativeSocialUIStyle.CreateButton("⧉ Duplicate as next tier", () => DuplicateTier(index)));
            buttons.Add(NativeSocialUIStyle.CreateButton("🗑 Remove tier", () => RemoveTier(index)));
            panel.Add(buttons);
            return panel;
        }

        // ── Structural edits ─────────────────────────────────────────────────────

        private void AddTrophy()
        {
            Undo.RecordObject(_map, "Add trophy");
            int next = _map.Entries.Count == 0 ? 1 : _map.Entries.Max(e => e.TrophyNumber) + 1;
            for (int tier = 1; tier <= 3; tier++)
            {
                _map.Entries.Add(new AchievementTierEntry
                {
                    TrophyNumber = next,
                    Tier = tier,
                    Points = tier * 10,
                    NameTerm = AchievementTierMap.DefaultNameTerm(next),
                    EarnedDescriptionTerm = AchievementTierMap.DefaultEarnedTerm(next, tier),
                    NotEarnedDescriptionTerm = AchievementTierMap.DefaultNotEarnedTerm(next, tier)
                });
            }
            EditorUtility.SetDirty(_map);
            Expanded.Add((next, 1));
            Refresh();
        }

        private void DuplicateTier(int index)
        {
            Undo.RecordObject(_map, "Duplicate tier");
            var source = _map.Entries[index];
            int nextTier = _map.Entries.Where(e => e.TrophyNumber == source.TrophyNumber).Max(e => e.Tier) + 1;
            var copy = source;
            copy.Tier = nextTier;
            copy.SteamStat = copy.GooglePlayId = copy.AppleId = string.Empty;
            copy.EarnedDescriptionTerm = AchievementTierMap.DefaultEarnedTerm(copy.TrophyNumber, nextTier);
            copy.NotEarnedDescriptionTerm = AchievementTierMap.DefaultNotEarnedTerm(copy.TrophyNumber, nextTier);
            _map.Entries.Insert(index + 1, copy);
            EditorUtility.SetDirty(_map);
            Expanded.Add((copy.TrophyNumber, nextTier));
            Refresh();
        }

        private void RemoveTier(int index)
        {
            var e = _map.Entries[index];
            if (!EditorUtility.DisplayDialog("Remove tier", $"Remove trophy #{e.TrophyNumber} tier {e.Tier}?", "Remove", "Cancel")) return;
            Undo.RecordObject(_map, "Remove tier");
            _map.Entries.RemoveAt(index);
            EditorUtility.SetDirty(_map);
            Refresh();
        }

        private void GenerateMissingTerms()
        {
            var missing = AchievementI2Tools.FindMissingTerms(_map);
            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog("Generate I2 terms", "Every configured term already exists in I2 (or no term keys are set — use \"Fill default term keys\" first).", "OK");
                return;
            }

            var preview = string.Join("\n", missing.Take(8).Select(m => $"• {m.Key}  =  \"{m.Value}\""));
            if (missing.Count > 8) preview += $"\n… and {missing.Count - 8} more";
            if (!EditorUtility.DisplayDialog("Generate missing I2 terms",
                    $"{missing.Count} term(s) don't exist in I2 yet and will be created with ENGLISH text only (other languages stay empty — use I2's translate tools). Existing translations are never overwritten.\n\n{preview}",
                    "Create terms", "Cancel")) return;

            int created = AchievementI2Tools.GenerateMissingTerms(_map);
            Refresh();
            EditorUtility.DisplayDialog("Generate I2 terms", $"Created {created} term(s) in the I2 language source.", "OK");
        }
    }
}
