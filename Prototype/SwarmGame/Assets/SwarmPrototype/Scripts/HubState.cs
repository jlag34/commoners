using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SwarmPrototype
{
    // Local, in-memory model behind the home hub: friends, requests, party, chat tabs, play setup.
    // Mirrors the handoff prototype's renderVals() state. Everything here is sample data until the
    // backend (PROTOCOL.md: auth, lobby, clans) replaces it; nothing is sent anywhere.
    public enum Presence { InMatch, Online, Offline }

    public sealed class HubFriend
    {
        public string Id, Name, Detail;
        public Presence Status;
        public Color Avatar;
        public bool InClan;
        public string Initial => Name.Substring(0, 1);
    }

    public sealed class HubMessage
    {
        public string Who, Time, Text;
        public bool System, Mine;
    }

    public sealed class HubMode { public string Id, Label, Play; public bool Online; }
    public sealed class HubMap { public string Id, Name, Size, Description; public int Columns, Players; }

    public sealed class HubState
    {
        [Flags]
        public enum Section { Profile = 1, Invite = 2, Friends = 4, Chat = 8, Play = 16, Settings = 32, All = 63 }
        public event Action<Section> Changed;

        public const int PartySize = 3;
        public const string ClanName = "Hearthguard", ClanTag = "HRTH";
        // Local display name (LocalPlayerProfile); online identity belongs to the server.
        public static string PlayerName => LocalPlayerProfile.DisplayName;
        const string Leader_Bramblewick = "bramblewick";

        public static readonly HubMode[] Modes =
        {
            new HubMode { Id = "quick", Label = "QUICK MATCH", Play = "PLAY", Online = true },
            new HubMode { Id = "custom", Label = "CUSTOM", Play = "FIND LOBBY", Online = true },
            new HubMode { Id = "practice", Label = "PRACTICE", Play = "PRACTICE" }
        };
        public static readonly HubMap[] Maps =
        {
            new HubMap { Id = "duel", Name = "DUEL", Size = "1v1", Description = "Duel · 1v1", Columns = 2, Players = 2 },
            new HubMap { Id = "small", Name = "SMALL", Size = "4 FFA", Description = "Small FFA · 4 players", Columns = 2, Players = 4 },
            new HubMap { Id = "medium", Name = "MEDIUM", Size = "6 · 2×3", Description = "Medium · 6 players · 2×3", Columns = 3, Players = 6 },
            new HubMap { Id = "large", Name = "LARGE", Size = "9 FFA", Description = "Large FFA · 9 players · 3×3", Columns = 3, Players = 9 }
        };

        public readonly List<HubFriend> Friends = new List<HubFriend>
        {
            new HubFriend { Id = "mudlark", Name = "Mudlark", Status = Presence.InMatch, Detail = "In match · 12 min", Avatar = HubTheme.Hex("3E5F5B"), InClan = true },
            new HubFriend { Id = "tallow", Name = "Tallow", Status = Presence.InMatch, Detail = "In match · 4 min", Avatar = HubTheme.Hex("4A4F66"), InClan = true },
            new HubFriend { Id = Leader_Bramblewick, Name = "Bramblewick", Status = Presence.Online, Detail = "In a party · 2 of 3", Avatar = HubTheme.Hex("5B4450"), InClan = true },
            new HubFriend { Id = "millie", Name = "Millie Grist", Status = Presence.Online, Detail = "In a party · 2 of 3", Avatar = HubTheme.Hex("3F5A44"), InClan = true },
            new HubFriend { Id = "oxcart", Name = "Oxcart", Status = Presence.Online, Detail = "Online", Avatar = HubTheme.Hex("5A4E3A") },
            new HubFriend { Id = "ditchdigger", Name = "Ditchdigger", Status = Presence.Offline, Detail = "Last online 2 days ago", Avatar = HubTheme.Hex("34474A") },
            new HubFriend { Id = "pete", Name = "Pitchfork Pete", Status = Presence.Offline, Detail = "Last online 5 days ago", Avatar = HubTheme.Hex("34474A") }
        };
        public readonly List<HubFriend> Requests = new List<HubFriend>
        {
            new HubFriend { Id = "copperpot", Name = "Copperpot", Status = Presence.Online, Detail = "Online", Avatar = HubTheme.Hex("5A4E3A") }
        };
        public readonly HashSet<string> ClanInvitesSent = new HashSet<string>();
        public readonly List<string> Party = new List<string>();          // friend ids when you lead
        public string PartyLeader;                                          // friend id when you joined theirs
        public bool InviteVisible = true, Ready;
        public readonly List<string> DirectTabs = new List<string> { "mudlark" };
        public string ChatTab = "clan", Expanded, Confirming, Note, Search = "", AddName = "";
        public bool AddOpen;
        public readonly Dictionary<string, List<HubMessage>> Messages = new Dictionary<string, List<HubMessage>>
        {
            ["clan"] = new List<HubMessage>
            {
                new HubMessage { Who = "Bramblewick", Time = "6:02 PM", Text = "Anyone up for a 3-stack? Need one more." },
                new HubMessage { System = true, Text = "Tallow joined Hearthguard." },
                new HubMessage { Who = "Sorrel", Time = "6:09 PM", Text = "Archers every 5th spawn felt way better than healers last night." },
                new HubMessage { Who = "Millie Grist", Time = "6:11 PM", Text = "Healers are fine if you body block the north gate." },
                new HubMessage { Who = "Bramblewick", Time = "6:14 PM", Text = "Party is up, it is me and Millie. Who wants the last tower?" }
            },
            ["party"] = new List<HubMessage>
            {
                new HubMessage { System = true, Text = "Invite friends to your party so your towers spawn side by side." }
            },
            ["mudlark"] = new List<HubMessage>
            {
                new HubMessage { Who = "Mudlark", Time = "5:48 PM", Text = "gg earlier. that berserk timer flipped the whole game" },
                new HubMessage { Who = PlayerName, Mine = true, Time = "5:50 PM", Text = "Right? We were losing until minute 15." },
                new HubMessage { Who = "Mudlark", Time = "5:51 PM", Text = "Queue again after this one?" }
            }
        };
        public readonly Dictionary<string, string> Drafts = new Dictionary<string, string>();
        public string ModeId = "practice", MapId = "large";
        public int Graphics = 2;
        public float Master = 1, Music = 0.7f, Effects = 0.85f;

        // ---- Derived --------------------------------------------------------------------------

        public HubMode Mode => Modes.First(m => m.Id == ModeId);
        public HubMap Map => Maps.First(m => m.Id == MapId);
        public bool InOtherParty => PartyLeader != null;
        public int PartyCount => InOtherParty ? PartySize : Party.Count + 1;
        public int OnlineCount => Friends.Count(f => f.Status != Presence.Offline);
        public HubFriend Find(string id) => Friends.FirstOrDefault(f => f.Id == id);
        public bool InParty(HubFriend f) => InOtherParty ? f.Id == Leader_Bramblewick || f.Id == "millie" : Party.Contains(f.Id);
        public bool CanInvite(HubFriend f) => f.Status == Presence.Online && !InParty(f) && !InOtherParty && Party.Count < PartySize - 1;
        public bool CanMessage(HubFriend f) => f.Status != Presence.Offline;
        // Online modes stay locked until authenticated matches exist (see PROTOCOL.md); practice runs locally.
        public bool PlayLocked => !InOtherParty && Mode.Online;
        public string PlayLabel => InOtherParty ? (Ready ? "READY  ✓" : "READY") : PlayLocked ? "ONLINE LOCKED" : Mode.Play;
        public string PartyTitle => InOtherParty ? "BRAMBLEWICK’S PARTY" : "YOUR PARTY";
        public string PartySubtitle => InOtherParty ? "Leader picks the map and queues" : PartyCount + " of " + PartySize + " · towers spawn together";
        public IEnumerable<HubFriend> Visible
        {
            get
            {
                string q = Search.Trim().ToLowerInvariant();
                return Friends.Where(f => q.Length == 0 || f.Name.ToLowerInvariant().Contains(q));
            }
        }

        public List<HubMessage> History(string tab)
        {
            if (Messages.TryGetValue(tab, out var list)) return list;
            var friend = Find(tab);
            list = new List<HubMessage> { new HubMessage { System = true, Text = "This is the start of your chat with " + (friend != null ? friend.Name : "your friend") + "." } };
            Messages[tab] = list;
            return list;
        }

        public (string title, string subtitle, string placeholder, bool isClan) Channel
        {
            get
            {
                if (ChatTab == "clan") return (ClanName, "6 of 12 members online", "Message " + ClanName, true);
                if (ChatTab == "party") return (InOtherParty ? "Bramblewick’s party" : "Party", PartyCount + " of " + PartySize + " · towers spawn together", "Message your party", false);
                var friend = Find(ChatTab);
                return (friend != null ? friend.Name : ChatTab, friend != null ? friend.Detail : "", "Message " + (friend != null ? friend.Name : "friend"), false);
            }
        }

        static string Now => DateTime.Now.ToString("h:mm tt");
        void Notify(Section section) => Changed?.Invoke(section);

        // ---- Friends --------------------------------------------------------------------------

        public void ToggleAdd() { AddOpen = !AddOpen; Note = null; Notify(Section.Friends); }
        public void SetSearch(string value) { Search = value ?? ""; Notify(Section.Friends); }
        public void SetAddName(string value) => AddName = value ?? "";

        public void SendFriendRequest()
        {
            string name = AddName.Trim();
            if (name.Length == 0) return;
            AddOpen = false; AddName = "";
            Note = "Friend request sent to " + name + ".";
            Notify(Section.Friends);
        }

        public void Accept(HubFriend request)
        {
            if (!Requests.Remove(request)) return;
            Friends.Add(request);
            Notify(Section.Friends);
        }

        public void Decline(HubFriend request) { if (Requests.Remove(request)) Notify(Section.Friends); }

        public void ToggleExpanded(HubFriend f)
        {
            bool open = Expanded == f.Id || Confirming == f.Id;
            Expanded = open ? null : f.Id;
            Confirming = null;
            Notify(Section.Friends);
        }
        public void AskRemove(HubFriend f) { Confirming = f.Id; Notify(Section.Friends); }
        public void CancelRemove(HubFriend f) { Confirming = null; Expanded = f.Id; Notify(Section.Friends); }

        public void Remove(HubFriend f)
        {
            Friends.Remove(f);
            Party.Remove(f.Id);
            bool tabGone = DirectTabs.Remove(f.Id);
            if (tabGone && ChatTab == f.Id) ChatTab = "clan";
            Confirming = Expanded = null;
            Note = f.Name + " was removed from your friends.";
            Notify(Section.Friends | Section.Play | (tabGone ? Section.Chat : 0));
        }

        public void InviteToClan(HubFriend f) { ClanInvitesSent.Add(f.Id); Notify(Section.Friends); }

        // ---- Party ----------------------------------------------------------------------------

        public void InviteToParty(HubFriend f)
        {
            if (!CanInvite(f)) return;
            Party.Add(f.Id);
            History("party").Add(new HubMessage { System = true, Text = f.Name + " joined your party." });
            Notify(Section.Friends | Section.Play | Section.Chat);
        }

        public void RemoveFromParty(string id)
        {
            if (!Party.Remove(id)) return;
            var friend = Find(id);
            History("party").Add(new HubMessage { System = true, Text = (friend != null ? friend.Name : id) + " left your party." });
            Notify(Section.Friends | Section.Play | Section.Chat);
        }

        public void AcceptInvite()
        {
            PartyLeader = Leader_Bramblewick;
            Party.Clear();
            InviteVisible = false;
            Ready = false;
            Messages["party"] = new List<HubMessage>
            {
                new HubMessage { System = true, Text = "You joined Bramblewick’s party." },
                new HubMessage { Who = "Bramblewick", Time = Now, Text = "Nice, full squad. Large FFA, I will queue us." }
            };
            ChatTab = "party"; ModeId = "quick"; MapId = "large";
            Notify(Section.All);
        }

        public void DeclineInvite() { InviteVisible = false; Notify(Section.Invite); }

        public void LeaveParty()
        {
            PartyLeader = null; Ready = false;
            Messages["party"] = new List<HubMessage> { new HubMessage { System = true, Text = "You left Bramblewick’s party." } };
            Notify(Section.All);
        }

        public void ToggleReady()
        {
            Ready = !Ready;
            History("party").Add(new HubMessage { System = true, Text = Ready ? "You are ready. Waiting for Bramblewick to queue." : "You are no longer ready." });
            Notify(Section.Play | Section.Chat);
        }

        // ---- Chat -----------------------------------------------------------------------------

        public void OpenDirect(string id)
        {
            if (!DirectTabs.Contains(id)) DirectTabs.Add(id);
            ChatTab = id;
            Notify(Section.Chat);
        }

        public void CloseDirect(string id)
        {
            DirectTabs.Remove(id);
            if (ChatTab == id) ChatTab = "clan";
            Notify(Section.Chat);
        }

        public void SelectTab(string id) { ChatTab = id; Notify(Section.Chat); }

        public bool Send(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            History(ChatTab).Add(new HubMessage { Who = PlayerName, Mine = true, Time = Now, Text = text });
            Drafts[ChatTab] = "";
            Notify(Section.Chat);
            return true;
        }

        // ---- Play -----------------------------------------------------------------------------

        public void SetMode(string id) { ModeId = id; Notify(Section.Play); }
        public void SetMap(string id) { MapId = id; Notify(Section.Play); }

        public void SetMapForPlayers(int players)
        {
            var match = Maps.FirstOrDefault(m => m.Players == players);
            if (match != null) MapId = match.Id;
        }

        public void SetGraphics(int preset) { Graphics = preset; Notify(Section.Settings); }
    }
}
