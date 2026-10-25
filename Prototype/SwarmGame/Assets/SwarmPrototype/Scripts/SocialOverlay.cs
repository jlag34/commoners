using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

namespace SwarmPrototype
{
    // One social surface survives the home/match transition. All data is explicitly simulated.
    public class SocialOverlay : MonoBehaviour
    {
        static SocialOverlay instance;
        public SocialDemoState Data { get; private set; }
        public event Action Changed;
        public static bool IsOpen => instance != null && instance.panel != null;
        public static bool IsTyping => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() is TMP_InputField input && input.isFocused;
        public string SelectedThread { get; private set; } = SocialDemoState.DirectThread("Mira");
        RectTransform safe, panel, toast;
        GameObject canvas;
        Button matchButton;
        string view, status = "", friendDraft = "", clanName = "", clanTag = "";
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        GameObject previousSelection;
        bool visible, rendering;
        float nextIncoming = -1, toastUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => instance = null;

        public static SocialOverlay Ensure()
        {
            if (instance == null) instance = new GameObject("Social demo").AddComponent<SocialOverlay>();
            return instance;
        }

        void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            Data = new SocialDemoState(LocalPlayerProfile.DisplayName);
            Data.Changed += OnChanged;
            LocalPlayerProfile.Changed += UpdatePlayerName;
            if (EventSystem.current == null)
            {
                var events = new GameObject("Menu EventSystem", typeof(EventSystem));
                events.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            canvas = new GameObject("Social Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 200;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safe = MenuUi.Fill(canvas.transform, "Social safe area");
            safe.gameObject.AddComponent<MenuSafeArea>();
            matchButton = MenuUi.Button(safe, "In-match social", "SOCIAL  /  F6", Vector2.right, new Vector2(-32, 28), new Vector2(230, 48), () => Toggle("Friends"));
            canvas.SetActive(false);
        }

        public void SetContext(bool inHome, bool show = true)
        {
            Close();
            visible = show;
            canvas.SetActive(show);
            matchButton.gameObject.SetActive(show && !inHome);
            nextIncoming = show ? Time.unscaledTime + 8 : -1;
            RefreshBadge();
        }

        public void Toggle(string tab)
        {
            if (panel != null && view == tab) Close();
            else Open(tab);
        }

        public void Open(string tab)
        {
            if (!visible) return;
            if (panel == null && EventSystem.current != null) previousSelection = EventSystem.current.currentSelectedGameObject;
            view = tab; status = "";
            if (tab == "Chat" && !Data.CanChat(SelectedThread)) SelectedThread = SocialDemoState.DirectThread(Data.Friends[0]);
            rendering = true;
            if (tab == "Chat") Data.MarkRead(SelectedThread);
            rendering = false;
            Rebuild();
        }

        public void OpenChat(string thread)
        {
            if (!Data.CanChat(thread)) return;
            DismissNotice();
            SelectedThread = thread;
            Open("Chat");
        }

        public void Close()
        {
            Remove(panel); panel = null;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
        }

        void OnChanged()
        {
            RefreshBadge();
            Changed?.Invoke();
            if (panel != null && !rendering) Rebuild();
        }

        void RefreshBadge()
        {
            matchButton.GetComponentInChildren<TMP_Text>().text = "SOCIAL" + (Data.Attention > 0 ? " (" + Data.Attention + ")" : "") + "  /  F6";
        }

        void Rebuild()
        {
            string restore = IsTyping ? EventSystem.current.currentSelectedGameObject.name : null;
            rendering = true;
            Remove(panel);
            panel = MenuUi.Rect(safe, "Social " + view, new Vector2(1, 0.5f), new Vector2(-42, 0), new Vector2(460, 700));
            MenuUi.Image(panel, MenuUi.Panel, true).raycastTarget = true;
            MenuUi.Label(panel, "SOCIAL DEMO  /  LOCAL ONLY", new Vector2(30, -24), new Vector2(360, 24), MenuUi.Gold);
            Text(panel, "Your people.", 30, MenuUi.Paper, 30, 62, 350, 44, true);
            var close = MenuUi.Button(panel, "Close social", "X", Vector2.one, new Vector2(-16, -18), new Vector2(36, 36), Close);
            string[] tabs = { "Friends", "Clan", "Chat" };
            for (int i = 0; i < tabs.Length; i++)
            {
                string tab = tabs[i];
                string label = tab.ToUpperInvariant();
                if (tab == "Friends" && Data.Requests.Count > 0) label += " (" + Data.Requests.Count + ")";
                if (tab == "Chat" && Data.Unread > 0) label += " (" + Data.Unread + ")";
                MenuUi.Button(panel, tab + " channel", label, Vector2.up, new Vector2(30 + i * 136, -132), new Vector2(128, 44), () => Open(tab), view == tab);
            }
            MenuUi.Line(panel, new Vector2(30, -193), 400);
            var body = MenuUi.Rect(panel, "Social content", Vector2.up, new Vector2(30, -215), new Vector2(400, 425));
            switch (view)
            {
                case "Friends": Friends(body); break;
                case "Clan": Clan(body); break;
                case "Create": CreateClan(body); break;
                case "Leave": LeaveClan(body); break;
                case "Conversations": Conversations(body); break;
                default: Chat(body); break;
            }
            Text(panel, "Simulated players. Resets when you exit Play.", 12, MenuUi.Muted, 30, 662, 400, 22);
            if (EventSystem.current != null)
            {
                var input = panel.GetComponentsInChildren<TMP_InputField>().FirstOrDefault(f => f.name == restore);
                EventSystem.current.SetSelectedGameObject(input != null ? input.gameObject : close.gameObject);
                if (input != null) { input.ActivateInputField(); input.MoveTextEnd(false); }
            }
            rendering = false;
        }

        void Friends(RectTransform body)
        {
            var content = Scroll(body, "Friends list", 425, out _);
            Input(content, "Friend name", "Player name", friendDraft, 0, 0, 280, 44, 24, value => friendDraft = value);
            MenuUi.Button(content, "Add friend", "ADD", Vector2.up, new Vector2(290, 0), new Vector2(110, 44), RequestFriend);
            Text(content, status.Length > 0 ? status : "Try adding Bram, or accept a request below.", 14, MenuUi.Muted, 0, 54, 400, 42);
            float y = 110;
            MenuUi.Label(content, "REQUESTS  /  " + Data.Requests.Count, new Vector2(0, -y), new Vector2(400, 22)); y += 36;
            if (Data.Requests.Count == 0) { Text(content, "All caught up.", 15, MenuUi.Muted, 0, y, 400, 30); y += 42; }
            foreach (string person in Data.Requests.ToArray())
            {
                var row = Card(content, person + " request", y, 72);
                Person(row, person, "Wants to be your friend", 195);
                MenuUi.Button(row, "Accept " + person, "ACCEPT", Vector2.up, new Vector2(205, -16), new Vector2(100, 40), () => { Data.Accept(person); Notice("Friend added", person + " is now on your friends list.", () => OpenChat(SocialDemoState.DirectThread(person))); });
                MenuUi.Button(row, "Decline " + person, "NO", Vector2.up, new Vector2(315, -16), new Vector2(72, 40), () => Data.Decline(person));
                y += 84;
            }
            foreach (string person in Data.Outgoing.ToArray())
            {
                var row = Card(content, person + " pending", y, 64);
                Person(row, person, "Request sent · waiting for demo reply", 365);
                y += 76;
            }
            y += 8;
            MenuUi.Label(content, "FRIENDS  /  " + Data.Friends.Count, new Vector2(0, -y), new Vector2(400, 22)); y += 36;
            foreach (string person in Data.Friends)
            {
                var row = Card(content, person + " friend", y, 72);
                int count = Data.UnreadFor(SocialDemoState.DirectThread(person));
                Person(row, person, count > 0 ? count + " unread · Demo player" : "Available · Demo player", 260);
                MenuUi.Button(row, "Message " + person, "MESSAGE", Vector2.up, new Vector2(275, -16), new Vector2(112, 40), () => OpenChat(SocialDemoState.DirectThread(person)));
                y += 84;
            }
            SizeContent(content, y);
        }

        void RequestFriend()
        {
            string name = friendDraft.Trim();
            status = Data.RequestFriend(name);
            if (status != null) { Rebuild(); return; }
            friendDraft = ""; status = "Request sent. This demo player will accept shortly.";
            Rebuild(); StartCoroutine(AcceptLater(name));
        }
        IEnumerator AcceptLater(string name)
        {
            yield return new WaitForSecondsRealtime(1.8f);
            if (!Data.Outgoing.Contains(name)) yield break;
            status = name + " is now on your friends list.";
            if (Data.AcceptOutgoing(name)) Notice("Friend request accepted", name + " accepted your request.", () => OpenChat(SocialDemoState.DirectThread(name)));
        }

        void Clan(RectTransform body)
        {
            if (Data.CurrentClan != null)
            {
                var clan = Data.CurrentClan;
                MenuUi.Label(body, "[" + clan.Tag + "]  /  " + (clan.Created ? "LEADER" : "MEMBER"), Vector2.zero, new Vector2(400, 24), MenuUi.Gold);
                Text(body, clan.Name, 32, MenuUi.Paper, 0, 40, 400, 52, true);
                Text(body, clan.Description, 17, MenuUi.Muted, 0, 105, 400, 64);
                MenuUi.Label(body, "MEMBERS  /  " + (clan.Members + 1), new Vector2(0, -192), new Vector2(400, 24));
                Text(body, Data.PlayerName + (clan.Created ? " (you) · Leader" : " (you) · Member\nMira · Leader\nRowan · Officer\n+ " + (clan.Members - 2) + " demo members"), 17, MenuUi.Paper, 0, 230, 400, 100);
                MenuUi.Button(body, "Open clan chat", "CLAN CHAT", Vector2.up, new Vector2(0, -351), new Vector2(242, 52), () => OpenChat(clan.Thread));
                MenuUi.Button(body, "Leave clan", "LEAVE", Vector2.up, new Vector2(254, -351), new Vector2(146, 52), () => { view = "Leave"; Rebuild(); });
                return;
            }
            var content = Scroll(body, "Clan browser", 425, out _);
            Text(content, "Find your people.", 25, MenuUi.Paper, 0, 0, 250, 40, true);
            MenuUi.Button(content, "Create clan", "CREATE", Vector2.up, new Vector2(265, 0), new Vector2(135, 40), () => { status = ""; view = "Create"; Rebuild(); });
            Text(content, status.Length > 0 ? status : "Join a demo clan, or start your own.", 15, MenuUi.Muted, 0, 48, 400, 42);
            float y = 110;
            foreach (var clan in Data.Clans.ToArray())
            {
                var row = Card(content, clan.Name + " clan", y, 104);
                Text(row, "[" + clan.Tag + "] " + clan.Name, 20, MenuUi.Paper, 16, 10, 270, 32, true);
                Text(row, clan.Members + " members · Open", 14, MenuUi.Teal, 16, 48, 260, 26);
                MenuUi.Button(row, "Join " + clan.Id, "JOIN", Vector2.up, new Vector2(294, -30), new Vector2(92, 44), () =>
                {
                    status = Data.Join(clan.Id) ?? "";
                    Rebuild();
                    if (status.Length == 0) Notice("Clan joined", "Welcome to [" + clan.Tag + "] " + clan.Name + ".", () => Open("Clan"));
                });
                y += 116;
            }
            SizeContent(content, y);
        }

        void CreateClan(RectTransform body)
        {
            Text(body, "Make it yours.", 28, MenuUi.Paper, 0, 0, 400, 44, true);
            MenuUi.Label(body, "CLAN NAME", new Vector2(0, -64), new Vector2(400, 24));
            Input(body, "Clan name", "e.g. The Small Council", clanName, 0, 100, 400, 48, 24, value => clanName = value);
            MenuUi.Label(body, "CLAN TAG", new Vector2(0, -172), new Vector2(400, 24));
            Input(body, "Clan tag", "2–4 letters", clanTag, 0, 208, 180, 48, 4, value => clanTag = value);
            Text(body, status.Length > 0 ? status : "You'll be the leader. Creating a demo clan is free and only changes this local preview.", 16, MenuUi.Muted, 0, 282, 400, 70);
            MenuUi.Button(body, "Confirm create clan", "CREATE", Vector2.up, new Vector2(0, -370), new Vector2(240, 52), () =>
            {
                rendering = true; status = Data.Create(clanName, clanTag); rendering = false;
                if (status == null) { view = "Clan"; status = ""; Notice("Clan created", "Your people have a new home.", () => Open("Clan")); }
                Rebuild();
            }, true);
            MenuUi.Button(body, "Cancel create clan", "BACK", Vector2.up, new Vector2(252, -370), new Vector2(148, 52), () => Open("Clan"));
        }

        void LeaveClan(RectTransform body)
        {
            var clan = Data.CurrentClan;
            Text(body, "Leave " + clan.Name + "?", 28, MenuUi.Paper, 0, 0, 400, 90, true);
            Text(body, clan.Created ? "You're this demo clan's only member. Leaving closes the clan. You can create another anytime."
                : "You'll lose access to this clan's chat. You can rejoin from the clan browser.", 18, MenuUi.Muted, 0, 120, 400, 130);
            MenuUi.Button(body, "Confirm leave clan", "LEAVE CLAN", Vector2.up, new Vector2(0, -304), new Vector2(240, 54), () =>
            {
                string name = clan.Name;
                rendering = true; Data.Leave(); rendering = false;
                view = "Clan"; status = ""; Rebuild();
                Notice("Clan left", "You left " + name + ".", () => Open("Clan"));
            }, true);
            MenuUi.Button(body, "Cancel leave clan", "STAY", Vector2.up, new Vector2(252, -304), new Vector2(148, 54), () => Open("Clan"));
        }

        void Conversations(RectTransform body)
        {
            var content = Scroll(body, "Conversations", 425, out _);
            float y = 0;
            foreach (string person in Data.Friends)
            {
                var row = Card(content, "Conversation " + person, y, 76);
                Person(row, person, Data.UnreadFor(SocialDemoState.DirectThread(person)) + " unread · Demo player", 260);
                MenuUi.Button(row, "Chat with " + person, "OPEN", Vector2.up, new Vector2(280, -17), new Vector2(105, 42), () => OpenChat(SocialDemoState.DirectThread(person)));
                y += 88;
            }
            if (Data.CurrentClan != null)
                MenuUi.Button(content, "Clan conversation", "[" + Data.CurrentClan.Tag + "] CLAN CHAT", Vector2.up, new Vector2(0, -y), new Vector2(400, 52), () => OpenChat(Data.CurrentClan.Thread));
            SizeContent(content, y + 66);
        }

        void Chat(RectTransform body)
        {
            Text(body, Data.ThreadName(SelectedThread), 22, MenuUi.Paper, 0, 0, 265, 36, true);
            MenuUi.Button(body, "Change conversation", "INBOX", Vector2.up, new Vector2(288, 0), new Vector2(112, 36), () => { view = "Conversations"; Rebuild(); });
            var logRoot = MenuUi.Rect(body, "Conversation history", Vector2.up, new Vector2(0, -53), new Vector2(400, 245));
            var content = Scroll(logRoot, "Messages", 245, out var scroll);
            float y = 0;
            foreach (var message in Data.History(SelectedThread))
            {
                var row = Card(content, "Chat bubble", y, 80);
                row.GetComponent<Image>().color = message.Mine ? MenuUi.Hex("28554F") : MenuUi.Ink;
                Text(row, message.Author + (message.Mine ? "" : "  ·  DEMO"), 12, message.Mine ? MenuUi.Gold : MenuUi.Teal, 14, 9, 370, 20, true);
                var text = Text(row, message.Body, 16, MenuUi.Paper, 14, 36, 370, 100);
                float height = Mathf.Max(70, text.GetPreferredValues(message.Body, 370, 0).y + 52);
                text.rectTransform.sizeDelta = new Vector2(370, height - 45);
                row.sizeDelta = new Vector2(400, height);
                y += height + 10;
            }
            SizeContent(content, y);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0;
            string draft = drafts.TryGetValue(SelectedThread, out string saved) ? saved : "";
            var input = Input(body, "Message draft", "Write a message...", draft, 0, 314, 298, 50, 280, value => drafts[SelectedThread] = value);
            input.onSubmit.AddListener(_ => Send());
            MenuUi.Button(body, "Send message", "SEND", Vector2.up, new Vector2(310, -314), new Vector2(90, 50), Send);
            if (status.Length > 0) Text(body, status, 13, MenuUi.Gold, 0, 370, 400, 25);
            MenuUi.Button(body, "Simulate incoming", "+ DEMO MESSAGE", Vector2.up, new Vector2(0, -390), new Vector2(230, 32), SimulateIncoming);
        }

        void Send()
        {
            string thread = SelectedThread;
            drafts.TryGetValue(thread, out string draft);
            rendering = true; status = Data.Send(thread, draft); rendering = false;
            if (status == null) { drafts[thread] = ""; status = ""; StartCoroutine(ReplyLater(thread)); }
            Rebuild();
        }
        IEnumerator ReplyLater(string thread)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Receive(thread, thread.StartsWith("clan:") ? "Mira" : Data.ThreadName(thread), "Sounds good! I'll meet you after this match.");
        }
        public void SimulateIncoming() => Receive(SelectedThread, SelectedThread.StartsWith("clan:") ? "Rowan" : Data.ThreadName(SelectedThread), "Nice hold at the north entrance. Up for another round?");
        void Receive(string thread, string author, string body)
        {
            bool reading = panel != null && view == "Chat" && SelectedThread == thread;
            if (Data.Receive(thread, author, body, reading) && !reading)
                Notice(author + " messaged you · DEMO", body, () => OpenChat(thread));
        }

        void Notice(string title, string message, Action clicked)
        {
            Remove(toast);
            toast = (RectTransform)MenuUi.Button(safe, "Social notification", "", new Vector2(0.5f, 1), new Vector2(0, -106), new Vector2(430, 100), () => { DismissNotice(); clicked(); }).transform;
            Text(toast, title, 17, MenuUi.Gold, 18, 12, 360, 26, true);
            Text(toast, message, 15, MenuUi.Paper, 18, 45, 365, 46);
            MenuUi.Button(toast, "Dismiss notification", "X", Vector2.one, new Vector2(-8, -8), new Vector2(30, 30), DismissNotice);
            toastUntil = Time.unscaledTime + 5;
        }
        void DismissNotice() { Remove(toast); toast = null; }

        void Update()
        {
            if (!visible) return;
            if (InputShim.SocialPressed) Toggle("Friends");
            if (InputShim.CancelPressed && panel != null) Close();
            if (toast != null && Time.unscaledTime >= toastUntil) DismissNotice();
            if (nextIncoming > 0 && Time.unscaledTime >= nextIncoming)
            {
                nextIncoming = -1;
                Receive(SocialDemoState.DirectThread("Mira"), "Mira", "Hey " + Data.PlayerName + "! Ready whenever you are.");
            }
        }

        static TMP_Text Text(Transform parent, string text, float size, Color color, float x, float y, float width, float height, bool bold = false)
        {
            var label = MenuUi.Text(parent, text, size, color, new Vector2(x, -y), new Vector2(width, height), bold: bold);
            label.richText = false; return label;
        }
        static RectTransform Card(Transform parent, string name, float y, float height)
        {
            var card = MenuUi.Rect(parent, name, Vector2.up, new Vector2(0, -y), new Vector2(400, height));
            MenuUi.Image(card, MenuUi.Ink, true); return card;
        }
        static void Person(Transform row, string name, string detail, float width)
        {
            Text(row, name, 19, MenuUi.Paper, 14, 9, width - 14, 28, true);
            Text(row, detail, 12, MenuUi.Muted, 14, 40, width - 14, 24);
        }
        static TMP_InputField Input(Transform parent, string name, string hint, string value, float x, float y, float width, float height, int limit, Action<string> changed)
            => MenuUi.InputField(parent, name, hint, value, new Vector2(x, -y), new Vector2(width, height), limit, v => changed(v));
        static RectTransform Scroll(Transform parent, string name, float height, out ScrollRect scroll)
        {
            var viewport = MenuUi.Rect(parent, name, Vector2.up, Vector2.zero, new Vector2(400, height));
            MenuUi.Image(viewport, new Color(0, 0, 0, 0)).raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            var content = MenuUi.Rect(viewport, "Scroll content", Vector2.up, Vector2.zero, new Vector2(400, height));
            scroll.viewport = viewport; scroll.content = content; return content;
        }
        static void SizeContent(RectTransform content, float height) => content.sizeDelta = new Vector2(400, height);
        static void Remove(RectTransform rect) { if (rect != null) { rect.gameObject.SetActive(false); Destroy(rect.gameObject); } }
        void UpdatePlayerName() => Data.RenamePlayer(LocalPlayerProfile.DisplayName);
        void OnDestroy() { LocalPlayerProfile.Changed -= UpdatePlayerName; if (Data != null) Data.Changed -= OnChanged; if (instance == this) instance = null; }
    }
}
