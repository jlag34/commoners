using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwarmPrototype
{
    [InitializeOnLoad]
    public static class ProfileRankedCheck
    {
        const string Active = "Commoners.ProfileRankedCheck";
        static int step;
        static double next;
        static string errors = "";
        static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static ProfileRankedCheck()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors += message + "\n";
            };
        }
        [MenuItem("Commoners/Check profile and ranked")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play before running profile checks."); return; }
            SessionState.SetBool(Active + ".hadName", PlayerPrefs.HasKey(LocalPlayerProfile.NameKey));
            SessionState.SetString(Active + ".savedName", PlayerPrefs.GetString(LocalPlayerProfile.NameKey, "Commander"));
            SessionState.SetBool(Active, true);
            step = 0; next = 0; errors = "";
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!SessionState.GetBool(Active, false))
            {
                string trigger = Path.Combine(Project, "profile-ranked-check.trigger");
                if (File.Exists(trigger) && !EditorApplication.isPlaying) { File.Delete(trigger); Run(); }
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.7;
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                var social = UnityEngine.Object.FindObjectOfType<SocialOverlay>();
                switch (step)
                {
                    case 0:
                        Require(menu != null, "Menu booted"); Click("Start");
                        Require(LocalPlayerProfile.Validate("ab") != null && LocalPlayerProfile.Validate("---") != null, "Short and empty names rejected");
                        Require(LocalPlayerProfile.Validate("<b>name</b>") != null && LocalPlayerProfile.Validate(new string('x', 21)) != null, "Markup and oversized names rejected");
                        Click("Player identity"); break;
                    case 1:
                        Require(GameObject.Find("Your profile modal") != null, "Profile card opens editor");
                        Require(!GameObject.Find("Character home").GetComponent<CanvasGroup>().interactable, "Profile blocks background controls");
                        var input = UnityEngine.Object.FindObjectsOfType<TMP_InputField>().Single(i => i.name == "Username draft");
                        Require(!input.richText && input.characterLimit == 20, "Input accepts literal bounded text");
                        input.text = "<bad>"; Click("Save username");
                        Require(GameObject.Find("Your profile modal") != null && Text("Username feedback").text.Contains("letters"), "Invalid save stays open with feedback");
                        input.text = "  Rook_27  "; Click("Save username"); break;
                    case 2:
                        Require(LocalPlayerProfile.DisplayName == "Rook_27" && PlayerPrefs.GetString(LocalPlayerProfile.NameKey) == "Rook_27", "Trimmed name persisted");
                        Require(Text("Profile username").text == "Rook_27" && !Text("Profile username").richText, "Top-left name updates immediately");
                        Require(social.Data.PlayerName == "Rook_27" && social.Data.RequestFriend("rook_27") != null, "Social uses current identity");
                        string thread = SocialDemoState.DirectThread("Mira"); social.Data.Send(thread, "Profile test");
                        Require(social.Data.History(thread).Where(m => m.Mine).All(m => m.Author == "Rook_27"), "Own chat authors use username");
                        social.Data.Join("oak"); Click("Clan tab");
                        Require(UnityEngine.Object.FindObjectsOfType<TMP_Text>().Any(t => t.text.Contains("Rook_27 (you)")), "Clan roster uses username");
                        Click("Close social"); social.Data.Leave(); Click("Settings"); Click("Edit username");
                        UnityEngine.Object.FindObjectsOfType<TMP_InputField>().Single(i => i.name == "Username draft").text = "CancelledName";
                        Click("Cancel username"); Require(LocalPlayerProfile.DisplayName == "Rook_27", "Cancel preserves saved name");
                        Click("Ranked mode"); break;
                    case 3:
                        Require(!Button("Play").interactable && Button("Play").GetComponentInChildren<TMP_Text>().text == "RANKED LOCKED", "Ranked cannot start local bots");
                        menu.Play(); Click("Match options");
                        Require(GameObject.Find("Ranked play modal") != null, "Ranked explanation available");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "screenshot-profile-ranked-details.png"), 2); break;
                    case 4:
                        Require(UnityEngine.Object.FindObjectOfType<MainMenu>() != null && Combatant.All.Count == 0, "Public Play also guards ranked");
                        Click("Ranked done");
                        Require(EventSystem.current.currentSelectedGameObject == Button("Ranked mode").gameObject, "Closing ranked details restores usable gamepad focus");
                        Click("Practice mode");
                        Require(Button("Play").interactable, "Practice remains available");
                        Click("Play"); break;
                    case 5:
                        Require(UnityEngine.Object.FindObjectOfType<MainMenu>() == null && GameState.TeamCount >= 2, "Practice launches");
                        Require(social.Data.PlayerName == "Rook_27", "Identity persists into match");
                        Bootstrap.ShowMenu(); break;
                    case 6:
                        Require(Text("Profile username").text == "Rook_27", "Name survives menu recreation");
                        Require(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length == 1, "One input event system");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "screenshot-profile-ranked-home.png"), 2); break;
                    case 7:
                        Finish(errors.Length == 0 ? "PASS: username validation, save, cancel, profile/chat/clan identity, match return, ranked lock and practice launch.\nNo runtime errors." : "FAIL: " + errors); return;
                }
                step++;
            }
            catch (Exception exception) { Finish("FAIL at step " + step + ": " + exception); }
        }
        static TMP_Text Text(string name) => UnityEngine.Object.FindObjectsOfType<TMP_Text>().Single(t => t.name == name);
        static Button Button(string name) => UnityEngine.Object.FindObjectsOfType<Button>().Single(b => b.name == name);
        static void Click(string name) => Button(name).onClick.Invoke();
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Debug.Log("ProfileRankedCheck: " + message); }
        static void Finish(string result)
        {
            SessionState.SetBool(Active, false);
            string saved = SessionState.GetString(Active + ".savedName", "Commander");
            LocalPlayerProfile.Rename(LocalPlayerProfile.Validate(saved) == null ? saved : "Commander");
            if (SessionState.GetBool(Active + ".hadName", false)) PlayerPrefs.SetString(LocalPlayerProfile.NameKey, saved);
            else PlayerPrefs.DeleteKey(LocalPlayerProfile.NameKey);
            PlayerPrefs.Save();
            File.WriteAllText(Path.Combine(Output, "profile-ranked-check.log"), result);
            Debug.Log("ProfileRankedCheck: " + result);
            if (result.StartsWith("PASS"))
            {
                UnityEngine.Object.FindObjectOfType<MainMenu>().ShowProfile();
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, "screenshot-profile-ranked-editor.png"), 2);
            }
        }
    }
}
