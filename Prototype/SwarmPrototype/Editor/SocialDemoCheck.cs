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
    public static class SocialDemoCheck
    {
        const string Key = "Commoners.SocialDemoCheck";
        static int step;
        static double next;
        static string errors = "";
        static Action afterCapture;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        static SocialDemoCheck()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors += message + "\n";
            };
        }

        [MenuItem("Commoners/Check social demo")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play before checking the social demo."); return; }
            step = 0; errors = ""; next = 0; afterCapture = null;
            SessionState.SetBool(Key + "hadCount", PlayerPrefs.HasKey("SwarmPrototype.Players"));
            SessionState.SetInt(Key + "count", PlayerPrefs.GetInt("SwarmPrototype.Players", 4));
            SessionState.SetBool(Key, true);
            try { ValidateModel(); }
            catch (Exception exception) { Finish("FAIL in state checks: " + exception); return; }
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.8;
            try
            {
                if (afterCapture != null) { var action = afterCapture; afterCapture = null; action(); return; }
                var social = SocialOverlay.Ensure();
                var state = social.Data;
                switch (step)
                {
                    case 0:
                        Click("Start"); Click("Friends and chat"); break;
                    case 1:
                        Require(state.Friends.Count == 2 && state.Requests.Count == 2, "Seeded friends, conversations and requests");
                        Click("Accept Vale"); Click("Decline Sable");
                        Require(state.Friends.Contains("Vale") && state.Requests.Count == 0, "Accept and decline update both lists");
                        Field("Friend name").text = "Bram"; Click("Add friend");
                        Require(state.Outgoing.Contains("Bram"), "Outgoing request shown as pending");
                        next += 1.5; break;
                    case 2:
                        Require(state.Friends.Contains("Bram") && !state.Outgoing.Contains("Bram"), "Demo recipient accepts the outgoing request");
                        Capture("friends", () => Click("Message Mira")); break;
                    case 3:
                        Require(state.History(social.SelectedThread).Length >= 3, "Seeded conversation visible");
                        Require(state.UnreadFor(social.SelectedThread) == 0, "Opening a thread marks it read");
                        Field("Message draft").text = "<b>Meet at the north gate?</b>";
                        Click("Send message");
                        Require(state.History(social.SelectedThread).Last().Body == "<b>Meet at the north gate?</b>", "Typed message stored literally");
                        var bodies = UnityEngine.Object.FindObjectsOfType<TMP_Text>().Where(t => t.text == "<b>Meet at the north gate?</b>").ToArray();
                        Require(bodies.Length > 0 && bodies.All(t => !t.richText), "Chat text cannot inject rich text");
                        next += 1.5; break;
                    case 4:
                        Require(state.History(social.SelectedThread).Last().Author == "Mira", "Simulated reply received");
                        Click("Simulate incoming");
                        Require(state.UnreadFor(social.SelectedThread) == 0, "Visible messages stay read");
                        Capture("chat", () => { Click("Close social"); social.SimulateIncoming(); }); break;
                    case 5:
                        Require(state.UnreadFor(social.SelectedThread) > 0 && GameObject.Find("Social notification") != null, "Incoming message raises a notification and unread badge");
                        Click("Social notification");
                        Require(state.UnreadFor(social.SelectedThread) == 0, "Notification opens and reads the right conversation");
                        Click("Clan channel"); Click("Join oak");
                        Require(state.CurrentClan != null && state.CurrentClan.Tag == "OAK", "Join clan updates membership");
                        Click("Open clan chat"); Field("Message draft").text = "Hello Oakguard!"; Click("Send message"); break;
                    case 6:
                        Require(state.History(state.CurrentClan.Thread).Any(m => m.Mine && m.Body == "Hello Oakguard!"), "Clan chat uses its own history");
                        Click("Clan channel"); Click("Leave clan"); Click("Cancel leave clan");
                        Require(state.CurrentClan != null, "Cancel leave preserves membership");
                        Click("Leave clan"); Click("Confirm leave clan");
                        Require(state.CurrentClan == null, "Leave clan returns to browsing");
                        Click("Create clan"); Field("Clan name").text = "Northbound"; Field("Clan tag").text = "NB"; Click("Confirm create clan"); break;
                    case 7:
                        Require(state.CurrentClan != null && state.CurrentClan.Created && state.CurrentClan.Tag == "NB", "Create clan makes player its leader");
                        Capture("clan", () => { Click("Leave clan"); Click("Confirm leave clan"); }); break;
                    case 8:
                        Require(state.CurrentClan == null && state.Clans.All(c => c.Tag != "NB"), "Leaving a sole-member demo clan closes it");
                        Click("Close social"); Click("Match options"); Click("Players 2"); Click("Done"); Click("Play"); break;
                    case 9:
                        Require(UnityEngine.Object.FindObjectOfType<MainMenu>() == null && GameState.TeamCount == 2, "Match launched");
                        Require(state.Friends.Contains("Vale") && state.Friends.Contains("Bram"), "Social state survives match launch");
                        Click("In-match social"); social.OpenChat(SocialDemoState.DirectThread("Mira"));
                        Field("Message draft").ActivateInputField(); next += 0.5; break;
                    case 10:
                        Require(SocialOverlay.IsTyping, "In-match composer takes keyboard focus");
                        Field("Message draft").text = "Defending my base. One moment!"; Click("Send message");
                        social.Close(); social.SimulateIncoming();
                        Capture("in-match", () => social.OpenChat(SocialDemoState.DirectThread("Mira"))); break;
                    case 11:
                        Require(state.History(SocialDemoState.DirectThread("Mira")).Any(m => m.Body == "Defending my base. One moment!"), "In-match chat sends into the same conversation");
                        social.Close(); Bootstrap.ShowMenu(); break;
                    case 12:
                        Require(UnityEngine.Object.FindObjectsOfType<SocialOverlay>().Length == 1 && UnityEngine.Object.FindObjectsOfType<EventSystem>().Length == 1, "No duplicate social overlay or input system after return");
                        Require(state.Friends.Contains("Bram"), "Friends survive returning home");
                        social.OpenChat(SocialDemoState.DirectThread("Mira"));
                        Capture("chat", () => Finish(errors.Length == 0 ? "PASS: message history/replies/unread notifications, incoming/outgoing friend requests, clan join/create/leave, clan chat and in-match continuity.\nNo runtime errors." : "FAIL: runtime errors\n" + errors));
                        step++; return;
                }
                step++;
            }
            catch (Exception exception) { Finish("FAIL at step " + step + ": " + exception + "\n" + errors); }
        }

        static void ValidateModel()
        {
            var s = new SocialDemoState();
            Require(s.RequestFriend("MIRA") != null && s.RequestFriend("Commander") != null && s.RequestFriend("<bad>") != null, "Invalid/self/duplicate requests rejected");
            Require(s.Accept("Vale") && !s.Accept("Vale"), "Friend acceptance is idempotent");
            Require(s.RequestFriend("Bram") == null && s.RequestFriend("bram") != null && s.AcceptOutgoing("Bram"), "Outgoing request lifecycle validated");
            Require(s.Send("unknown", "hi") != null && s.Send(SocialDemoState.DirectThread("Mira"), " ") != null, "Invalid chat sends rejected");
            Require(s.Create("x", "1") != null && s.Join("oak") == null && s.Join("night") != null, "Clan validation and single membership enforced");
            string thread = s.CurrentClan.Thread;
            s.Leave(); Require(!s.CanChat(thread) && s.Send(thread, "hi") != null, "Leaving revokes clan chat access");
            Require(s.Create("Northbound", "nb") == null && s.CurrentClan.Created && s.CurrentClan.Tag == "NB", "Clan creation normalizes tag");
            s.Leave(); Require(s.Clans.All(c => c.Tag != "NB"), "Sole-member clan removed on leave");
        }
        static TMP_InputField Field(string name) => UnityEngine.Object.FindObjectsOfType<TMP_InputField>().First(f => f.name == name);
        static void Click(string name) => UnityEngine.Object.FindObjectsOfType<Button>().First(b => b.name == name).onClick.Invoke();
        static void Require(bool condition, string text) { if (!condition) throw new Exception(text); Debug.Log("SocialDemoCheck: " + text); }
        static void Capture(string name, Action then)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "screenshot-social-" + name + ".png"), 2);
            afterCapture = then;
        }
        static void Finish(string text)
        {
            SessionState.SetBool(Key, false);
            if (SessionState.GetBool(Key + "hadCount", false)) PlayerPrefs.SetInt("SwarmPrototype.Players", SessionState.GetInt(Key + "count", 4));
            else PlayerPrefs.DeleteKey("SwarmPrototype.Players");
            PlayerPrefs.Save();
            File.WriteAllText(Path.Combine(Output, "social-demo-check.log"), text);
            Debug.Log("SocialDemoCheck: " + text);
        }
    }
}
