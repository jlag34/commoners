using System;
using System.Collections.Generic;
using System.Linq;

namespace SwarmPrototype
{
    // Local, disposable demo data. No accounts, messages or membership leave this process.
    public sealed class SocialDemoState
    {
        public sealed class Message
        {
            public string Thread, Author, Body;
            public bool Mine;
        }
        public sealed class Clan
        {
            public string Id, Name, Tag, Description;
            public int Members;
            public bool Created;
            public string Thread => "clan:" + Id;
        }

        public readonly List<string> Friends = new List<string> { "Mira", "Rowan" };
        public readonly List<string> Requests = new List<string> { "Vale", "Sable" };
        public readonly List<string> Outgoing = new List<string>();
        public readonly List<Clan> Clans = new List<Clan>
        {
            new Clan { Id = "oak", Name = "Oakguard", Tag = "OAK", Description = "Hold the line. Look after your people.", Members = 18 },
            new Clan { Id = "ember", Name = "Ember Company", Tag = "EMBR", Description = "Small armies. Big comebacks.", Members = 11 },
            new Clan { Id = "night", Name = "Nightwatch", Tag = "NITE", Description = "One more match before the watch ends.", Members = 23 }
        };
        readonly List<Message> messages = new List<Message>();
        readonly Dictionary<string, int> unread = new Dictionary<string, int>();
        public Clan CurrentClan { get; private set; }
        public string PlayerName { get; private set; }
        public event Action Changed;
        public int Unread => unread.Values.Sum();
        public int Attention => Requests.Count + Unread;
        public static string DirectThread(string friend) => "dm:" + friend.ToLowerInvariant();
        public int UnreadFor(string thread) => unread.TryGetValue(thread, out int count) ? count : 0;
        public Message[] History(string thread) => messages.Where(m => m.Thread == thread).ToArray();
        public bool CanChat(string thread) => Friends.Any(f => DirectThread(f) == thread) || (CurrentClan != null && CurrentClan.Thread == thread);
        public string ThreadName(string thread) => CurrentClan != null && CurrentClan.Thread == thread ? "[" + CurrentClan.Tag + "] " + CurrentClan.Name
            : Friends.FirstOrDefault(f => DirectThread(f) == thread) ?? "Messages";

        public SocialDemoState(string playerName = "Commander")
        {
            PlayerName = playerName;
            Add(DirectThread("Mira"), "Mira", "Ready for a quick match?", false, true);
            Add(DirectThread("Mira"), PlayerName, "One minute. Checking my army!", true, false);
            Add(DirectThread("Mira"), "Mira", "Perfect. I'll take the north entrance this time.", false, true);
            Add(DirectThread("Rowan"), "Rowan", "That last defense was close. Nice hold!", false, true);
        }

        public void RenamePlayer(string name)
        {
            PlayerName = name;
            foreach (var message in messages.Where(m => m.Mine)) message.Author = name;
            Notify();
        }

        public string RequestFriend(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 2 || name.Length > 24 || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-'))
                return "Use 2–24 letters, numbers, spaces, underscores or hyphens.";
            if (Same(name, PlayerName)) return "That's you. Try another player name.";
            if (Friends.Any(f => Same(f, name))) return name + " is already on your friends list.";
            if (Outgoing.Any(f => Same(f, name))) return "You already sent that request.";
            if (Requests.Any(f => Same(f, name))) return "That player sent you a request. Accept it below.";
            Outgoing.Add(name); Notify(); return null;
        }

        public bool Accept(string name)
        {
            if (!Requests.Remove(name)) return false;
            AddFriend(name); return true;
        }
        public bool Decline(string name) { bool removed = Requests.Remove(name); if (removed) Notify(); return removed; }
        public bool AcceptOutgoing(string name)
        {
            if (!Outgoing.Remove(name)) return false;
            AddFriend(name); return true;
        }
        void AddFriend(string name)
        {
            if (!Friends.Any(f => Same(f, name))) Friends.Add(name);
            Add(DirectThread(name), name, "Hey " + PlayerName + "! Good to have you on my friends list.", false, true);
            Notify();
        }

        public string Send(string thread, string body)
        {
            body = (body ?? "").Trim();
            if (!CanChat(thread)) return "Choose a friend or join a clan first.";
            if (body.Length == 0) return "Write a message first.";
            if (body.Length > 280) return "Keep messages under 280 characters.";
            Add(thread, PlayerName, body, true, false); Notify(); return null;
        }
        public bool Receive(string thread, string author, string body, bool isReading)
        {
            if (!CanChat(thread)) return false;
            Add(thread, author, body, false, !isReading); Notify(); return true;
        }
        public void MarkRead(string thread)
        {
            if (UnreadFor(thread) == 0) return;
            unread.Remove(thread); Notify();
        }
        void Add(string thread, string author, string body, bool mine, bool markUnread)
        {
            messages.Add(new Message { Thread = thread, Author = author, Body = body, Mine = mine });
            var history = messages.Where(m => m.Thread == thread).ToArray();
            if (history.Length > 40) messages.Remove(history[0]);
            if (markUnread) unread[thread] = UnreadFor(thread) + 1;
        }

        public string Join(string id)
        {
            if (CurrentClan != null) return "Leave your current clan before joining another.";
            var clan = Clans.FirstOrDefault(c => c.Id == id);
            if (clan == null) return "That clan isn't available.";
            CurrentClan = clan;
            Add(clan.Thread, "Mira", "Welcome to " + clan.Name + ", " + PlayerName + ". Make yourself at home!", false, true);
            Notify(); return null;
        }

        public string Create(string name, string tag)
        {
            name = (name ?? "").Trim(); tag = (tag ?? "").Trim().ToUpperInvariant();
            if (CurrentClan != null) return "Leave your current clan before creating another.";
            if (name.Length < 3 || name.Length > 24 || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_'))
                return "Clan names need 3–24 letters, numbers, spaces, hyphens or underscores.";
            if (tag.Length < 2 || tag.Length > 4 || tag.Any(c => c < 'A' || c > 'Z')) return "Clan tags need 2–4 letters (A–Z).";
            if (Clans.Any(c => Same(c.Name, name) || Same(c.Tag, tag))) return "That clan name or tag is already taken in this demo.";
            CurrentClan = new Clan { Id = "local-" + Guid.NewGuid().ToString("N"), Name = name, Tag = tag, Description = "A new home for your people.", Members = 0, Created = true };
            Clans.Add(CurrentClan);
            Add(CurrentClan.Thread, "Clan notice", "You created " + name + ". This local demo clan starts with you as its leader.", false, false);
            Notify(); return null;
        }
        public void Leave()
        {
            if (CurrentClan == null) return;
            unread.Remove(CurrentClan.Thread);
            if (CurrentClan.Created) Clans.Remove(CurrentClan);
            CurrentClan = null; Notify();
        }
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        void Notify() => Changed?.Invoke();
    }
}
