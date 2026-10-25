using System;
using System.Linq;
using UnityEngine;

namespace SwarmPrototype
{
    // A local display name only. Online account identity and ratings belong to the server.
    public static class LocalPlayerProfile
    {
        public const string NameKey = "Commoners.DisplayName";
        public const int MaxNameLength = 20;
        static string displayName;
        public static event Action Changed;
        public static string DisplayName
        {
            get
            {
                if (displayName == null)
                {
                    string saved = PlayerPrefs.GetString(NameKey, "Commander");
                    displayName = Validate(saved) == null ? saved.Trim() : "Commander";
                }
                return displayName;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { displayName = null; Changed = null; }

        public static string Validate(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 3 || name.Length > MaxNameLength)
                return "Use 3–20 characters for your username.";
            if (!name.Any(char.IsLetterOrDigit) || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-'))
                return "Use letters, numbers, spaces, underscores or hyphens.";
            return null;
        }

        public static string Rename(string name)
        {
            string error = Validate(name);
            if (error != null) return error;
            displayName = name.Trim();
            PlayerPrefs.SetString(NameKey, displayName);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return null;
        }
    }
}
