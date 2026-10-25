using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SwarmPrototype.HubWidgets;

namespace SwarmPrototype
{
    // Settings dialog from the hub handoff: graphics Low / Medium / High, three volumes, the M hint, DONE.
    public static class SettingsModal
    {
        public static RectTransform Build(RectTransform parent, HubState state, Action<int> applyGraphics, Action<string, float> applyVolume, Action editName, Action close)
        {
            var dialog = Dialog(parent, "Settings modal", "Settings", "Make yourself at home.", close, out var overlay);

            // Profile (local username; see LocalPlayerProfile)
            var profile = Column(dialog, "Profile", 12);
            HubTheme.Label(profile, "PROFILE", HubTheme.Secondary);
            var profileRow = Row(profile, "Row", 16, null, TextAnchor.MiddleLeft);
            var who = Column(profileRow, "Who", 2, null, TextAnchor.MiddleLeft);
            Size(who, flexWidth: 1, minWidth: 0);
            HubTheme.Text(who, "Name", HubState.PlayerName, 16, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineLeft, false);
            HubTheme.Text(who, "Detail", "Unranked · saved on this device", 12, HubTheme.Tertiary, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            Button(profileRow, "Edit username", "EDIT USERNAME", ButtonStyle.Ghost, () => editName(), new Spec { Height = 44, PaddingX = 16, Radius = 9, FontSize = 12, Tracking = 0.14f });

            // Graphics
            var graphics = Column(dialog, "Graphics", 12);
            HubTheme.Label(graphics, "GRAPHICS QUALITY", HubTheme.Secondary);
            var track = Row(graphics, "Quality", 4, Pad(4, 4), TextAnchor.MiddleLeft);
            Background(track, HubTheme.TrackSoft, 10);
            string[] presets = { "LOW", "MEDIUM", "HIGH" };
            for (int i = 0; i < presets.Length; i++)
            {
                int index = i;
                bool selected = state.Graphics == i;
                var button = Button(track, "Quality " + presets[i], presets[i], selected ? ButtonStyle.SegmentActive : ButtonStyle.Segment,
                    () => { applyGraphics(index); Rebuild(parent, state, applyGraphics, applyVolume, editName, close); },
                    new Spec { Height = 48, Radius = 7, FontSize = 13, Tracking = 0.14f, FlexWidth = 1 });
                if (selected)
                {
                    var tint = button.GetComponent<HubHover>();
                    tint.Normal = tint.Hover = tint.Pressed = HubTheme.Gold;
                    tint.NormalText = tint.HoverText = HubTheme.OnGold;
                    tint.Fill.color = HubTheme.Gold;
                    tint.Label.color = HubTheme.OnGold;
                }
                else
                {
                    var tint = button.GetComponent<HubHover>();
                    tint.NormalText = HubTheme.Pale; tint.Label.color = HubTheme.Pale;
                }
            }

            // Audio
            var audio = Column(dialog, "Audio", 16);
            HubTheme.Label(audio, "AUDIO", HubTheme.Secondary);
            Volume(audio, "Master", state.Master, v => applyVolume("master", v));
            Volume(audio, "Music", state.Music, v => applyVolume("music", v));
            Volume(audio, "Effects", state.Effects, v => applyVolume("effects", v));

            // Footer
            var footer = Column(dialog, "Footer", 18);
            Rule(footer, "Divider", HubTheme.PanelBorder);
            var footerRow = Row(footer, "Footer row", 20, null, TextAnchor.LowerLeft);
            var hint = HubTheme.Text(footerRow, "Hint", "Drag your unit to rotate it.\nPress M during a match to return home.", 13, HubTheme.Secondary);
            hint.lineSpacing = 30;
            Size(hint, flexWidth: 1);
            Button(footerRow, "Settings done", "DONE", ButtonStyle.Gold, () => close(),
                new Spec { Height = 56, PaddingX = 44, Radius = 10, FontSize = 17, Tracking = 0.18f });
            return overlay;
        }

        static void Rebuild(RectTransform parent, HubState state, Action<int> applyGraphics, Action<string, float> applyVolume, Action editName, Action close)
        {
            var old = parent.Find("Settings modal");
            if (old != null) { old.gameObject.SetActive(false); UnityEngine.Object.Destroy(old.gameObject); }
            Build(parent, state, applyGraphics, applyVolume, editName, close);
        }

        // Shared dimmed overlay + 560px dialog with title, subtitle and close button. Returns the dialog column.
        public static RectTransform Dialog(RectTransform parent, string name, string title, string subtitle, Action close, out RectTransform overlay)
        {
            overlay = Fill(parent, name);
            Background(overlay, HubTheme.Rgba(5, 13, 16, 0.72f), 0, true);
            var dialog = Centered(overlay, title, 560, 100);
            Shadow(dialog, 16, 40, HubTheme.Rgba(0, 0, 0, 0.5f), -14);
            Background(dialog, HubTheme.ModalSurface, 16, true);
            Border(dialog, HubTheme.PanelBorderBright, 16);
            var column = dialog.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = Pad(36, 32, 36, 32);
            column.spacing = 26;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            FitContent(dialog, false, true);
            var header = Row(dialog, "Header", 16, null, TextAnchor.UpperLeft);
            var titles = Column(header, "Titles", 6);
            Size(titles, flexWidth: 1);
            HubTheme.Text(titles, "Title", title, 30, HubTheme.Cream, HubTheme.Face.BodyBold, 0.02f);
            HubTheme.Text(titles, "Subtitle", subtitle, 14, HubTheme.Secondary);
            IconButton(header, "Close modal", "close", () => close(), 44, HubTheme.Pale);
            return dialog;
        }

        // Username editor ("Your profile"): validates through LocalPlayerProfile, saves on this device only.
        public static RectTransform BuildProfile(RectTransform parent, Action close)
        {
            var dialog = Dialog(parent, "Your profile modal", "Your profile", "Choose the name your army knows you by.", close, out var overlay);
            var field = Column(dialog, "Username", 10);
            HubTheme.Label(field, "USERNAME", HubTheme.Secondary);
            TMP_Text feedback = null;
            var input = Input(field, "Username draft", "Your username", LocalPlayerProfile.DisplayName, _ => { feedback.text = Hint; feedback.color = HubTheme.Tertiary; }, null,
                48, 10, null, null, null, LocalPlayerProfile.MaxNameLength);
            input.richText = false;
            feedback = HubTheme.Text(field, "Username feedback", Hint, 12, HubTheme.Tertiary);
            Rule(dialog, "Divider", HubTheme.PanelBorder);
            var ranked = Column(dialog, "Ranked", 12);
            HubTheme.Label(ranked, "RANKED  /  UNRANKED", HubTheme.Gold);
            HubTheme.Text(ranked, "Note", "Your username is saved on this device.\nOnline accounts and ranked play are coming later.", 14, HubTheme.Secondary).lineSpacing = 20;
            var footer = Row(dialog, "Footer", 12, null, TextAnchor.MiddleRight);
            Spacer(footer);
            Button(footer, "Cancel username", "CANCEL", ButtonStyle.Ghost, () => close(), new Spec { Height = 56, PaddingX = 24, Radius = 10, FontSize = 14, Tracking = 0.16f });
            Button(footer, "Save username", "SAVE NAME", ButtonStyle.Gold, () =>
            {
                string error = LocalPlayerProfile.Rename(input.text);
                if (error == null) { close(); return; }
                feedback.text = error;
                feedback.color = HubTheme.DangerText;
                input.ActivateInputField();
            }, new Spec { Height = 56, PaddingX = 36, Radius = 10, FontSize = 17, Tracking = 0.18f });
            input.onSubmit.AddListener(_ => { if (LocalPlayerProfile.Rename(input.text) == null) close(); else { feedback.text = LocalPlayerProfile.Validate(input.text); feedback.color = HubTheme.DangerText; } });
            input.Select(); input.ActivateInputField();
            return overlay;
        }
        const string Hint = "3–20 characters. Letters, numbers, spaces, _ or -.";

        // Why online modes are locked ("Ranked play").
        public static RectTransform BuildRanked(RectTransform parent, Action close)
        {
            var dialog = Dialog(parent, "Ranked play modal", "Ranked play", "UNRANKED  ·  Online play coming later", close, out var overlay);
            var how = Column(dialog, "How", 14);
            HubTheme.Label(how, "HOW YOUR RATING WILL WORK", HubTheme.Gold);
            HubTheme.Text(how, "Line 1", "Find opponents near your skill level.", 16, HubTheme.Cream);
            HubTheme.Text(how, "Line 2", "Your final placement and opponents' ratings determine your Elo change.", 16, HubTheme.Cream);
            HubTheme.Text(how, "Line 3", "Practice and bot matches do not affect Elo.", 16, HubTheme.Cream);
            HubTheme.Text(dialog, "Unlock", "Ranked unlocks when online matches and verified server results are available.", 13, HubTheme.Secondary);
            var footer = Row(dialog, "Footer", 12, null, TextAnchor.MiddleRight);
            Spacer(footer);
            Button(footer, "Ranked done", "GOT IT", ButtonStyle.Gold, () => close(), new Spec { Height = 56, PaddingX = 44, Radius = 10, FontSize = 17, Tracking = 0.18f });
            return overlay;
        }

        static void Volume(Transform parent, string label, float value, Action<float> changed)
        {
            var row = Row(parent, label + " volume", 16, null, TextAnchor.MiddleLeft);
            Size(HubTheme.Text(row, "Label", label, 14, HubTheme.Soft), 120, 44, minWidth: 120);
            var readout = HubTheme.Text(row, "Value", Mathf.RoundToInt(value * 100).ToString(), 13, HubTheme.Cream, HubTheme.Face.BodyBold, 0, TextAlignmentOptions.MidlineRight, false);
            var slider = Range(row, label + " slider", value, v => { readout.text = Mathf.RoundToInt(v * 100).ToString(); changed(v); });
            readout.transform.SetAsLastSibling();
            Size(readout, 44, 44, minWidth: 44);
        }
    }
}
