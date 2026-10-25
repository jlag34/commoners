using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SwarmPrototype.HubWidgets;

namespace SwarmPrototype
{
    // Home hub (handoff screens/Hub.dc.html): top bar, friends column, the commoner on stage, chat and play.
    // The skeleton is built once; the dynamic parts (invite card, friends list, chat, play panel) rebuild
    // from HubState whenever their section changes.
    public class HomeHub : MonoBehaviour
    {
        public HubState State { get; private set; }
        public HomeUnitPreview Preview { get; private set; }
        public Button PlayButton { get; private set; }

        RectTransform root, inviteSlot, addSlot, friendsContent, chatTabs, chatHeader, chatContent, playBody, nav;
        TMP_Text onlineLabel, statusLabel, nameLabel, initialLabel;
        TMP_InputField chatInput;
        ScrollRect chatScroll, friendsScroll;
        Action openSettings, openProfile, openRanked, play;
        bool scrollChatToEnd, builtAddOpen;
        string builtNote;

        public void Build(RectTransform parent, HubState state, Action openSettings, Action openProfile, Action openRanked, Action play)
        {
            State = state;
            this.openSettings = openSettings;
            this.openProfile = openProfile;
            this.openRanked = openRanked;
            this.play = play;
            root = Fill(parent, "Hub");
            BuildBackdrop();
            BuildTopBar();
            BuildLeftColumn();
            BuildCenter();
            BuildRightColumn();
            Stretch(HubTheme.Text(Fixed(root, "Preview build", 0, 900 - 12 - 14, 1600, 14), "Label", "PREVIEW BUILD", 10, HubTheme.Faint,
                HubTheme.Face.Wide, 0.18f, TextAlignmentOptions.Center, false).rectTransform);
            State.Changed += OnChanged;
            LocalPlayerProfile.Changed += RefreshProfile;
            OnChanged(HubState.Section.All);
        }

        void OnDestroy()
        {
            if (State != null) State.Changed -= OnChanged;
            LocalPlayerProfile.Changed -= RefreshProfile;
        }

        void OnChanged(HubState.Section section)
        {
            if ((section & HubState.Section.Profile) != 0) RefreshProfile();
            if ((section & HubState.Section.Invite) != 0) RefreshInvite();
            if ((section & HubState.Section.Friends) != 0) RefreshFriends();
            if ((section & HubState.Section.Chat) != 0) RefreshChat();
            if ((section & HubState.Section.Play) != 0) RefreshPlay();
        }

        void LateUpdate()
        {
            if (!scrollChatToEnd || chatScroll == null) return;
            scrollChatToEnd = false;
            Canvas.ForceUpdateCanvases();
            chatScroll.verticalNormalizedPosition = 0;
        }

        // ---- Skeleton -------------------------------------------------------------------------

        void BuildBackdrop()
        {
            var gradient = Fill(root, "Ground").gameObject.AddComponent<RadialGradient>();
            gradient.Center = new Vector2(0.5f, 0.48f);
            gradient.Radius = new Vector2(0.34f, 0.52f);
            gradient.Colors = new[] { HubTheme.GroundCenter, HubTheme.GroundMid, HubTheme.GroundFar, HubTheme.GroundEdge };
            gradient.Stops = new[] { 0f, 0.4f, 0.75f, 1f };
            gradient.raycastTarget = false;

            // 900 box at left 350, top 18: centre 468px from the top.
            var compass = Centered(root, "Compass", 900, 900, 0, 450 - 468).gameObject.AddComponent<CompassRings>();
            compass.raycastTarget = false;
            compass.color = new Color(1, 1, 1, 0.5f);
            compass.SecondsPerTurn = 220;
            compass.Rings.Add(new CompassRings.Ring { Radius = 440, Dash = 2, Gap = 7, Width = 1, Color = HubTheme.Alpha(HubTheme.Teal, 0.16f) });
            compass.Rings.Add(new CompassRings.Ring { Radius = 330, Dash = 1, Gap = 9, Width = 1, Color = HubTheme.Alpha(HubTheme.Teal, 0.12f) });
            foreach (var p in new[] { new Vector2(0, 440), new Vector2(-440, 0), new Vector2(440, 0) })
                compass.Dots.Add(new CompassRings.Dot { Position = p, Radius = 4, Width = 1, Color = HubTheme.Alpha(HubTheme.Gold, 0.5f) });
        }

        void BuildTopBar()
        {
            var bar = Fixed(root, "Top bar", 32, 28, 1536, 56);

            var brand = Row(Fixed(bar, "Brand", 0, 0, 344, 56), "Row", 14, null, TextAnchor.MiddleLeft, false, false);
            Stretch(brand);
            var badge = Rect(brand, "Badge");
            Size(badge, 44, 44, minWidth: 44, minHeight: 44);
            Place(Ring(badge, "Edge", 44, 1.5f, HubTheme.Alpha(HubTheme.Gold, 0.55f)).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            Stretch(HubTheme.Text(badge, "C", "C", 20, HubTheme.Gold, HubTheme.Face.Logo, 0, TextAlignmentOptions.Center, false).rectTransform);
            var words = Column(brand, "Wordmark", 3, null, TextAnchor.MiddleLeft, false);
            HubTheme.Text(words, "COMMONERS", "COMMONERS", 15, HubTheme.Gold, HubTheme.Face.Logo, 0.22f, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(words, "Tagline", "Your next great little army.", 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);

            nav = Row(bar, "Navigation", 4, Pad(5, 5), TextAnchor.MiddleCenter, false, false);
            nav.anchorMin = nav.anchorMax = nav.pivot = new Vector2(0.5f, 0.5f);
            nav.anchoredPosition = Vector2.zero;
            FitContent(nav, true, true);
            Background(nav, HubTheme.Bar, 12, true);
            Border(nav, HubTheme.PanelBorder, 12);
            var navSpec = new Spec { Height = 44, PaddingX = 26, Radius = 8, FontSize = 13, Tracking = 0.16f, Face = HubTheme.Face.Wide };
            var homeTab = Button(nav, "Home tab", "HOME", ButtonStyle.NavActive, ShowHome, navSpec);
            homeTab.GetComponentInChildren<TMP_Text>().margin = new Vector4(0.16f * 13, 0, 0, 7);
            var underline = Rect(homeTab.transform, "Underline");
            underline.sizeDelta = new Vector2(18, 2);
            Background(underline, HubTheme.Gold, 1);
            Place(underline, new Vector2(0.5f, 1), new Vector2(0, -32.5f));
            Button(nav, "Customize tab", "CUSTOMIZE", ButtonStyle.Nav, () => Placeholder("Customize is on its way. Your commoner wears the original outfit for now."), navSpec);
            Button(nav, "Clan tab", "CLANS", ButtonStyle.Nav, () => { State.SelectTab("clan"); Placeholder("Clan pages come next. Hearthguard's chat is open on the right."); }, navSpec);
            Button(nav, "Store tab", "STORE", ButtonStyle.Nav, () => Placeholder("The store opens later. Nothing is for sale yet."), navSpec);

            var settings = Button(bar, "Settings", "SETTINGS", ButtonStyle.Ghost, () => openSettings(),
                new Spec { Height = 48, PaddingX = 20, Radius = 10, FontSize = 13, Tracking = 0.16f, Face = HubTheme.Face.Wide, Icon = "gear", IconSize = 18, Gap = 10 });
            var settingsRect = (RectTransform)settings.transform;
            settingsRect.anchorMin = settingsRect.anchorMax = new Vector2(1, 0.5f);
            settingsRect.pivot = new Vector2(1, 0.5f);
            settingsRect.anchoredPosition = Vector2.zero;
            var tint = settings.GetComponent<HubHover>();
            tint.Normal = HubTheme.Bar; tint.Fill.color = HubTheme.Bar;
            tint.Hover = Color.Lerp(HubTheme.Bar, HubTheme.Cream, 0.08f); tint.Hover.a = 0.78f;
            tint.NormalBorder = HubTheme.PanelBorderBright; tint.Border.color = HubTheme.PanelBorderBright;
        }

        void BuildLeftColumn()
        {
            var column = Column(Inset(root, "Left column", 32, 112, 1600 - 32 - 344, 32), "Column", 14);
            Stretch(column);

            // Profile card (click to edit the local username)
            var card = Panel(column, "Player identity");
            var cardRow = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            cardRow.padding = Pad(14, 16); cardRow.spacing = 14; cardRow.childAlignment = TextAnchor.MiddleLeft;
            cardRow.childControlWidth = cardRow.childControlHeight = true; cardRow.childForceExpandWidth = false; cardRow.childForceExpandHeight = false;
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.targetGraphic = card.GetComponent<Image>();
            cardButton.transition = Selectable.Transition.None;
            cardButton.onClick.AddListener(() => openProfile());
            var cardHover = card.gameObject.AddComponent<HubHover>();
            cardHover.Fill = card.GetComponent<Image>(); cardHover.Normal = cardHover.DisabledFill = HubTheme.Panel;
            cardHover.Hover = cardHover.Pressed = Color.Lerp(HubTheme.Panel, HubTheme.Cream, 0.05f);
            cardHover.Border = card.Find("Border").GetComponent<Image>(); cardHover.NormalBorder = HubTheme.PanelBorder; cardHover.HoverBorder = HubTheme.Alpha(HubTheme.Gold, 0.45f);
            var avatar = Avatar(card, "Avatar", 52, 12, HubTheme.Hex("1D3940"), "C", 24, HubTheme.Gold, HubTheme.Alpha(HubTheme.Gold, 0.35f), HubTheme.Online, 12, 2.5f, HubTheme.Face.Logo);
            initialLabel = avatar.Find("Initial").GetComponent<TMP_Text>();
            var identity = Column(card, "Identity", 4, null, TextAnchor.MiddleLeft, false);
            nameLabel = HubTheme.Text(identity, "Profile username", HubState.PlayerName, 19, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            var line = Row(identity, "Clan line", 8, null, TextAnchor.MiddleLeft, false, false);
            HubTheme.Text(line, "Tag", "[" + HubState.ClanTag + "]", 12, HubTheme.Gold, HubTheme.Face.Wide, 0.08f, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(line, "Clan", HubState.ClanName, 12, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(line, "Dot", "·", 12, HubTheme.Offline, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            statusLabel = HubTheme.Text(line, "Status", "Online", 12, HubTheme.Online, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);

            inviteSlot = Column(column, "Party invite", 0);

            // Friends panel
            var friends = Panel(column, "Friends");
            Size(friends, flexHeight: 1, minHeight: 0);
            var friendsColumn = friends.gameObject.AddComponent<VerticalLayoutGroup>();
            friendsColumn.childControlWidth = friendsColumn.childControlHeight = true;
            friendsColumn.childForceExpandWidth = true; friendsColumn.childForceExpandHeight = false;
            friends.gameObject.AddComponent<RectMask2D>();

            var header = Row(friends, "Header", 10, Pad(18, 10, 8, 6), TextAnchor.MiddleLeft, false, false);
            HubTheme.Text(header, "Title", "FRIENDS", 13, HubTheme.Cream, HubTheme.Face.WideHeavy, 0.18f, TextAlignmentOptions.MidlineLeft, false);
            onlineLabel = HubTheme.Text(header, "Online count", "", 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            Spacer(header);
            IconButton(header, "Add friend", "add-friend", () => State.ToggleAdd(), 44, HubTheme.Pale, iconSize: 20);

            addSlot = Column(friends, "Add slot", 0);

            var searchBox = Column(friends, "Search", 0, Pad(16, 0, 16, 10));
            Input(searchBox, "Search friends", "Search friends", State.Search, v => State.SetSearch(v), null, 44, 10, HubTheme.TrackSoft, HubTheme.PanelBorderStrong, "search");

            friendsContent = ScrollList(friends, "Friend list", out friendsScroll, 0, Pad(8, 0, 8, 12));
        }

        void BuildCenter()
        {
            var stage = Fixed(root, "Unit preview", 536, 104, 528, 654);
            Preview = stage.gameObject.AddComponent<HomeUnitPreview>();
            Preview.Build();
            // The portrait render target is square; show its middle column at the stage's aspect.
            var raw = stage.GetComponent<RawImage>();
            if (raw != null) { float aspect = 528f / 654f; raw.uvRect = new Rect((1 - aspect) / 2, 0, aspect, 1); }

            var caption = Column(Fixed(root, "Caption", 400, 744, 800, 90), "Column", 7, null, TextAnchor.UpperCenter, false);
            Stretch(caption);
            HubTheme.Text(caption, "Unit name", "THE COMMONER", 22, HubTheme.Cream, HubTheme.Face.WideHeavy, 0.14f, TextAlignmentOptions.Center, false).margin = new Vector4(0.14f * 22, 0, 0, 0);
            HubTheme.Text(caption, "Original outfit", "Original outfit · Equipped", 13, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.Center, false);
            var hint = Row(caption, "Rotate hint", 8, Pad(6, 0, 0, 0), TextAnchor.MiddleCenter, false, false);
            Icon(hint, "Icon", "rotate", 16, HubTheme.Tertiary);
            HubTheme.Text(hint, "Label", "DRAG TO ROTATE", 11, HubTheme.Tertiary, HubTheme.Face.Wide, 0.18f, TextAlignmentOptions.MidlineLeft, false);
        }

        void BuildRightColumn()
        {
            var column = Column(Inset(root, "Right column", 1600 - 32 - 344, 112, 32, 32), "Column", 14);
            Stretch(column);

            // Chat
            var chat = Panel(column, "Chat");
            Size(chat, flexHeight: 1, minHeight: 0);
            var chatColumn = chat.gameObject.AddComponent<VerticalLayoutGroup>();
            chatColumn.childControlWidth = chatColumn.childControlHeight = true;
            chatColumn.childForceExpandWidth = true; chatColumn.childForceExpandHeight = false;
            chat.gameObject.AddComponent<RectMask2D>();

            chatTabs = Row(chat, "Tabs", 2, Pad(8, 0, 8, 0), TextAnchor.LowerLeft, false, false);
            Size(chatTabs, height: 46, minHeight: 46);
            Rule(chat, "Tabs rule", HubTheme.PanelBorder);
            chatHeader = Row(chat, "Channel", 12, Pad(18, 10, 18, 4), TextAnchor.MiddleLeft, false, false);
            chatContent = ScrollList(chat, "Messages", out chatScroll, 14, Pad(18, 8, 18, 14));
            Rule(chat, "Composer rule", HubTheme.Rgba(156, 194, 188, 0.1f));
            var composer = Row(chat, "Composer", 8, Pad(12, 10, 12, 12), TextAnchor.MiddleLeft);
            chatInput = Input(composer, "Chat input", "", "", v => State.Drafts[State.ChatTab] = v, SendChat, 44, 10, HubTheme.Track, HubTheme.PanelBorderStrong, null, 240);
            chatInput.richText = false;
            IconButton(composer, "Send message", "send", () => SendChat(chatInput.text), 44, HubTheme.Gold, HubTheme.GoldWash);

            // Play
            var playPanel = Panel(column, "Play", 14, HubTheme.PanelStrong, HubTheme.PanelBorderStrong);
            playBody = playPanel;
            var playColumn = playPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            playColumn.padding = Pad(16, 14, 16, 16); playColumn.spacing = 12;
            playColumn.childControlWidth = playColumn.childControlHeight = true;
            playColumn.childForceExpandWidth = true; playColumn.childForceExpandHeight = false;
        }

        // ---- Dynamic sections -----------------------------------------------------------------

        void RefreshProfile()
        {
            if (nameLabel == null) return;
            nameLabel.text = HubState.PlayerName;
            initialLabel.text = HubState.PlayerName.Substring(0, 1).ToUpperInvariant();
            statusLabel.text = State.InOtherParty ? "In a party" : "Online";
        }

        void RefreshInvite()
        {
            Clear(inviteSlot);
            inviteSlot.gameObject.SetActive(State.InviteVisible && !State.InOtherParty);
            if (!inviteSlot.gameObject.activeSelf) return;
            var card = Panel(inviteSlot, "Invite card", 14, HubTheme.Rgba(48, 56, 40, 0.92f), HubTheme.Alpha(HubTheme.Gold, 0.45f));
            var body = card.gameObject.AddComponent<VerticalLayoutGroup>();
            body.padding = Pad(16, 14, 16, 14); body.spacing = 12;
            body.childControlWidth = body.childControlHeight = true; body.childForceExpandWidth = true; body.childForceExpandHeight = false;
            var row = Row(card, "Who", 12, null, TextAnchor.MiddleLeft, false, false);
            Avatar(row, "Avatar", 40, 10, HubTheme.Hex("5B4450"), "B", 17, HubTheme.Cream, null, HubTheme.Online, 10, 2);
            var lines = Column(row, "Lines", 3, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1);
            var invite = HubTheme.Text(lines, "Invite", "<b>Bramblewick</b> invited you to their party", 14, HubTheme.Cream);
            invite.richText = true;
            HubTheme.Text(lines, "Detail", "Quick match · Large FFA · 2 of 3", 12, HubTheme.Parchment, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            var actions = Row(card, "Actions", 8, null, TextAnchor.MiddleLeft);
            Button(actions, "Join party", "JOIN PARTY", ButtonStyle.Gold, () => State.AcceptInvite(), new Spec { Height = 44, Radius = 9, FlexWidth = 1 });
            var decline = Button(actions, "Decline invite", "DECLINE", ButtonStyle.Ghost, () => State.DeclineInvite(), new Spec { Height = 44, Radius = 9, FlexWidth = 1 });
            var tint = decline.GetComponent<HubHover>();
            tint.NormalBorder = HubTheme.Rgba(242, 236, 220, 0.2f); tint.Border.color = tint.NormalBorder;
        }

        void RefreshFriends()
        {
            onlineLabel.text = State.OnlineCount + " online";
            if (builtAddOpen != State.AddOpen || builtNote != State.Note) RefreshAddForm();
            RefreshFriendList();
        }

        // Rebuilt only when it changes, so typing in search never steals focus from the add-friend field.
        void RefreshAddForm()
        {
            builtAddOpen = State.AddOpen; builtNote = State.Note;
            Clear(addSlot);
            if (State.AddOpen)
            {
                var add = Column(addSlot, "Add friend form", 8, Pad(16, 4, 16, 12));
                HubTheme.Label(add, "ADD BY NAME OR FRIEND CODE");
                var row = Row(add, "Row", 8, null, TextAnchor.MiddleLeft);
                var field = Input(row, "Friend code", "e.g. Copperpot#2210", State.AddName, v => State.SetAddName(v), _ => State.SendFriendRequest());
                Button(row, "Send request", "SEND", ButtonStyle.Gold, () => State.SendFriendRequest(), new Spec { Height = 44, PaddingX = 16, Radius = 10 });
                field.Select(); field.ActivateInputField();
            }
            if (!string.IsNullOrEmpty(State.Note))
            {
                var note = Column(addSlot, "Note", 0, Pad(18, 0, 18, 10));
                HubTheme.Text(note, "Note text", State.Note, 12, HubTheme.Online);
            }
        }

        void RefreshFriendList()
        {
            Clear(friendsContent);
            if (State.Requests.Count > 0)
            {
                var head = Column(friendsContent, "Requests header", 0, Pad(10, 8, 10, 6));
                HubTheme.Label(head, "REQUESTS · " + State.Requests.Count, HubTheme.Gold);
                foreach (var request in State.Requests.ToArray()) RequestRow(request);
            }
            var groups = new (string label, Presence status)[] { ("IN MATCH", Presence.InMatch), ("ONLINE", Presence.Online), ("OFFLINE", Presence.Offline) };
            int shown = 0;
            foreach (var group in groups)
            {
                var members = State.Visible.Where(f => f.Status == group.status).ToArray();
                if (members.Length == 0) continue;
                shown += members.Length;
                var head = Column(friendsContent, group.label, 0, Pad(10, 14, 10, 6));
                HubTheme.Label(head, group.label + " · " + members.Length);
                foreach (var friend in members) FriendRow(friend);
            }
            if (shown == 0 && State.Requests.Count == 0)
            {
                var empty = Column(friendsContent, "No results", 0, Pad(10, 18, 10, 18));
                HubTheme.Text(empty, "Text", "No friends match that name.", 13, HubTheme.Tertiary);
            }
        }

        void RequestRow(HubFriend request)
        {
            var row = Row(friendsContent, "Request " + request.Name, 12, Pad(10, 6, 4, 6), TextAnchor.MiddleLeft, false, false);
            Background(row, HubTheme.Rgba(230, 194, 119, 0.07f), 10);
            Avatar(row, "Avatar", 36, 9, HubTheme.Hex("5A4E3A"), request.Initial, 15, HubTheme.Cream);
            var lines = Column(row, "Lines", 2, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1);
            HubTheme.Text(lines, "Name", request.Name, 14, HubTheme.Cream, HubTheme.Face.BodyMedium, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(lines, "Detail", "Wants to be friends", 12, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            IconButton(row, "Accept " + request.Name, "check", () => State.Accept(request), 44, HubTheme.Online, HubTheme.OnlineWash);
            IconButton(row, "Decline " + request.Name, "close", () => State.Decline(request), 44, HubTheme.Secondary);
        }

        void FriendRow(HubFriend f)
        {
            bool open = State.Expanded == f.Id || State.Confirming == f.Id;
            bool inParty = State.InParty(f);
            var wrap = Column(friendsContent, f.Name, 0);
            Background(wrap, open ? HubTheme.OpenWash : Color.clear, 10);

            var row = Row(wrap, "Row", 10, Pad(10, 2, 2, 2), TextAnchor.MiddleLeft, false, false);
            Size(row, minHeight: 52);
            var rowTint = row.gameObject.AddComponent<Image>();
            rowTint.color = Color.clear; rowTint.raycastTarget = true;
            rowTint.sprite = HubArt.Rounded(10); rowTint.type = Image.Type.Sliced;
            var hover = row.gameObject.AddComponent<HubHover>();
            hover.Fill = rowTint; hover.Normal = hover.DisabledFill = Color.clear; hover.Hover = hover.Pressed = HubTheme.RowWash;

            Color dot = f.Status == Presence.InMatch ? HubTheme.InMatch : f.Status == Presence.Online ? HubTheme.Online : HubTheme.Offline;
            var avatar = Avatar(row, "Avatar", 36, 9, f.Avatar, f.Initial, 15, HubTheme.Cream, null, dot, 10, 2);
            if (f.Status == Presence.Offline) avatar.gameObject.AddComponent<CanvasGroup>().alpha = 0.7f;
            var lines = Column(row, "Lines", 2, null, TextAnchor.MiddleLeft);
            Size(lines, flexWidth: 1, minWidth: 0);
            var name = HubTheme.Text(lines, "Name", f.Name, 14, HubTheme.Cream, HubTheme.Face.BodyMedium, 0, TextAlignmentOptions.MidlineLeft, false);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Color detailColor = f.Status == Presence.InMatch ? HubTheme.InMatch : f.Status == Presence.Online ? HubTheme.Secondary : HubTheme.Faint;
            var detail = HubTheme.Text(lines, "Detail", f.Detail, 12, detailColor, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            detail.overflowMode = TextOverflowModes.Ellipsis;

            var actions = Row(row, "Actions", 0, null, TextAnchor.MiddleRight, false, false);
            if (State.CanMessage(f)) IconButton(actions, "Message " + f.Name, "message", () => State.OpenDirect(f.Id), 44, HubTheme.Secondary);
            if (State.CanInvite(f)) IconButton(actions, "Invite " + f.Name, "plus", () => State.InviteToParty(f), 44, HubTheme.Secondary);
            if (inParty)
            {
                var tag = Column(actions, "In party", 0, Pad(6, 0, 6, 0), TextAnchor.MiddleCenter, false);
                Size(tag, height: 44);
                HubTheme.Text(tag, "Label", "IN PARTY", 10, HubTheme.Gold, HubTheme.Face.Wide, 0.12f, TextAlignmentOptions.MidlineLeft, false);
            }
            IconButton(actions, "More " + f.Name, "dots", () => State.ToggleExpanded(f), 44, HubTheme.Secondary, open ? HubTheme.HoverWash : (Color?)null);

            if (State.Expanded == f.Id && State.Confirming != f.Id)
            {
                var more = Row(wrap, "More actions", 6, Pad(56, 2, 10, 10), TextAnchor.MiddleLeft, false, false);
                var small = new Spec { Height = 44, PaddingX = 12, Radius = 9, FontSize = 11, Tracking = 0.12f };
                if (f.InClan) InlineTag(more, "In clan", "IN HEARTHGUARD", HubTheme.Tertiary);
                else if (State.ClanInvitesSent.Contains(f.Id)) InlineTag(more, "Clan invite sent", "CLAN INVITE SENT", HubTheme.Online);
                else Button(more, "Invite to clan " + f.Name, "INVITE TO CLAN", ButtonStyle.Ghost, () => State.InviteToClan(f), small);
                Button(more, "Remove friend " + f.Name, "REMOVE FRIEND", ButtonStyle.DangerText, () => State.AskRemove(f), small);
            }
            if (State.Confirming == f.Id)
            {
                var confirm = Column(wrap, "Confirm", 8, Pad(56, 2, 10, 12));
                HubTheme.Text(confirm, "Question", "Remove " + f.Name + " from your friends?", 13, HubTheme.Soft);
                var buttons = Row(confirm, "Buttons", 6, null, TextAnchor.MiddleLeft, false, false);
                var small = new Spec { Height = 44, PaddingX = 14, Radius = 9, FontSize = 11, Tracking = 0.12f };
                Button(buttons, "Cancel remove", "CANCEL", ButtonStyle.Ghost, () => State.CancelRemove(f), small);
                Button(buttons, "Confirm remove", "REMOVE", ButtonStyle.Danger, () => State.Remove(f), small);
            }
        }

        static void InlineTag(Transform parent, string name, string label, Color color)
        {
            var tag = Column(parent, name, 0, Pad(4, 0, 4, 0), TextAnchor.MiddleCenter, false);
            Size(tag, height: 44);
            HubTheme.Text(tag, "Label", label, 11, color, HubTheme.Face.Wide, 0.12f, TextAlignmentOptions.MidlineLeft, false);
        }

        void RefreshChat()
        {
            Clear(chatTabs);
            var tabs = new List<(string id, string label, bool closable)> { ("clan", "CLAN", false), ("party", "PARTY", false) };
            foreach (var id in State.DirectTabs)
            {
                var friend = State.Find(id);
                tabs.Add((id, (friend != null ? friend.Name : id).ToUpperInvariant(), true));
            }
            foreach (var tab in tabs)
            {
                bool selected = tab.id == State.ChatTab;
                var wrap = Column(chatTabs, "Tab " + tab.id, 0, null, TextAnchor.LowerLeft, false);
                var row = Row(wrap, "Row", 0, null, TextAnchor.MiddleLeft, false, false);
                var button = Button(row, "Select " + tab.id, tab.label, ButtonStyle.Segment, () => State.SelectTab(tab.id),
                    new Spec { Height = 44, PaddingX = 12, Radius = 6, FontSize = 12, Tracking = 0.14f, Face = HubTheme.Face.Wide });
                var tint = button.GetComponent<HubHover>();
                tint.Hover = Color.clear;
                tint.NormalText = selected ? HubTheme.Cream : HubTheme.Tertiary; tint.HoverText = HubTheme.Cream; tint.Label.color = tint.NormalText;
                if (tab.closable)
                {
                    var close = IconButton(row, "Close " + tab.id, "close-small", () => State.CloseDirect(tab.id), 28, HubTheme.Faint, null, 12, 6);
                    ((RectTransform)close.transform).GetComponent<LayoutElement>().minWidth = 28;
                    var closeRow = row.GetComponent<HorizontalLayoutGroup>();
                    closeRow.padding = Pad(0, 0, 2, 0); closeRow.spacing = -6;
                }
                var underline = Rule(wrap, "Underline", selected ? HubTheme.Gold : Color.clear);
                Size(underline, height: 2, flexWidth: 1, minHeight: 2);
            }

            Clear(chatHeader);
            var channel = State.Channel;
            var titles = Column(chatHeader, "Titles", 2, null, TextAnchor.MiddleLeft);
            Size(titles, flexWidth: 1, minWidth: 0);
            HubTheme.Text(titles, "Title", channel.title, 15, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(titles, "Subtitle", channel.subtitle, 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            if (channel.isClan)
                Button(chatHeader, "Clan page", "CLAN PAGE", ButtonStyle.Link, () => Placeholder("Clan pages come next."),
                    new Spec { Height = 44, PaddingX = 4, Radius = 8, FontSize = 12, Tracking = 0.12f, Face = HubTheme.Face.Wide });

            Clear(chatContent);
            foreach (var message in State.History(State.ChatTab))
            {
                if (message.System)
                {
                    var system = HubTheme.Text(chatContent, "System", message.Text, 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.Center);
                    system.fontStyle = FontStyles.Italic;
                    continue;
                }
                var block = Column(chatContent, "Message", 3);
                var line = Row(block, "Byline", 8, null, TextAnchor.LowerLeft, false, false);
                HubTheme.Text(line, "Who", message.Who, 13, message.Mine ? HubTheme.Gold : HubTheme.Mint, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.BottomLeft, false);
                HubTheme.Text(line, "Time", message.Time, 11, HubTheme.Faint, HubTheme.Face.Body, 0, TextAlignmentOptions.BottomLeft, false);
                var body = HubTheme.Text(block, "Text", message.Text, 14, HubTheme.Soft);
                body.lineSpacing = 25;
            }
            scrollChatToEnd = true;

            chatInput.placeholder.GetComponent<TMP_Text>().text = channel.placeholder;
            chatInput.SetTextWithoutNotify(State.Drafts.TryGetValue(State.ChatTab, out var draft) ? draft : "");
        }

        void SendChat(string text)
        {
            if (!State.Send(text)) return;
            chatInput.SetTextWithoutNotify("");
            chatInput.ActivateInputField();
        }

        void RefreshPlay()
        {
            Clear(playBody);

            // Mode
            var modes = Row(playBody, "Mode", 4, Pad(4, 4), TextAnchor.MiddleLeft);
            Background(modes, HubTheme.Track, 10);
            foreach (var mode in HubState.Modes)
            {
                bool selected = mode.Id == State.ModeId;
                var button = Button(modes, "Mode " + mode.Id, mode.Label, selected ? ButtonStyle.SegmentActive : ButtonStyle.Segment, () => State.SetMode(mode.Id),
                    new Spec { Height = 40, Radius = 7, FontSize = 11, Tracking = 0.12f, PaddingX = 4, FlexWidth = 1 });
                button.interactable = !State.InOtherParty;
            }

            // Map size
            var mapBlock = Column(playBody, "Map size", 8);
            var mapHead = Row(mapBlock, "Head", 10, null, TextAnchor.LowerLeft, false, false);
            HubTheme.Label(mapHead, "MAP SIZE");
            Spacer(mapHead);
            HubTheme.Text(mapHead, "Description", State.Map.Description, 12, HubTheme.Soft, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineRight, false);
            var tiles = Row(mapBlock, "Tiles", 6, null, TextAnchor.MiddleLeft);
            foreach (var map in HubState.Maps) MapTile(tiles, map);

            // Party
            var party = Row(playBody, "Party", 12, null, TextAnchor.MiddleLeft, false, false);
            var partyText = Column(party, "Text", 3, null, TextAnchor.MiddleLeft);
            Size(partyText, flexWidth: 1, minWidth: 0);
            HubTheme.Label(partyText, State.PartyTitle);
            HubTheme.Text(partyText, "Subtitle", State.PartySubtitle, 12, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            if (State.InOtherParty)
            {
                var leave = Button(partyText, "Leave party", "LEAVE PARTY", ButtonStyle.DangerText, () => State.LeaveParty(),
                    new Spec { Height = 28, PaddingX = 0, Radius = 6, FontSize = 11, Tracking = 0.12f });
                leave.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft; // CSS align-self: flex-start
            }
            var slots = Row(party, "Slots", 8, null, TextAnchor.MiddleLeft, false, false);
            if (State.InOtherParty)
            {
                Slot(slots, "B", "Bramblewick, party leader", HubTheme.Hex("5B4450"), HubTheme.Cream, true, null);
                Slot(slots, "M", "Millie Grist", HubTheme.Hex("3F5A44"), HubTheme.Cream, false, null);
                Slot(slots, "C", "You", HubTheme.Hex("1D3940"), HubTheme.Gold, false, null);
            }
            else
            {
                Slot(slots, "C", "You", HubTheme.Hex("1D3940"), HubTheme.Gold, State.Party.Count > 0, null, true);
                for (int i = 0; i < HubState.PartySize - 1; i++)
                {
                    var member = i < State.Party.Count ? State.Find(State.Party[i]) : null;
                    if (member != null) { string id = member.Id; Slot(slots, member.Initial, "Remove " + member.Name + " from party", member.Avatar, HubTheme.Cream, false, () => State.RemoveFromParty(id)); }
                    else EmptySlot(slots);
                }
            }

            // Online modes are locked until the server exists; say so instead of faking a queue.
            if (State.PlayLocked)
            {
                var locked = Row(playBody, "Online lock", 8, null, TextAnchor.MiddleLeft, false, false);
                var note = HubTheme.Text(locked, "Note", "Unranked · Online play opens with verified server matches.", 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
                note.overflowMode = TextOverflowModes.Ellipsis;
                Size(note, flexWidth: 1, minWidth: 0);
                Button(locked, "Ranked details", "DETAILS", ButtonStyle.Link, () => openRanked(),
                    new Spec { Height = 28, PaddingX = 6, Radius = 6, FontSize = 11, Tracking = 0.12f, Face = HubTheme.Face.Wide });
            }

            // Play
            PlayButton = Button(playBody, "Play", State.PlayLabel, ButtonStyle.Gold, () => { if (State.InOtherParty) State.ToggleReady(); else play(); },
                new Spec { Height = 64, Radius = 10, FontSize = 22, Tracking = 0.2f, FlexWidth = 1, Shadow = true });
            PlayButton.interactable = !State.PlayLocked;
        }

        void MapTile(Transform parent, HubMap map)
        {
            bool selected = map.Id == State.MapId;
            var tile = Rect(parent, "Map " + map.Id);
            Size(tile, height: 82, flexWidth: 1, minHeight: 82);
            var fill = Background(tile, selected ? HubTheme.Alpha(HubTheme.Gold, 0.1f) : HubTheme.Rgba(6, 16, 20, 0.35f), 9, true);
            var border = Border(tile, selected ? HubTheme.Gold : HubTheme.PanelBorderBright, 9, 1.5f);
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;
            button.interactable = !State.InOtherParty;
            button.onClick.AddListener(() => State.SetMap(map.Id));
            var hover = tile.gameObject.AddComponent<HubHover>();
            hover.Fill = fill; hover.Normal = hover.Hover = hover.Pressed = hover.DisabledFill = fill.color;
            hover.Border = border; hover.NormalBorder = border.color; hover.HoverBorder = selected ? HubTheme.Gold : HubTheme.Alpha(HubTheme.Gold, 0.5f);

            var content = Column(tile, "Content", 6, Pad(4, 8, 4, 9), TextAnchor.LowerCenter, false);
            Stretch(content);
            var gridHolder = Column(content, "Grid holder", 0, null, TextAnchor.MiddleCenter, false);
            Size(gridHolder, flexHeight: 1);
            var grid = Rect(gridHolder, "Grid");
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(8, 8); layout.spacing = new Vector2(3, 3);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = map.Columns;
            int rows = Mathf.CeilToInt(map.Players / (float)map.Columns);
            Size(grid, map.Columns * 8 + (map.Columns - 1) * 3, rows * 8 + (rows - 1) * 3);
            for (int i = 0; i < map.Players; i++)
            {
                var cell = Rect(grid, "Base " + i);
                Background(cell, i == 0 ? HubTheme.Gold : selected ? HubTheme.Teal : HubTheme.Hex("557571"), 2);
            }
            HubTheme.Text(content, "Name", map.Name, 11, selected ? HubTheme.Cream : HubTheme.Pale, HubTheme.Face.WideHeavy, 0.1f, TextAlignmentOptions.Center, false);
            HubTheme.Text(content, "Size", map.Size, 11, HubTheme.Secondary, HubTheme.Face.Body, 0, TextAlignmentOptions.Center, false);
        }

        void Slot(Transform parent, string initial, string label, Color fill, Color text, bool leader, Action remove, bool you = false)
        {
            var slot = Rect(parent, label);
            Size(slot, 44, 44, minWidth: 44, minHeight: 44);
            var image = Background(slot, fill, 10, true);
            Border(slot, leader || you ? HubTheme.Alpha(HubTheme.Gold, 0.6f) : HubTheme.Rgba(242, 236, 220, 0.25f), 10, 1.5f);
            Stretch(HubTheme.Text(slot, "Initial", initial, 17, text, HubTheme.Face.WideHeavy, 0, TextAlignmentOptions.Center, false).rectTransform);
            if (remove != null)
            {
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => remove());
                var hover = slot.gameObject.AddComponent<HubHover>();
                hover.Fill = image; hover.Normal = hover.DisabledFill = fill; hover.Hover = hover.Pressed = Color.Lerp(fill, HubTheme.Danger, 0.35f);
            }
            if (leader)
            {
                var crown = Icon(slot, "Party leader", "crown", 24, Color.white);
                // CSS: left 14px, top -9px for the 16x12 artwork, which sits at (5, 7) inside the 24px icon box.
                Place(crown.rectTransform, new Vector2(0, 1), new Vector2(21, 4));
            }
        }

        static void EmptySlot(Transform parent)
        {
            var slot = Rect(parent, "Empty slot");
            Size(slot, 44, 44, minWidth: 44, minHeight: 44);
            var dashed = slot.gameObject.AddComponent<Image>();
            dashed.sprite = HubArt.DashedBox(44, 10, 1.5f);
            dashed.color = HubTheme.Alpha(HubTheme.Teal, 0.35f);
            dashed.raycastTarget = false;
            Place(Icon(slot, "Plus", "plus", 16, HubTheme.Faint).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        }

        // ---- Navigation ---------------------------------------------------------------------

        void ShowHome() => Placeholder(null);

        void Placeholder(string message)
        {
            if (message != null) Toast(root, message);
        }
    }
}
