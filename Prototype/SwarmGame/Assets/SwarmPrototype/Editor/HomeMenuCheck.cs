using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Focused integration smoke check for the splash + home hub, runnable from an already-open editor.
    // Touch <project>/home-menu-check.trigger or use Commoners > Check home flow.
    [InitializeOnLoad]
    public static class HomeMenuCheck
    {
        const string Active = "Commoners.HomeMenuCheck";
        static int step;
        static double next;
        static string errors = "";
        static Action afterCapture;
        static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        static HomeMenuCheck()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    errors += message + "\n";
            };
        }

        [MenuItem("Commoners/Check home flow")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play before running the home-flow check."); return; }
            step = 0; errors = ""; afterCapture = null; next = 0;
            SavePref("Commoners.Graphics"); SavePref("SwarmPrototype.Players");
            SessionState.SetInt(Active + ".quality", QualitySettings.GetQualityLevel());
            SessionState.SetFloat(Active + ".volume", AudioListener.volume);
            SessionState.SetBool(Active + ".hadVolume", PlayerPrefs.HasKey("Commoners.Volume"));
            SessionState.SetFloat(Active + ".savedVolume", PlayerPrefs.GetFloat("Commoners.Volume", 1));
            SessionState.SetBool(Active + ".hadName", PlayerPrefs.HasKey(LocalPlayerProfile.NameKey));
            SessionState.SetString(Active + ".savedName", PlayerPrefs.GetString(LocalPlayerProfile.NameKey, "Commander"));
            SessionState.SetBool(Active, true);
            EditorApplication.isPlaying = true;
        }

        static void SavePref(string key)
        {
            SessionState.SetBool(Active + key + ".exists", PlayerPrefs.HasKey(key));
            SessionState.SetInt(Active + key, PlayerPrefs.GetInt(key));
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!SessionState.GetBool(Active, false))
            {
                string trigger = Path.Combine(Project, "home-menu-check.trigger");
                if (File.Exists(trigger) && !EditorApplication.isPlaying)
                {
                    File.Delete(trigger);
                    Run();
                }
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.8;
            try
            {
                if (afterCapture != null)
                {
                    var action = afterCapture;
                    afterCapture = null;
                    action();
                    return;
                }
                var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                switch (step)
                {
                    case 0:
                        Require(menu != null, "Menu booted");
                        Require(GameObject.Find("Start screen") != null, "Start screen visible");
                        next = EditorApplication.timeSinceStartup + 1.4; // let the title rise in
                        Capture("start", () => Click("Start")); break;
                    case 1:
                        Require(GameObject.Find("Character home") != null && GameObject.Find("Start screen") == null, "Start enters home");
                        Require(Combatant.All.Count == 0, "Preview creates no match units");
                        Require(UnityEngine.Object.FindObjectOfType<HomeUnitPreview>() != null, "Character preview loaded");
                        Require(GameObject.Find("Original outfit") != null, "Equipped outfit shown");
                        Require(GameObject.Find("Invite card") != null, "Party invite shown");
                        Capture("home", () => Click("More Oxcart")); break;
                    case 2:
                        Require(Button("Remove friend Oxcart") != null, "Friend row expands to its actions");
                        Click("Message Oxcart");
                        Require(GameObject.Find("Tab oxcart") != null && menu.State.ChatTab == "oxcart", "Message opens a DM tab");
                        Click("Accept Copperpot");
                        Require(menu.State.Friends.Any(f => f.Id == "copperpot") && menu.State.Requests.Count == 0, "Friend request accepted");
                        Click("Invite Oxcart");
                        Require(menu.State.Party.Contains("oxcart") && Button("Remove Oxcart from party") != null, "Party invite fills a slot");
                        Capture("friends", () => Click("Select clan")); break;
                    case 3:
                        Require(menu.State.ChatTab == "clan", "Chat tab selection");
                        Click("Send message");
                        Require(menu.State.History("clan").Last().Who == "Bramblewick", "Empty draft does not post");
                        Click("Join party");
                        Require(menu.State.InOtherParty && GameObject.Find("Invite card") == null && Button("Leave party") != null, "Joining a party hides the invite and shows LEAVE PARTY");
                        Require(Button("Play").GetComponentInChildren<TMPro.TMP_Text>().text == "READY", "Play button reads READY in someone else's party");
                        Capture("party", () => { Click("Leave party"); Click("Player identity"); }); break;
                    case 4:
                        Require(!menu.State.InOtherParty, "Left the party");
                        Require(GameObject.Find("Your profile modal") != null, "Profile card opens the username editor");
                        Require(!GameObject.Find("Character home").GetComponent<CanvasGroup>().interactable, "Modal blocks home navigation");
                        var draft = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_InputField>().Single(i => i.name == "Username draft");
                        Require(!draft.richText && draft.characterLimit == LocalPlayerProfile.MaxNameLength, "Username input is literal and bounded");
                        draft.text = "<bad>"; Click("Save username");
                        Require(GameObject.Find("Your profile modal") != null && Text("Username feedback").text.Contains("letters"), "Invalid name stays open with feedback");
                        draft.text = "  Rook_27  "; Click("Save username");
                        Require(LocalPlayerProfile.DisplayName == "Rook_27" && Text("Profile username").text == "Rook_27", "Trimmed name saved and shown on the card");
                        Click("Mode quick");
                        Require(!Button("Play").interactable && Button("Play").GetComponentInChildren<TMPro.TMP_Text>().text == "ONLINE LOCKED", "Online modes lock the Play button");
                        Click("Ranked details");
                        Require(GameObject.Find("Ranked play modal") != null, "Online lock explains ranked play");
                        Capture("ranked", () => { Click("Ranked done"); Click("Settings"); }); break;
                    case 5:
                        Require(!GameObject.Find("Character home").GetComponent<CanvasGroup>().interactable, "Settings blocks home navigation");
                        Click("Quality LOW");
                        Require(QualitySettings.GetQualityLevel() == 0, "Low graphics applied");
                        Click("Quality MEDIUM"); break;
                    case 6:
                        Require(QualitySettings.GetQualityLevel() == (QualitySettings.names.Length - 1) / 2, "Medium graphics applied");
                        Click("Quality HIGH"); break;
                    case 7:
                        Require(QualitySettings.GetQualityLevel() == QualitySettings.names.Length - 1, "High graphics applied");
                        UnityEngine.Object.FindObjectsOfType<Slider>().First(s => s.name == "Master slider").value = 0.42f;
                        Require(Mathf.Approximately(AudioListener.volume, 0.42f), "Volume slider applied");
                        Capture("settings", () => { Click("Settings done"); Click("Map duel"); Click("Mode practice"); }); break;
                    case 8:
                        Require(GameObject.Find("Character home").GetComponent<CanvasGroup>().interactable, "Done restores home navigation");
                        Require(menu.State.Map.Players == 2, "Map size selects the player count");
                        Require(Button("Play").interactable && Button("Play").GetComponentInChildren<TMPro.TMP_Text>().text == "PRACTICE", "Practice unlocks Play");
                        Click("Play"); break;
                    case 9:
                        Require(GameState.TeamCount == 2 && GameState.Bases.Take(GameState.TeamCount).All(b => b != null), "Play launches configured match");
                        Require(UnityEngine.Object.FindObjectOfType<MainMenu>() == null, "Menu cleaned up in match");
                        Capture("match", Bootstrap.ShowMenu); break;
                    case 10:
                        Require(menu != null && GameObject.Find("Character home") != null, "Return from match opens home");
                        Require(GameObject.Find("Start screen") == null, "Returning skips Start");
                        Require(menu.State.DirectTabs.Contains("oxcart") && menu.State.Friends.Any(f => f.Id == "copperpot"), "Hub state survives the match");
                        Require(Text("Profile username").text == "Rook_27", "Username survives menu recreation");
                        Require(UnityEngine.Object.FindObjectsOfType<HomeUnitPreview>().Length == 1, "One preview after return");
                        Require(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length == 1, "One EventSystem after return");
                        var events = EventSystem.current;
                        Require(events.currentInputModule != null, "Active input module after return");
                        var add = Button("Add friend");
                        Require(add.IsInteractable(), "Home buttons remain interactive after return");
                        var rect = (RectTransform)add.transform;
                        var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                        var hits = new List<RaycastResult>();
                        events.RaycastAll(pointer, hits);
                        Require(hits.Count > 0 && hits[0].gameObject == add.gameObject, "Mouse hit test reaches the add-friend button");
                        ExecuteEvents.Execute(add.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                        Require(GameObject.Find("Add friend form") != null, "Pointer event opens the add-friend form after return");
                        Debug.Log("HomeMenuCheck: GameView focused=" + Application.isFocused + ", module=" + events.currentInputModule.GetType().Name);
                        Capture("home", () => Finish(errors.Length == 0 ? "PASS: Start, home hub, friends, chat, party, settings, map selection, launch and return.\nNo runtime errors." : "FAIL: runtime errors\n" + errors));
                        step++;
                        return;
                }
                step++;
            }
            catch (Exception exception) { Finish("FAIL at step " + step + ": " + exception); }
        }

        static TMPro.TMP_Text Text(string name) => UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>().FirstOrDefault(t => t.name == name)
            ?? throw new Exception("Missing text: " + name);
        static Button Button(string name) => UnityEngine.Object.FindObjectsOfType<Button>().FirstOrDefault(b => b.name == name)
            ?? throw new Exception("Missing active button: " + name);
        static void Click(string name) => Button(name).onClick.Invoke();
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Debug.Log("HomeMenuCheck: " + message); }
        static void Capture(string name, Action then)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "screenshot-home-menu-" + name + ".png"), 2);
            afterCapture = then;
        }

        static void Finish(string result)
        {
            SessionState.SetBool(Active, false);
            foreach (string key in new[] { "Commoners.Graphics", "SwarmPrototype.Players" })
                if (SessionState.GetBool(Active + key + ".exists", false)) PlayerPrefs.SetInt(key, SessionState.GetInt(Active + key, 0));
                else PlayerPrefs.DeleteKey(key);
            if (SessionState.GetBool(Active + ".hadVolume", false)) PlayerPrefs.SetFloat("Commoners.Volume", SessionState.GetFloat(Active + ".savedVolume", 1));
            else PlayerPrefs.DeleteKey("Commoners.Volume");
            string savedName = SessionState.GetString(Active + ".savedName", "Commander");
            LocalPlayerProfile.Rename(LocalPlayerProfile.Validate(savedName) == null ? savedName : "Commander");
            if (!SessionState.GetBool(Active + ".hadName", false)) PlayerPrefs.DeleteKey(LocalPlayerProfile.NameKey);
            QualitySettings.SetQualityLevel(SessionState.GetInt(Active + ".quality", 2), true);
            AudioListener.volume = SessionState.GetFloat(Active + ".volume", 1);
            PlayerPrefs.Save();
            File.WriteAllText(Path.Combine(Output, "home-menu-check.log"), result);
            Debug.Log("HomeMenuCheck: " + result);
        }
    }
}
