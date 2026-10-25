using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Boot -> splash -> home hub -> offline match. Screens come from SplashScreen / HomeHub (handoff UI);
    // this host owns the canvas, persisted settings, the settings modal and the launch into Bootstrap.
    public class MainMenu : MonoBehaviour
    {
        const string PlayerCountKey = "SwarmPrototype.Players", QualityKey = "Commoners.Graphics";
        const string VolumeKey = "Commoners.Volume", MusicKey = "Commoners.Music", EffectsKey = "Commoners.Effects";
        static bool enteredHome;
        static HubState state;                       // survives home <-> match within one play session
        RectTransform safe, splash, home, modal;
        HomeHub hub;
        Button startButton;
        CanvasGroup homeInteraction;
        SocialOverlay social;
        bool launching;

        public HubState State => state;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetNavigation() { enteredHome = false; state = null; }

        void Awake()
        {
            if (state == null)
            {
                state = new HubState();
                int players = PlayerPrefs.GetInt(PlayerCountKey, 4);
                if (System.Array.IndexOf(MapLayout.Sizes, players) < 0) players = 4;
                state.SetMapForPlayers(players);
                state.Graphics = PlayerPrefs.GetInt(QualityKey, PresetFromQuality());
                state.Master = PlayerPrefs.GetFloat(VolumeKey, 1);
                state.Music = PlayerPrefs.GetFloat(MusicKey, 0.7f);
                state.Effects = PlayerPrefs.GetFloat(EffectsKey, 0.85f);
            }
            if (PlayerPrefs.HasKey(QualityKey)) ApplyQuality(state.Graphics);
            AudioListener.volume = state.Master;

            // The social overlay owns the EventSystem and the in-match panel; it stays hidden on the home screens.
            social = SocialOverlay.Ensure();
            var canvas = new GameObject("Home Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            HubWidgets.Background(HubWidgets.Fill(canvas.transform, "Base"), HubTheme.Base);
            safe = HubWidgets.Fill(canvas.transform, "Safe area");
            safe.gameObject.AddComponent<MenuSafeArea>();

            home = HubWidgets.Fill(safe, "Character home");
            homeInteraction = home.gameObject.AddComponent<CanvasGroup>();
            hub = home.gameObject.AddComponent<HomeHub>();
            hub.Build(home, state, ShowSettings, ShowProfile, ShowRanked, Play);

            splash = HubWidgets.Fill(safe, "Start screen");
            startButton = SplashScreen.Build(splash, EnterHome);

            splash.gameObject.SetActive(!enteredHome);
            home.gameObject.SetActive(enteredHome);
            social.SetContext(true, false);
        }

        void Start() => Focus(enteredHome ? hub.PlayButton : startButton);

        public void EnterHome()
        {
            enteredHome = true;
            splash.gameObject.SetActive(false);
            home.gameObject.SetActive(true);
            Focus(hub.PlayButton);
        }

        public void Play()
        {
            // Online modes cannot start local bot matches; the hub shows why (ShowRanked).
            if (launching || state.PlayLocked || state.InOtherParty) return;
            launching = true;
            hub.PlayButton.interactable = false;
            hub.PlayButton.GetComponentInChildren<TMP_Text>().text = "PREPARING...";
            PlayerPrefs.SetInt(PlayerCountKey, state.Map.Players);
            PlayerPrefs.Save();
            StartCoroutine(Launch());
        }

        IEnumerator Launch()
        {
            yield return null;
            // Remove preview cameras/lights before the match sets up its lighting.
            home.gameObject.SetActive(false);
            social.SetContext(false);
            Bootstrap.Play(state.Map.Players);
        }

        public void ShowSettings()
        {
            OpenModal();
            modal = SettingsModal.Build(safe, state, preset =>
            {
                state.SetGraphics(preset);
                ApplyQuality(preset);
                PlayerPrefs.SetInt(QualityKey, preset);
            }, (channel, value) =>
            {
                switch (channel)
                {
                    case "master": state.Master = value; AudioListener.volume = value; PlayerPrefs.SetFloat(VolumeKey, value); break;
                    case "music": state.Music = value; PlayerPrefs.SetFloat(MusicKey, value); break;
                    default: state.Effects = value; PlayerPrefs.SetFloat(EffectsKey, value); break;
                }
            }, ShowProfile, CloseModal);
        }

        public void ShowProfile() { OpenModal(); modal = SettingsModal.BuildProfile(safe, CloseModal); }
        public void ShowRanked() { OpenModal(); modal = SettingsModal.BuildRanked(safe, CloseModal); }

        void OpenModal()
        {
            CloseModal();
            homeInteraction.interactable = false;
            homeInteraction.blocksRaycasts = false;
        }

        void CloseModal()
        {
            if (modal != null) { modal.gameObject.SetActive(false); Destroy(modal.gameObject); }
            modal = null;
            if (homeInteraction != null) { homeInteraction.interactable = true; homeInteraction.blocksRaycasts = true; }
            PlayerPrefs.Save();
            Focus(hub != null ? hub.PlayButton : null);
        }

        static int PresetFromQuality()
        {
            int last = Mathf.Max(1, QualitySettings.names.Length - 1);
            return Mathf.RoundToInt(QualitySettings.GetQualityLevel() * 2f / last);
        }

        static void ApplyQuality(int preset)
        {
            int last = QualitySettings.names.Length - 1;
            QualitySettings.SetQualityLevel(preset == 0 ? 0 : preset == 1 ? last / 2 : last, true);
        }

        static void Focus(Selectable control)
        {
            if (control != null && control.gameObject.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(control.gameObject);
        }

        void Update()
        {
            if (SocialOverlay.IsOpen || SocialOverlay.IsTyping || !InputShim.CancelPressed) return;
            if (modal != null) CloseModal();
        }
    }
}
