using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;

namespace Wagenheimer.NativeSocial.Editor
{
    /// <summary>
    /// Reflection-only wrapper over I2 Localization's term/language API. This package must still compile when
    /// I2 isn't installed (no Odin / I2 dependency in Runtime or Editor — see AGENTS.md), so nothing here ever
    /// writes <c>using I2.Loc;</c>. Same technique as UnityRewiredHelper's I2Api; every member degrades to
    /// "not available" instead of throwing when I2 (or a differing I2 version) isn't there.
    /// </summary>
    internal static class I2Bridge
    {
        public const string DefaultLanguage = "English";

        private static Type _locManager, _sourceType, _termDataType, _languageDataType;
        private static MethodInfo _updateSources, _getTermData, _addTerm, _getLanguageIndex, _addLanguage, _setTranslation, _editorSetDirty;
        private static FieldInfo _sourcesField, _termLanguages, _sourceLanguagesField, _languageName, _languageCode;
        private static bool _resolved;

        public static bool IsAvailable
        {
            get
            {
                Resolve();
                return _sourceType != null && _getTermData != null && _getLanguageIndex != null && _termLanguages != null && _sourcesField != null;
            }
        }

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            _locManager = FindType("I2.Loc.LocalizationManager");
            _sourceType = FindType("I2.Loc.LanguageSourceData");
            _termDataType = FindType("I2.Loc.TermData");
            _languageDataType = FindType("I2.Loc.LanguageData");
            if (_locManager == null || _sourceType == null || _termDataType == null) return;

            _updateSources = _locManager.GetMethod("UpdateSources", BindingFlags.Public | BindingFlags.Static);
            _sourcesField = _locManager.GetField("Sources", BindingFlags.Public | BindingFlags.Static);
            _getTermData = _sourceType.GetMethod("GetTermData", new[] { typeof(string), typeof(bool) });
            _addTerm = _sourceType.GetMethod("AddTerm", new[] { typeof(string) });
            _getLanguageIndex = _sourceType.GetMethod("GetLanguageIndex", new[] { typeof(string), typeof(bool), typeof(bool) });
            _addLanguage = _sourceType.GetMethod("AddLanguage", new[] { typeof(string) });
            _editorSetDirty = _sourceType.GetMethod("Editor_SetDirty", BindingFlags.Public | BindingFlags.Instance);
            _sourceLanguagesField = _sourceType.GetField("mLanguages", BindingFlags.Public | BindingFlags.Instance);
            _setTranslation = _termDataType.GetMethod("SetTranslation", new[] { typeof(int), typeof(string), typeof(string) });
            _termLanguages = _termDataType.GetField("Languages", BindingFlags.Public | BindingFlags.Instance);
            _languageName = _languageDataType?.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
            _languageCode = _languageDataType?.GetField("Code", BindingFlags.Public | BindingFlags.Instance);
        }

        private static IList Sources()
        {
            if (!IsAvailable) return null;
            _updateSources?.Invoke(null, null);
            return _sourcesField.GetValue(null) as IList;
        }

        /// <summary>The language names defined in the project's I2 source (e.g. "English", "Portuguese"...).</summary>
        public static List<string> GetLanguages()
        {
            var result = new List<string>();
            var sources = Sources();
            if (sources == null || sources.Count == 0 || _sourceLanguagesField == null || _languageName == null) return result;

            if (_sourceLanguagesField.GetValue(sources[0]) is IEnumerable languages)
                foreach (var l in languages)
                    result.Add((string)_languageName.GetValue(l));
            return result;
        }

        /// <summary>I2's language code for a language name (e.g. "English" → "en"), or null.</summary>
        public static string GetLanguageCode(string languageName)
        {
            var sources = Sources();
            if (sources == null || sources.Count == 0 || _sourceLanguagesField == null || _languageName == null || _languageCode == null) return null;

            if (_sourceLanguagesField.GetValue(sources[0]) is IEnumerable languages)
                foreach (var l in languages)
                    if ((string)_languageName.GetValue(l) == languageName)
                        return (string)_languageCode.GetValue(l);
            return null;
        }

        public static bool TermExists(string term)
        {
            if (string.IsNullOrEmpty(term)) return false;
            var sources = Sources();
            if (sources == null) return false;
            foreach (var s in sources)
                if (_getTermData.Invoke(s, new object[] { term, false }) != null) return true;
            return false;
        }

        /// <summary>The translation of <paramref name="term"/> in <paramref name="language"/>, or null if the term or that translation is missing/empty.</summary>
        public static string GetTranslation(string term, string language)
        {
            if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(language)) return null;
            var sources = Sources();
            if (sources == null) return null;

            foreach (var s in sources)
            {
                var termData = _getTermData.Invoke(s, new object[] { term, false });
                if (termData == null) continue;

                var langIdx = (int)_getLanguageIndex.Invoke(s, new object[] { language, true, true });
                if (langIdx < 0) continue;

                var languages = _termLanguages.GetValue(termData) as string[];
                if (languages != null && langIdx < languages.Length && !string.IsNullOrEmpty(languages[langIdx]))
                    return languages[langIdx];
            }
            return null;
        }

        /// <summary>
        /// Makes sure <paramref name="term"/> exists (created in the project's first language source if not) and
        /// has a non-empty English translation, seeding it with <paramref name="englishText"/> only when empty —
        /// it never overwrites an existing translation. Returns true when anything was written.
        /// </summary>
        public static bool EnsureTerm(string term, string englishText, out bool termCreated)
        {
            termCreated = false;
            var sources = Sources();
            if (sources == null || sources.Count == 0 || _addTerm == null || _setTranslation == null) return false;

            object termData = null, owner = null;
            foreach (var s in sources)
            {
                var d = _getTermData.Invoke(s, new object[] { term, false });
                if (d == null) continue;
                termData = d;
                owner = s;
                break;
            }

            if (termData == null)
            {
                owner = sources[0];
                termData = _addTerm.Invoke(owner, new object[] { term });
                termCreated = true;
            }

            var langIdx = (int)_getLanguageIndex.Invoke(owner, new object[] { DefaultLanguage, true, true });
            if (langIdx < 0)
            {
                _addLanguage?.Invoke(owner, new object[] { DefaultLanguage });
                langIdx = (int)_getLanguageIndex.Invoke(owner, new object[] { DefaultLanguage, true, true });
                if (langIdx < 0) return termCreated;
            }

            var languages = _termLanguages.GetValue(termData) as string[];
            if (languages != null && langIdx < languages.Length && !string.IsNullOrEmpty(languages[langIdx]))
                return termCreated;

            _setTranslation.Invoke(termData, new object[] { langIdx, englishText ?? string.Empty, null });
            return true;
        }

        /// <summary>Marks every source dirty and saves, so generated terms actually persist.</summary>
        public static void SaveSources()
        {
            var sources = Sources();
            if (sources == null) return;
            foreach (var s in sources) _editorSetDirty?.Invoke(s, null);
            AssetDatabase.SaveAssets();
        }
    }
}
