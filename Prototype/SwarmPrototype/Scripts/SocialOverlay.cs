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
using static SwarmPrototype.HubWidgets;

namespace SwarmPrototype
{
    // The in-match social panel: a card above the HUD's bottom-right buttons with
    // Friends (requests, friends with last message and unread), Clan (browser, or
    // your clan with its chat) and direct chats, built from the hub widget kit so
    // it matches the home screens. One instance survives the home/match transition.
    // All data is SocialDemoState: explicitly simulated, nothing leaves the process.
    public class SocialOverlay : MonoBehaviour
    {
        const float PanelWidth = 380, PanelHeight = 580, Edge = 14, AboveButtons = 14 + 44 + 12;

        static SocialOverlay instance;
        public SocialDemoState Data { get; private set; }
        public event Action Changed;
        public static bool IsOpen => instance != null && instance.panel != null;
        public static bool IsTyping => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() is TMP_InputField input && input.isFocused;
        public string SelectedThread { get; private set; } = SocialDemoState.DirectThread("Mira");

        RectTransform safe, panel, toast;
        GameObject canvas;
        ScrollRect chatScroll;
        string view, status = "", friendDraft = "", clanSearch = "", clanName = "", clanTag = "";
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        GameObject previousSelection;
        bool visible, rendering, scrollToEnd;
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
            canvas.SetActive(false);
        }

        // ---- Public surface --------------------------------------------------------------------

        public void SetContext(bool inHome, bool show = true)
        {
            Close();
            visible = show;
            canvas.SetActive(show);
            nextIncoming = show ? Time.unscaledTime + 8 : -1;
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
            if (tab == "Clan" && Data.CurrentClan != null) Data.MarkRead(Data.CurrentClan.Thread);
            rendering = false;
            Rebuild();
        }

        // Direct threads open the chat view; the clan thread lives on the Clan tab.
        public void OpenChat(string thread)
        {
            if (!Data.CanChat(thread)) return;
            DismissNotice();
            SelectedThread = thread;
            Open(thread.StartsWith("clan:") ? "Clan" : "Chat");
        }

        public void Close()
        {
            Remove(panel); panel = null;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
        }

        public void SimulateIncoming() => Receive(SelectedThread, SelectedThread.StartsWith("clan:") ? "Rowan" : Data.ThreadName(SelectedThread), "Nice hold at the north entrance. Up for another round?");

        // ---- Frame -----------------------------------------------------------------------------

        void OnChanged()
        {
            Changed?.Invoke();
            if (panel != null && !rendering) Rebuild();
        }

        void Rebuild()
        {
            string restore = IsTyping ? EventSystem.current.currentSelectedGameObject.name : null;
            rendering = true;
            Remove(panel);
            panel = Panel(safe, "Social " + view, 18, HubTheme.Rgba(14, 30, 36, 0.97f), HubTheme.PanelBorderBright);
            panel.anchorMin = panel.anchorMax = new Vector2(1, 0);
            panel.pivot = new Vector2(1, 0);
            panel.anchoredPosition = new Vector2(-Edge, AboveButtons);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.GetComponent<Image>().raycastTarget = true;
            var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true; column.childForceExpandHeight = false;

            bool chat = view == "Chat";
            if (chat) ChatHeader(panel); else TabHeader(panel);
            Rule(panel, "Header rule", HubTheme.PanelBorderBright);

            var body = Column(panel, "Social content", 0);
            Size(body, flexHeight: 1, minHeight: 0);
            switch (view)
            {
                case "Friends": Friends(body); break;
                case "Clan": if (Data.CurrentClan != null) ClanHome(body); else ClanBrowser(body); break;
                case "Create": CreateClan(body); break;
                case "Leave": LeaveClan(body); break;
                default: Conversation(body, SelectedThread); break;
            }

            if (EventSystem.current != null)
            {
                var input = panel.GetComponentsInChildren<TMP_InputField>().FirstOrDefault(f => f.name == restore);
                EventSystem.current.SetSelectedGameObject(input != null ? input.gameObject : panel.gameObject);
                if (input != null) { input.ActivateInputField(); input.MoveTextEnd(false); }
            }
            rendering = false;
        }

        void TabHeader(Transform parent)
        {
            var header = Row(parent, "Header", 8, Pad(12, 14, 10, 12), TextAnchor.MiddleLeft, false, false);
            var tabs = Row(header, "Tabs", 4, Pad(4, 4), TextAnchor.MiddleLeft, false, false);
            Background(tabs, HubTheme.Track, 12);
            Tab(tabs, "Friends", Data.Requests.Count + Data.Friends.Sum(f => Data.UnreadFor(SocialDemoState.DirectThread(f))));
            Tab(tabs, "Clan", Data.CurrentClan != null ? Data.UnreadFor(Data.CurrentClan.Thread) : 0);
            Spacer(header);
            IconButton(header, "Close social", "close", Close, 40, HubTheme.Secondary, null, 16, 10);
        }

        void Tab(Transform tabs, string tab, int badge)
        {
            bool active = view == tab || (tab == "Clan" && (view == "Create" || view == "Leave"));
            var button = Button(tabs, tab + " channel", tab, active ? ButtonStyle.SegmentActive : ButtonStyle.Segment, () => Open(tab),
                new Spec { Height = 40, PaddingX = 14, Radius = 9, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold, Gap = 8 });
            if (badge > 0) Badge(button.transform, badge);
        }

        void ChatHeader(Transform parent)
        {
            string name = Data.ThreadName(SelectedThread);
            var header = Row(parent, "Header", 10, Pad(12, 12, 10, 12), TextAnchor.MiddleLeft, false, false);
            IconButton(header, "Back to friends", "chevron-left", () => Open("Friends"), 36, HubTheme.Secondary, null, 16, 9);
            Avatar(header, "Avatar", 40, 20, AvatarColor(name), Initial(name), 16, HubTheme.Cream, null, HubTheme.Online, 11, 2);
            var lines = Column(header, "Lines", 1, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1, minWidth: 0);
            HubTheme.Text(lines, "Name", name, 16, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(lines, "Presence", "Online", 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            IconButton(header, "Close social", "close", Close, 40, HubTheme.Secondary, null, 16, 10);
        }

        static void Footer(Transform parent, string text)
        {
            Rule(parent, "Footer rule", HubTheme.PanelBorderBright);
            var foot = Row(parent, "Footer", 0, Pad(14, 14), TextAnchor.MiddleCenter, false, false);
            HubTheme.Text(foot, "Note", text, 13, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.Center, false);
        }

        // ---- Friends ---------------------------------------------------------------------------

        void Friends(Transform body)
        {
            var list = ScrollList(body, "Friends list", out _, 8, Pad(14, 14, 14, 10));

            var field = Input(list, "Friend name", "Add by player name", friendDraft, v => friendDraft = v, _ => RequestFriend(), 48, 12, null, null, "search", 24);
            field.textViewport.offsetMax = new Vector2(-56, 0);
            var add = IconButton(field.transform, "Add friend", "plus", RequestFriend, 40, HubTheme.OnGold, HubTheme.Gold, 18, 10);
            Place((RectTransform)add.transform, new Vector2(1, 0.5f), new Vector2(-4 - 20, 0));
            if (status.Length > 0) HubTheme.Text(list, "Status", status, 13, HubTheme.Online);

            if (Data.Requests.Count > 0 || Data.Outgoing.Count > 0)
            {
                SectionLabel(list, "REQUESTS", Data.Requests.Count + Data.Outgoing.Count, 10);
                foreach (string person in Data.Requests.ToArray())
                {
                    var row = PersonRow(list, person + " request", person, "Wants to be friends", null);
                    IconButton(row, "Accept " + person, "check", () =>
                    {
                        Data.Accept(person);
                        Notice("Friend added", person + " is now on your friends list.", () => OpenChat(SocialDemoState.DirectThread(person)));
                    }, 44, HubTheme.OnGold, HubTheme.Online, 18, 12);
                    var decline = IconButton(row, "Decline " + person, "close", () => Data.Decline(person), 44, HubTheme.Secondary, null, 16, 12);
                    Border((RectTransform)decline.transform, HubTheme.InputBorder, 12);
                }
                foreach (string person in Data.Outgoing.ToArray())
                    PersonRow(list, person + " pending", person, "Request sent · waiting for demo reply", null);
            }

            SectionLabel(list, "FRIENDS", Data.Friends.Count, 14);
            foreach (string person in Data.Friends)
            {
                string thread = SocialDemoState.DirectThread(person);
                int unread = Data.UnreadFor(thread);
                var last = Data.History(thread).LastOrDefault();
                var row = PersonRow(list, person + " friend", person, last != null ? last.Body : "Online", HubTheme.Online);
                if (unread > 0) { Background(row, HubTheme.RowWash, 12).raycastTarget = true; Badge(row, unread); }
                else Background(row, Color.clear, 12).raycastTarget = true;
                Icon(row, "Chevron", "chevron-right", 16, HubTheme.Faint);
                Clickable(row, () => OpenChat(thread));
            }
            Footer(body, "Demo players · resets when you leave Play");
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

        // ---- Clan ------------------------------------------------------------------------------

        void ClanBrowser(Transform body)
        {
            var list = ScrollList(body, "Clan browser", out _, 8, Pad(14, 14, 14, 10));
            var search = Row(list, "Search row", 10, null, TextAnchor.MiddleLeft);
            Input(search, "Clan search", "Find a clan", clanSearch, v => { clanSearch = v; Rebuild(); }, null, 48, 12, null, null, "search", 24);
            Button(search, "Create clan", "Create", ButtonStyle.Gold, () => { status = ""; view = "Create"; Rebuild(); },
                new Spec { Height = 48, PaddingX = 16, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold, Icon = "plus", IconSize = 16, Gap = 6 });
            if (status.Length > 0) HubTheme.Text(list, "Status", status, 13, HubTheme.Online);

            SectionLabel(list, "OPEN CLANS", -1, 10);
            string filter = clanSearch.Trim();
            var shown = Data.Clans.Where(c => filter.Length == 0 || c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || c.Tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (shown.Length == 0) HubTheme.Text(list, "Empty", "No clan matches that. Create one?", 13, HubTheme.Tertiary);
            foreach (var clan in shown)
            {
                var row = Row(list, clan.Name + " clan", 12, Pad(10, 0), TextAnchor.MiddleLeft, false, false);
                TagBadge(row, clan.Tag);
                var lines = Column(row, "Lines", 2, null, TextAnchor.MiddleLeft);
                Size(lines, flexWidth: 1, minWidth: 0);
                var title = Row(lines, "Title", 8, null, TextAnchor.LowerLeft, false, false);
                HubTheme.Text(title, "Name", clan.Name, 16, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.BottomLeft, false);
                HubTheme.Text(title, "Members", clan.Members + " members", 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.BottomLeft, false);
                var about = HubTheme.Text(lines, "About", clan.Description, 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
                about.overflowMode = TextOverflowModes.Ellipsis;
                var join = Button(row, "Join " + clan.Id, "Join", ButtonStyle.Ghost, () =>
                {
                    status = Data.Join(clan.Id) ?? "";
                    Rebuild();
                    if (status.Length == 0) Notice("Clan joined", "Welcome to [" + clan.Tag + "] " + clan.Name + ".", () => Open("Clan"));
                }, new Spec { Height = 44, PaddingX = 18, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold });
                var tint = join.GetComponent<HubHover>();
                tint.NormalText = HubTheme.Gold; tint.HoverText = HubTheme.GoldHover; tint.Label.color = HubTheme.Gold;
                tint.NormalBorder = HubTheme.Alpha(HubTheme.Gold, 0.55f); tint.HoverBorder = HubTheme.Gold; if (tint.Border != null) tint.Border.color = tint.NormalBorder;
            }
            Footer(body, "Demo clans · resets when you leave Play");
        }

        void ClanHome(Transform body)
        {
            var clan = Data.CurrentClan;
            var head = Column(body, "Clan head", 10, Pad(12, 14, 14, 12));
            var title = Row(head, "Title row", 12, null, TextAnchor.MiddleLeft, false, false);
            TagBadge(title, clan.Tag);
            var lines = Column(title, "Lines", 2, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1, minWidth: 0);
            HubTheme.Text(lines, "Name", clan.Name, 22, HubTheme.Cream, HubTheme.Face.WideHeavy, 0, TextAlignmentOptions.MidlineLeft, false);
            var about = HubTheme.Text(lines, "About", clan.Description, 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            about.overflowMode = TextOverflowModes.Ellipsis;
            IconButton(title, "Clan options", "dots", () => { view = "Leave"; Rebuild(); }, 36, HubTheme.Secondary, null, 18, 9);

            var members = Row(head, "Members card", 12, Pad(8, 10, 12, 10), TextAnchor.MiddleLeft, false, false);
            Background(members, HubTheme.Track, 12);
            var stack = Row(members, "Stack", -10, null, TextAnchor.MiddleLeft, false, false);
            Avatar(stack, "Member Mira", 36, 18, AvatarColor("Mira"), "M", 14, HubTheme.Cream, HubTheme.DotRing);
            Avatar(stack, "Member Rowan", 36, 18, AvatarColor("Rowan"), "R", 14, HubTheme.Cream, HubTheme.DotRing);
            int others = Mathf.Max(0, clan.Members - 2);
            Avatar(stack, "More members", 36, 18, HubTheme.Segment, "+" + others, 12, HubTheme.Cream, HubTheme.DotRing);
            var count = Row(members, "Count", 6, null, TextAnchor.MiddleLeft, false, false);
            HubTheme.Text(count, "Label", "Members", 15, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(count, "Number", (clan.Members + 1).ToString(), 15, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            Spacer(members);
            HubTheme.Text(members, "Role", "You · " + (clan.Created ? "Leader" : "Member"), 14, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineRight, false);
            Icon(members, "Chevron", "chevron-right", 16, HubTheme.Faint);
            Rule(body, "Head rule", HubTheme.PanelBorderBright);

            Conversation(body, clan.Thread);
        }

        void CreateClan(Transform body)
        {
            var form = Column(body, "Create form", 12, Pad(16, 14, 14, 14));
            HubTheme.Text(form, "Title", "Make it yours.", 22, HubTheme.Cream, HubTheme.Face.WideHeavy, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Label(form, "CLAN NAME");
            Input(form, "Clan name", "e.g. The Small Council", clanName, v => clanName = v, null, 48, 12, null, null, null, 24);
            HubTheme.Label(form, "CLAN TAG");
            Input(form, "Clan tag", "2–4 letters", clanTag, v => clanTag = v, null, 48, 12, null, null, null, 4);
            HubTheme.Text(form, "Note", status.Length > 0 ? status : "You'll be the leader. A demo clan only changes this local preview.", 13, status.Length > 0 ? HubTheme.DangerText : HubTheme.Tertiary);
            var actions = Row(form, "Actions", 8, null, TextAnchor.MiddleLeft);
            Button(actions, "Confirm create clan", "Create clan", ButtonStyle.Gold, () =>
            {
                rendering = true; status = Data.Create(clanName, clanTag) ?? ""; rendering = false;
                if (status.Length == 0) { view = "Clan"; Notice("Clan created", "Your people have a new home.", () => Open("Clan")); }
                Rebuild();
            }, new Spec { Height = 48, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold, FlexWidth = 1 });
            Button(actions, "Cancel create clan", "Back", ButtonStyle.Ghost, () => Open("Clan"),
                new Spec { Height = 48, PaddingX = 20, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold });
        }

        void LeaveClan(Transform body)
        {
            var clan = Data.CurrentClan;
            var form = Column(body, "Leave form", 12, Pad(16, 14, 14, 14));
            HubTheme.Text(form, "Title", "Leave " + clan.Name + "?", 22, HubTheme.Cream, HubTheme.Face.WideHeavy);
            HubTheme.Text(form, "Note", clan.Created ? "You're this demo clan's only member. Leaving closes the clan. You can create another anytime."
                : "You'll lose access to this clan's chat. You can rejoin from the clan browser.", 14, HubTheme.Secondary);
            var actions = Row(form, "Actions", 8, null, TextAnchor.MiddleLeft);
            Button(actions, "Confirm leave clan", "Leave clan", ButtonStyle.Danger, () =>
            {
                string name = clan.Name;
                rendering = true; Data.Leave(); rendering = false;
                view = "Clan"; status = ""; Rebuild();
                Notice("Clan left", "You left " + name + ".", () => Open("Clan"));
            }, new Spec { Height = 48, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold, FlexWidth = 1 });
            Button(actions, "Cancel leave clan", "Stay", ButtonStyle.Ghost, () => Open("Clan"),
                new Spec { Height = 48, PaddingX = 20, Radius = 12, FontSize = 15, Tracking = 0, Face = HubTheme.Face.BodyBold });
        }

        // ---- Conversation ----------------------------------------------------------------------

        void Conversation(Transform body, string thread)
        {
            bool clan = thread.StartsWith("clan:");
            var messages = ScrollList(body, "Messages", out chatScroll, 8, Pad(12, 14, 14, 12));
            // Pin the column to the bottom so short threads sit above the composer like the mock.
            messages.anchorMin = new Vector2(0, 0); messages.anchorMax = new Vector2(1, 0); messages.pivot = new Vector2(0.5f, 0);
            messages.anchoredPosition = Vector2.zero;
            foreach (var message in Data.History(thread))
            {
                var block = Column(messages, "Message", 4, null, message.Mine ? TextAnchor.UpperRight : TextAnchor.UpperLeft, false);
                if (clan && !message.Mine)
                    HubTheme.Text(block, "Author", message.Author + " · " + Role(message.Author), 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
                Bubble(block, message.Body, message.Mine);
            }
            scrollToEnd = true;

            Rule(body, "Composer rule", HubTheme.PanelBorderBright);
            var composer = Row(body, "Composer", 8, Pad(12, 14, 14, 12), TextAnchor.MiddleLeft);
            string draft = drafts.TryGetValue(thread, out string saved) ? saved : "";
            var field = Input(composer, "Message draft", "Message " + Data.ThreadName(thread).Replace("[" + (Data.CurrentClan?.Tag ?? "") + "] ", ""), draft,
                v => drafts[thread] = v, _ => Send(thread), 52, 12, null, null, null, 280);
            field.richText = false;
            IconButton(composer, "Send message", "arrow-up", () => Send(thread), 52, HubTheme.OnGold, HubTheme.Gold, 20, 12);
            if (status.Length > 0) HubTheme.Text(body, "Status", status, 13, HubTheme.DangerText);
        }

        static void Bubble(Transform parent, string text, bool mine)
        {
            const float maxWidth = 280;
            var bubble = Column(parent, "Bubble", 0, Pad(10, 14));
            Background(bubble, mine ? HubTheme.Gold : HubTheme.Hex("1F3A3C"), 14);
            var label = HubTheme.Text(bubble, "Text", text, 15, mine ? HubTheme.OnGold : HubTheme.Cream);
            label.lineSpacing = 8;
            float width = Mathf.Min(maxWidth, label.GetPreferredValues(text, maxWidth, 0).x + 2);
            Size(bubble, width: width + 28, minWidth: width + 28);
        }

        void Send(string thread)
        {
            drafts.TryGetValue(thread, out string draft);
            rendering = true; status = Data.Send(thread, draft) ?? ""; rendering = false;
            if (status.Length == 0) { drafts[thread] = ""; StartCoroutine(ReplyLater(thread)); }
            Rebuild();
        }
        IEnumerator ReplyLater(string thread)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Receive(thread, thread.StartsWith("clan:") ? "Mira" : Data.ThreadName(thread), "Sounds good! I'll meet you after this match.");
        }
        void Receive(string thread, string author, string body)
        {
            bool reading = panel != null && ((view == "Chat" && SelectedThread == thread) || (view == "Clan" && Data.CurrentClan != null && Data.CurrentClan.Thread == thread));
            if (Data.Receive(thread, author, body, reading) && !reading)
                Notice(author + " messaged you", body, () => OpenChat(thread));
        }

        // ---- Notices -----------------------------------------------------------------------------

        void Notice(string title, string message, Action clicked)
        {
            Remove(toast);
            toast = Centered(safe, "Social notification", 420, 70, 0, 900 * 0.5f - 60);
            Background(toast, HubTheme.Rgba(14, 30, 36, 0.96f), 14, true);
            Border(toast, HubTheme.PanelBorderBright, 14);
            var row = Row(toast, "Row", 12, Pad(10, 14, 10, 12), TextAnchor.MiddleLeft, false, false);
            Stretch(row);
            Avatar(row, "Avatar", 36, 18, AvatarColor(title), Initial(title), 14, HubTheme.Cream);
            var lines = Column(row, "Lines", 2, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1, minWidth: 0);
            HubTheme.Text(lines, "Title", title, 13, HubTheme.Gold, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            var text = HubTheme.Text(lines, "Message", message, 13, HubTheme.Cream, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            text.overflowMode = TextOverflowModes.Ellipsis;
            IconButton(row, "Dismiss notification", "close-small", DismissNotice, 28, HubTheme.Faint, null, 12, 7);
            Clickable(toast, () => { DismissNotice(); clicked(); });
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

        void LateUpdate()
        {
            if (!scrollToEnd || chatScroll == null) return;
            scrollToEnd = false;
            Canvas.ForceUpdateCanvases();
            chatScroll.verticalNormalizedPosition = 0;
        }

        // ---- Pieces ------------------------------------------------------------------------------

        static void SectionLabel(Transform parent, string text, int count, int topPad)
        {
            var row = Row(parent, text, 8, Pad(topPad, 2, 0, 2), TextAnchor.MiddleLeft, false, false);
            HubTheme.Label(row, text, HubTheme.Secondary, 12, 0.14f);
            if (count >= 0) HubTheme.Label(row, count.ToString(), HubTheme.Gold, 12, 0.1f);
        }

        // Avatar, name and detail line; actions go after it. Returns the row.
        static RectTransform PersonRow(Transform parent, string name, string person, string detail, Color? presence)
        {
            var row = Row(parent, name, 12, Pad(6, 8, 8, 6), TextAnchor.MiddleLeft, false, false);
            Avatar(row, "Avatar", 44, 22, AvatarColor(person), Initial(person), 17, HubTheme.Cream, null, presence, 12, 2.5f);
            var lines = Column(row, "Lines", 2, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1, minWidth: 0);
            HubTheme.Text(lines, "Name", person, 16, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            var line = HubTheme.Text(lines, "Detail", detail, 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            line.overflowMode = TextOverflowModes.Ellipsis;
            return row;
        }

        static void Badge(Transform parent, int count)
        {
            var disc = Disc(parent, "Badge", 22, HubTheme.Gold);
            Size(disc, 22, 22, minWidth: 22, minHeight: 22);
            var label = HubTheme.Text(disc.transform, "Count", count > 99 ? "99+" : count.ToString(), 12, HubTheme.OnGold, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.Center, false);
            Stretch(label.rectTransform);
        }

        static void TagBadge(Transform parent, string tag)
        {
            var rect = Rect(parent, "Tag");
            Size(rect, 72, 44, minWidth: 72, minHeight: 44);
            Background(rect, HubTheme.Hex("3A452E"), 8);
            var label = HubTheme.Text(rect, "Text", tag, 13, HubTheme.Gold, HubTheme.Face.WideHeavy, 0.1f, TextAlignmentOptions.Center, false);
            Stretch(label.rectTransform);
        }

        static void Clickable(RectTransform rect, Action onClick)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = Background(rect, Color.clear, 0, true);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
        }

        static string Initial(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        static Color AvatarColor(string name)
        {
            string[] palette = { "5B7D5A", "6B5B8E", "4A6E8E", "7A5A48", "5A6E3A", "8E5B6B" };
            int hash = 0;
            foreach (char c in name ?? "") hash = hash * 31 + c;
            return HubTheme.Hex(palette[Mathf.Abs(hash) % palette.Length]);
        }
        string Role(string author) => author == "Mira" ? "Leader" : author == "Rowan" ? "Officer" : author == "Clan notice" ? "" : "Member";

        static void Remove(RectTransform rect) { if (rect != null) { rect.gameObject.SetActive(false); Destroy(rect.gameObject); } }
        void UpdatePlayerName() => Data.RenamePlayer(LocalPlayerProfile.DisplayName);
        void OnDestroy() { LocalPlayerProfile.Changed -= UpdatePlayerName; if (Data != null) Data.Changed -= OnChanged; if (instance == this) instance = null; }
    }
}
