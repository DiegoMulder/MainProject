using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SurvivalFP.UIKit;
namespace SurvivalFP
{
    // Every game screen, built once on UI Toolkit from the shared UIKit / GameUI.uss vocabulary.
    // Screens only present state: session, round, life and inventory systems remain the sources of truth.
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameUI : MonoBehaviour
    {
        public Camera menuCamera;
        public PanelSettings panelSettings;
        const string KeyName = "SurvivalFP.Name";
        VisualElement root, menu, lobby, loading, end, pause, optionsScreen, hud;
        bool options, paused, optionsFromPause;
        // Main menu
        TextField nameField, codeField; Button hostButton, joinButton, resetButton; Label menuStatus;
        // Lobby
        Label lobbyCode, playerCount, mapValue, difficultyValue, difficultyDetail, hostHint, lobbyStatus, waitingLabel;
        VisualElement playerList; Button mapPrev, mapNext, difficultyPrev, difficultyNext, startButton, copyButton;
        string rosterSignature; int shownDifficulty = -1;
        // Loading / end / pause
        Label loadingTitle, loadingDetail, endTitle, endBody, endStats, endHint; Button backToLobby;
        // Options
        Button[] tabButtons; VisualElement[] tabPages; int optionsTab;
        Slider master, sfx, voice, sensitivity, fov, smoothingStrength; Toggle smoothingToggle, psxToggle, muteToggle; Label voiceStatus;
        // HUD
        VisualElement crosshair, prompt, objective, slotsRow, stamina, staminaFill, hints, downed, bleedFill, spectator, vignette;
        Label promptText, objectiveCount, objectiveHint, debugLine, bleedTime, spectatorLabel, spectatorName;
        readonly VisualElement[] slots = new VisualElement[3]; readonly Label[] slotNames = new Label[3], slotTags = new Label[3];
        readonly PickupItem[] shownItems = new PickupItem[3];
        int shownDeposited = -1, shownRequired = -1, shownSeconds = -1, shownSeed; ExitDoor exit; float nextExitSearch;
        NetworkPlayer cachedPlayer; PlayerStamina playerStamina; DownedVignette playerVignette; SpectatorController playerSpectator;

        void Awake()
        {
            var document = GetComponent<UIDocument>();
            if (!document.panelSettings) document.panelSettings = panelSettings;
            if (!document.panelSettings) Debug.LogError("GameUI needs a PanelSettings asset (Assets/Game/UI/GamePanelSettings.asset).", this);
        }
        void Start()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.AddToClassList("ui-root");
            BuildHud(); BuildMenu(); BuildLobby(); BuildLoading(); BuildEnd(); BuildPause(); BuildOptions(); BuildOverlays();
        }
        void OnDisable() { if (options) LocalSettings.Save(); }

        // ---------------------------------------------------------------- building
        // Main menu: the living Grand Hall fills the screen (MenuBackdrop); the menu is a column on the
        // left, over a shade that keeps text readable without boxing the scene in.
        void BuildMenu()
        {
            menu = NewScreen(root, "Main Menu", "screen--backdrop");
            var side = MenuColumn(menu);
            // The title surfaces slowly out of the dark, as the hall does.
            var titleBlock = NewBox("title-block", "title-block--dark").AddTo(side);
            titleBlock.schedule.Execute(() => titleBlock.RemoveFromClassList("title-block--dark")).StartingIn(500);
            NewLabel("CO-OP SURVIVAL HORROR", "menu-kicker").AddTo(titleBlock);
            menuTitle = NewLabel("MOURN", "menu-title").AddTo(titleBlock);
            NewBox("menu-rule").AddTo(titleBlock);
            NewLabel("Find the objectives. Unlock the exit. Stay quiet.", "menu-subtitle").AddTo(titleBlock);
            NewBox("spacer-l").AddTo(side);
            nameField = NewField(side, "YOUR NAME", PlayerPrefs.GetString(KeyName, "Survivor"), 20, "field--menu");
            NewBox("spacer-m").AddTo(side);
            hostButton = MenuItem(side, "HOST GAME", Host, true);
            var joinRow = NewBox("menu-join").AddTo(side);
            joinButton = MenuItem(joinRow, "JOIN", Join, false);
            codeField = new TextField { maxLength = 12 }.With("field", "field--code", "field--inline").AddTo(joinRow);
            codeField.textEdition.placeholder = "LOBBY CODE"; codeField.textEdition.hidePlaceholderOnFocus = true;
            codeField.RegisterValueChangedCallback(e => codeField.SetValueWithoutNotify(e.newValue.ToUpperInvariant().Replace(" ", "")));
            codeField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Join(); });
            MenuItem(side, "OPTIONS", () => OpenOptions(false), false);
            MenuItem(side, "QUIT", Application.Quit, false);
            menuStatus = NewLabel("", "status", "status--left").AddTo(side);
            resetButton = NewLink(side, "RESET CONNECTION", () => GameSession.Instance.ReturnToMenu()).With("link--danger");
            NewLabel("Private lobby  •  Unity Relay", "menu-footer").AddTo(menu);
            // A lantern-like stutter in the title, rarely: restraint, not a glitch effect.
            menuTitle.schedule.Execute(() =>
            {
                if (Random.value > .08f) return;
                menuTitle.AddToClassList("menu-title--flicker");
                menuTitle.schedule.Execute(() => menuTitle.RemoveFromClassList("menu-title--flicker")).StartingIn(Random.Range(50, 140));
            }).Every(700);
        }
        Label menuTitle;
        VisualElement MenuColumn(VisualElement screen)
        {
            var shade = NewBox("menu-shade").AddTo(screen);
            shade.style.backgroundImage = SideShade;
            shade.pickingMode = PickingMode.Ignore;
            var side = NewBox("menu-side").AddTo(screen);
            Drift(side, 0f);
            return side;
        }
        // Darkens the right of the screen so text there reads over the hall without any panel behind it.
        VisualElement RightShade(VisualElement screen)
        {
            var shade = NewBox("menu-shade", "menu-shade--right").AddTo(screen);
            shade.style.backgroundImage = SideShade; shade.style.scale = new Scale(new Vector3(-1, 1, 1));
            shade.pickingMode = PickingMode.Ignore;
            return shade;
        }
        // Very slow, very small float, as if the words were hung in the air of the hall.
        static void Drift(VisualElement element, float phase)
        {
            element.schedule.Execute(() =>
            {
                float t = Time.unscaledTime * .23f + phase;
                element.style.translate = new Translate(Mathf.Sin(t) * 2.2f, Mathf.Sin(t * .71f + 1.3f) * 1.6f);
            }).Every(40);
        }
        static Button MenuItem(VisualElement parent, string text, System.Action click, bool primary)
            => NewMenuItem(parent, text, click, primary ? "menu-item--primary" : null);

        // Generated once: a left-to-right shade and a fine grain tile, so the UI needs no texture assets.
        static Texture2D sideShade, grain;
        static Texture2D SideShade
        {
            get
            {
                if (sideShade) return sideShade;
                sideShade = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                for (int x = 0; x < 256; x++) { float t = x / 255f; sideShade.SetPixel(x, 0, new Color(.012f, .01f, .01f, Mathf.Lerp(.95f, 0f, t * t))); }
                sideShade.Apply(); return sideShade;
            }
        }
        static Texture2D Grain
        {
            get
            {
                if (grain) return grain;
                grain = new Texture2D(128, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
                var rng = new System.Random(7);
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float v = (float)rng.NextDouble(); grain.SetPixel(x, y, new Color(v, v, v, Mathf.Abs(v - .5f) * .016f)); }
                grain.Apply(); return grain;
            }
        }
        VisualElement grainLayer, fadeLayer;
        void BuildOverlays()
        {
            // Fine film grain over the menus, nudged a few times a second so it lives without distracting.
            grainLayer = NewBox("grain").AddTo(root);
            grainLayer.pickingMode = PickingMode.Ignore;
            grainLayer.style.backgroundImage = Grain;
            grainLayer.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            grainLayer.style.backgroundSize = new BackgroundSize(new Length(256), new Length(256));
            grainLayer.schedule.Execute(() =>
            {
                grainLayer.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Random.Range(0, 256));
                grainLayer.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Random.Range(0, 256));
            }).Every(90);
            // Opens from black when the game starts.
            fadeLayer = NewBox("fade").AddTo(root);
            fadeLayer.pickingMode = PickingMode.Ignore;
            fadeLayer.schedule.Execute(() => fadeLayer.AddToClassList("fade--out")).StartingIn(120);
        }
        void Host() { GameSession.Instance.SetPlayerName(nameField.value); GameSession.Instance.HostLobby(); }
        void Join() { if (joinButton.enabledSelf) { GameSession.Instance.SetPlayerName(nameField.value); GameSession.Instance.JoinLobby(codeField.value); } }

        void BuildLobby()
        {
            // The same room as the main menu, the same column: the party gathers in the hall, nothing is boxed.
            lobby = NewScreen(root, "Lobby", "screen--backdrop");
            RightShade(lobby);
            var side = MenuColumn(lobby);
            NewLabel("PRIVATE LOBBY  ·  UNITY RELAY", "menu-kicker").AddTo(side);
            NewLabel("GATHERING", "menu-title", "menu-title--small").AddTo(side);
            NewBox("menu-rule").AddTo(side);
            NewLabel("Share this code with the others.", "menu-subtitle").AddTo(side);
            var codeRow = NewBox("code-row").AddTo(side);
            lobbyCode = NewLabel("", "code").AddTo(codeRow);
            lobbyCode.selection.isSelectable = true;
            copyButton = NewLink(codeRow, "COPY", CopyCode);
            NewBox("spacer-l").AddTo(side);
            startButton = MenuItem(side, "START GAME", () => GameSession.Instance.StartMatch(), true);
            waitingLabel = NewLabel("Waiting for the host to begin…", "menu-note", "waiting").AddTo(side);
            MenuItem(side, "OPTIONS", () => OpenOptions(false), false);
            NewMenuItem(side, "LEAVE LOBBY", () => GameSession.Instance.ReturnToMenu(), "menu-item--danger");
            lobbyStatus = NewLabel("", "menu-note").AddTo(side);

            // Right: who is here, and where they are going.
            var right = NewBox("lobby-side").AddTo(lobby);
            Drift(right, 2.1f);
            var party = NewSection(right, "Survivors");
            playerCount = NewLabel("", "section__count").AddTo(party.Q(className: "section__head"));
            playerList = NewBox("player-list").AddTo(party);
            var expedition = NewSection(right, "Area");
            (mapPrev, mapValue, mapNext) = Stepper(expedition, "MAP", -1, true);
            (difficultyPrev, difficultyValue, difficultyNext) = Stepper(expedition, "DIFFICULTY", -1, false);
            difficultyDetail = NewLabel("", "stepper__detail").AddTo(expedition);
            hostHint = NewLabel("Only the host can choose the house and how hard it is.", "setting-row__hint").AddTo(expedition);
        }
        static string Seat(int index) => index switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", 4 => "V", 5 => "VI", 6 => "VII", 7 => "VIII", 8 => "IX", 9 => "X", 10 => "XI", _ => "XII" };
        // Map and difficulty: a selector with the value centred between arrows. Never opens a list, so it can't
        // run off-screen, and it works the same with mouse, keyboard or gamepad.
        (Button, Label, Button) Stepper(VisualElement parent, string caption, int _, bool map)
        {
            var row = NewBox("stepper").AddTo(parent);
            NewLabel(caption, "stepper__label").AddTo(row);
            var prev = NewButton("‹", () => Step(map, -1), "stepper__arrow").AddTo(row);
            var value = NewLabel("", "stepper__value").AddTo(row);
            var next = NewButton("›", () => Step(map, 1), "stepper__arrow").AddTo(row);
            return (prev, value, next);
        }
        void Step(bool map, int direction)
        {
            var roster = LobbyRoster.Instance; if (!roster) return;
            if (map) { int count = roster.maps?.Length ?? 0; if (count > 0) roster.SelectMap((roster.Map.Value + direction + count) % count); }
            else { int count = roster.difficultyConfig ? roster.difficultyConfig.profiles.Length : 0; if (count > 0) roster.SelectDifficulty((roster.Difficulty.Value + direction + count) % count); }
        }
        void CopyCode()
        {
            GUIUtility.systemCopyBuffer = GameSession.Instance.JoinCode;
            copyButton.text = "COPIED";
            copyButton.schedule.Execute(() => copyButton.text = "COPY").StartingIn(1200);
        }

        void BuildLoading()
        {
            loading = NewScreen(root, "Loading", "screen--menu");
            loadingTitle = NewLabel("", "heading").AddTo(loading);
            loadingDetail = NewLabel("", "faint").With("status").AddTo(loading);
            var dot = NewBox("loading__dot").AddTo(loading);
            dot.schedule.Execute(() => dot.ToggleInClassList("loading__dot--dim")).Every(600);
        }
        void BuildEnd()
        {
            // The world stays visible behind the verdict, as with pause.
            end = NewScreen(root, "End", "screen--pause", "screen--end");
            var side = MenuColumn(end);
            NewLabel("THE ROUND IS OVER", "menu-kicker").AddTo(side);
            endTitle = NewLabel("", "menu-title", "menu-title--small", "end-title").AddTo(side);
            NewBox("menu-rule").AddTo(side);
            endBody = NewLabel("", "menu-subtitle").AddTo(side);
            endStats = NewLabel("", "menu-note").AddTo(side);
            NewBox("spacer-l").AddTo(side);
            backToLobby = MenuItem(side, "BACK TO LOBBY", () => GameSession.Instance.BackToLobby(), true);
            endHint = NewLabel("The host can return the party to the lobby.", "menu-note").AddTo(side);
            NewMenuItem(side, "MAIN MENU", () => GameSession.Instance.ReturnToMenu(), "menu-item--danger");
        }
        // Pause: the same column language as the main menu, over the still-running game. Local only.
        Label pauseMap, pauseStatus;
        void BuildPause()
        {
            pause = NewScreen(root, "Pause", "screen--pause");
            var side = MenuColumn(pause);
            NewLabel("PAUSED", "menu-kicker").AddTo(side);
            pauseMap = NewLabel("", "menu-title", "menu-title--small").AddTo(side);
            NewBox("menu-rule").AddTo(side);
            pauseStatus = NewLabel("", "menu-subtitle").AddTo(side);
            NewBox("spacer-l").AddTo(side);
            MenuItem(side, "RESUME", () => SetPaused(false), true);
            MenuItem(side, "OPTIONS", () => OpenOptions(true), false);
            NewMenuItem(side, "LEAVE TO MAIN MENU", () => GameSession.Instance.ReturnToMenu(), "menu-item--danger");
            NewBox("spacer-l").AddTo(side);
            NewLabel("Only your screen is paused. The round, the monster and your team keep going.", "menu-note").AddTo(side);
        }
        void UpdatePause(NetworkPlayer player, RoundManager round)
        {
            pauseMap.SetText(MapName().ToUpperInvariant());
            int alive = 0, total = 0;
            foreach (var p in NetworkPlayer.Players) if (p) { total++; if (p.Alive || p.Life.Value == PlayerLife.Downed) alive++; }
            string objectives = exit ? $"{exit.Deposited.Value} of {exit.Required.Value} objectives delivered" : "";
            pauseStatus.SetText($"{objectives}{(objectives.Length > 0 ? "\n" : "")}{alive} of {total} survivors still standing");
        }
        // Options: the menu column language on the left (section navigation, Back) and a sheet on the right with
        // titled sections, one setting per row. Settings apply the moment they change and are saved on close,
        // so there is no Apply button.
        void BuildOptions()
        {
            optionsScreen = NewScreen(root, "Options", "screen--options");
            RightShade(optionsScreen);
            var side = MenuColumn(optionsScreen);
            side.AddToClassList("menu-side--options");
            NewLabel("SETTINGS", "menu-kicker").AddTo(side);
            NewLabel("OPTIONS", "menu-title", "menu-title--small").AddTo(side);
            NewBox("menu-rule").AddTo(side);
            string[] names = { "AUDIO", "CONTROLS", "VIDEO" };
            tabButtons = new Button[names.Length]; tabPages = new VisualElement[names.Length];
            var sheet = NewBox("options-content").AddTo(optionsScreen);
            Drift(sheet, 2.1f);
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                tabButtons[i] = MenuItem(side, names[i], () => SelectTab(index), false).With("menu-item--nav");
                tabPages[i] = NewBox("options-page").AddTo(sheet);
            }
            NewBox("spacer-l").AddTo(side);
            MenuItem(side, "BACK", CloseOptions, true);
            NewBox("spacer-l").AddTo(side);
            NewLabel("Changes apply immediately and are saved when you leave this screen. They only affect this computer.", "menu-note").AddTo(side);

            string Percent(float v) => Mathf.RoundToInt(v * 100) + "%";
            var levels = NewSection(tabPages[0], "Audio", "How loud the mansion, the monster and your team are.");
            master = NewSlider(levels, "Master volume", 0, 1, Percent, _ => PushSettings());
            sfx = NewSlider(levels, "Sound effects", 0, 1, Percent, _ => PushSettings(), "Footsteps, doors, the monster.");
            voice = NewSlider(levels, "Voice & radio", 0, 1, Percent, _ => PushSettings(), "Nearby players and walkie talkies.");
            var chat = NewSection(tabPages[0], "Voice chat", "Proximity voice: others hear you when they are close, or over a switched-on walkie talkie.");
            muteToggle = NewToggle(chat, "Mute my microphone", _ => PushSettings());
            var voiceRow = NewBox("row", "row--spread", "voice-status").AddTo(chat);
            voiceStatus = NewLabel("", "setting-row__hint").AddTo(voiceRow);
            NewLink(voiceRow, "RETRY VOICE", () => GameSession.Instance.GetComponent<ProximityVoice>()?.Retry());

            var mouse = NewSection(tabPages[1], "Mouse", "Movement always responds instantly; these only shape the camera.");
            sensitivity = NewSlider(mouse, "Look sensitivity", .1f, 5, v => v.ToString("0.00"), _ => PushSettings());
            var look = NewSection(tabPages[1], "Camera smoothing");
            smoothingToggle = NewToggle(look, "Smooth camera look", _ => PushSettings(), "Softens small mouse jitters.");
            smoothingStrength = NewSlider(look, "Smoothing strength", 0, 1, Percent, _ => PushSettings());
            var keys = NewSection(tabPages[1], "Keys", null);
            NewLabel("WASD move  •  Shift sprint  •  C / Ctrl crouch  •  Space jump\nE interact  •  Q drop  •  LMB use  •  F flashlight  •  1–3 / wheel select  •  Esc pause", "setting-row__hint").With("keys").AddTo(keys);

            var view = NewSection(tabPages[2], "View", "Only your screen is affected.");
            fov = NewSlider(view, "Field of view", LocalSettings.MinFov, LocalSettings.MaxFov, v => Mathf.RoundToInt(v) + "°", _ => PushSettings());
            var look2 = NewSection(tabPages[2], "Presentation");
            psxToggle = NewToggle(look2, "PSX presentation", _ => PushSettings(), "Low internal resolution, colour banding and vertex snapping.");
        }
        void SelectTab(int index)
        {
            bool changed = optionsTab != index; optionsTab = index;
            for (int i = 0; i < tabPages.Length; i++)
            {
                tabPages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                tabButtons[i].SetCurrent(i == index);
            }
            if (!changed) return;
            // A short fade-and-rise when switching sections.
            var page = tabPages[index];
            page.AddToClassList("options-page--enter");
            page.schedule.Execute(() => page.RemoveFromClassList("options-page--enter")).StartingIn(16);
        }
        void OpenOptions(bool fromPause)
        {
            options = true; optionsFromPause = fromPause;
            // Over the living backdrop from the main menu and lobby; over the running game, darker.
            optionsScreen.EnableInClassList("screen--options-game", fromPause);
            master.SetQuiet(LocalSettings.Master); sfx.SetQuiet(LocalSettings.Sfx); voice.SetQuiet(LocalSettings.Voice);
            sensitivity.SetQuiet(LocalSettings.Sensitivity * 10); fov.SetQuiet(LocalSettings.Fov); smoothingStrength.SetQuiet(LocalSettings.SmoothingStrength);
            smoothingToggle.SetQuiet(LocalSettings.Smoothing); psxToggle.SetQuiet(LocalSettings.Psx); muteToggle.SetQuiet(LocalSettings.Muted);
            smoothingStrength.SetEnabled(LocalSettings.Smoothing);
            SelectTab(optionsTab);
        }
        void CloseOptions() { options = false; LocalSettings.Save(); }
        void PushSettings()
        {
            smoothingStrength.SetEnabled(smoothingToggle.value);
            LocalSettings.Set(master.value, sfx.value, voice.value, sensitivity.value / 10, fov.value, smoothingToggle.value, smoothingStrength.value, psxToggle.value, muteToggle.value);
        }

        void BuildHud()
        {
            hud = NewBox("hud").AddTo(root);
            hud.pickingMode = PickingMode.Ignore;
            vignette = NewBox("vignette").AddTo(hud);
            vignette.style.backgroundImage = DownedVignette.Mask;
            crosshair = NewBox("crosshair").AddTo(hud);
            prompt = NewBox("prompt").AddTo(hud);
            NewLabel("E", "key").AddTo(prompt);
            promptText = NewLabel("", "prompt__text").AddTo(prompt);
            objective = NewBox("hud-top-left").AddTo(hud);
            NewLabel("OBJECTIVES", "objective__title").AddTo(objective);
            objectiveCount = NewLabel("", "objective__count").AddTo(objective);
            objectiveHint = NewLabel("", "objective__hint").AddTo(objective);
            var topRight = NewBox("hud-top-right").AddTo(hud);
            debugLine = NewLabel("", "debug").AddTo(topRight);
            slotsRow = NewBox("hud-bottom").AddTo(hud);
            for (int i = 0; i < 3; i++)
            {
                slots[i] = NewBox("slot", "slot--empty").AddTo(slotsRow);
                var header = NewBox("slot__header").AddTo(slots[i]);
                NewLabel((i + 1).ToString(), "slot__number").AddTo(header);
                slotTags[i] = NewLabel("", "slot__tag").AddTo(header);
                slotNames[i] = NewLabel("Empty", "slot__name").AddTo(slots[i]);
            }
            stamina = NewBox("stamina").AddTo(hud);
            NewLabel("STAMINA", "objective__title").AddTo(stamina);
            var track = NewBox("stamina__track").AddTo(stamina);
            staminaFill = NewBox("stamina__fill").AddTo(track);
            hints = NewBox("hints").AddTo(hud);
            NewLabel("E interact  •  Q drop  •  LMB use  •  F light", null).AddTo(hints);
            NewLabel("1–3 / wheel select  •  ESC pause", null).AddTo(hints);
            downed = NewBox("downed").AddTo(hud);
            NewLabel("YOU ARE DOWN", "downed__title").AddTo(downed);
            NewLabel("You can still crawl and talk. A teammate carrying a medkit can revive you.", "downed__text").AddTo(downed);
            var bleed = NewBox("bleed").AddTo(downed);
            var bleedRow = NewBox("bleed__row").AddTo(bleed);
            NewLabel("BLEEDING OUT", "bleed__label").AddTo(bleedRow);
            bleedTime = NewLabel("", "bleed__time").AddTo(bleedRow);
            bleedFill = NewBox("bleed__fill").AddTo(NewBox("bleed__track").AddTo(bleed));
            spectator = NewBox("spectator").AddTo(hud);
            spectatorLabel = NewLabel("SPECTATING", "spectator__label").AddTo(spectator);
            spectatorName = NewLabel("", "spectator__name").AddTo(spectator);
            NewLabel("TAB  next survivor", "faint").AddTo(spectator);
            foreach (var element in hud.Query<VisualElement>().ToList()) element.pickingMode = PickingMode.Ignore;
        }

        // ---------------------------------------------------------------- per-frame state
        void Update()
        {
            if (root == null) return;
            var session = GameSession.Instance; var player = NetworkPlayer.Local; var round = RoundManager.Instance;
            if (menuCamera) { menuCamera.enabled = !player; var listener = menuCamera.GetComponent<AudioListener>(); if (listener) listener.enabled = !player; }
            if (player && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (options) CloseOptions();
                else if (round && round.Phase.Value == RoundPhase.Playing) SetPaused(!paused);
            }
            bool ended = round && (round.Phase.Value == RoundPhase.Lost || round.Phase.Value == RoundPhase.Won || round.Phase.Value == RoundPhase.Failed);
            if (ended) { UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true; if (player) player.SetPaused(true); paused = false; }

            bool inGameScene = session && SceneManager.GetActiveScene().name == session.gameScene;
            bool showMenu = false, showLobby = false, showLoading = false, showEnd = false, showHud = false;
            if (!session) { }
            else if (!round)
            {
                if (inGameScene) { showLoading = true; SetLoading("LOADING", "Preparing the round…"); }
                else if (LobbyRoster.Instance) { showLobby = true; UpdateLobby(session); }
                else { showMenu = true; UpdateMenu(session); }
            }
            else if (round.Phase.Value == RoundPhase.Generating) { showLoading = true; SetLoading("BUILDING THE " + MapName().ToUpperInvariant(), "Generating rooms, furniture and navigation…"); }
            else if (ended) { showEnd = true; UpdateEnd(round); }
            else if (!player) { showLoading = true; SetLoading("JOINING", "Joining the round…"); }
            else { showHud = true; UpdateHud(player, round); if (paused) UpdatePause(player, round); }

            Show(menu, showMenu && !options); Show(lobby, showLobby && !options); Show(loading, showLoading);
            Show(end, showEnd && !options); Show(pause, showHud && paused && !options); Show(optionsScreen, options);
            // The pause screen is its own composition; the HUD steps aside while it is open.
            hud.style.display = showHud && !(paused && !options) ? DisplayStyle.Flex : DisplayStyle.None;
            // Grain belongs to the menus; in play the post-processing film grain already covers the world.
            grainLayer.style.display = showHud && !paused ? DisplayStyle.None : DisplayStyle.Flex;
            if (options) voiceStatus.SetText(session && session.GetComponent<ProximityVoice>() ? session.GetComponent<ProximityVoice>().Status : "");
        }
        static string MapName() => LobbyRoster.Instance ? LobbyRoster.Instance.MapName : "Mansion";
        // Public so other input paths (e.g. a gamepad Start button) can drive the same local pause.
        public bool IsPaused => paused;
        public void SetPaused(bool value)
        {
            var round = RoundManager.Instance;
            if (value && !(round && round.Phase.Value == RoundPhase.Playing)) return;
            paused = value; var player = NetworkPlayer.Local; if (player) player.SetPaused(value);
        }
        public void ShowOptions() => OpenOptions(paused);
        void SetLoading(string title, string detail) { loadingTitle.SetText(title); loadingDetail.SetText(detail); }

        void UpdateMenu(GameSession session)
        {
            bool busy = session.Starting || (NetworkManager.Singleton && NetworkManager.Singleton.IsListening);
            hostButton.SetEnabled(!busy); joinButton.SetEnabled(!busy && codeField.value.Trim().Length >= 4);
            nameField.SetEnabled(!busy); codeField.SetEnabled(!busy);
            menuStatus.SetText(session.Status);
            menuStatus.EnableInClassList("status--error", session.Status.Contains("unavailable") || session.Status.Contains("ended") || session.Status.Contains("Could not") || session.Status.Contains("Invalid") || session.Status.Contains("valid"));
            resetButton.style.display = string.IsNullOrEmpty(session.Status) || session.Starting ? DisplayStyle.None : DisplayStyle.Flex;
        }
        void UpdateLobby(GameSession session)
        {
            var roster = LobbyRoster.Instance; var manager = NetworkManager.Singleton;
            bool host = manager && manager.IsServer, locked = roster.Started.Value || session.Starting;
            lobbyCode.SetText(session.JoinCode);
            // Rebuild the player list only when membership changes.
            var signature = new System.Text.StringBuilder();
            foreach (var member in roster.Members) signature.Append(member.clientId).Append(':').Append(member.name.ToString()).Append('|');
            if (signature.ToString() != rosterSignature)
            {
                rosterSignature = signature.ToString(); playerList.Clear();
                int seat = 0;
                foreach (var member in roster.Members)
                {
                    var row = NewBox("player-row").AddTo(playerList);
                    NewLabel(Seat(seat++), "player-row__seat").AddTo(row);
                    NewLabel(member.name.ToString(), "player-row__name").AddTo(row);
                    if (member.clientId == NetworkManager.ServerClientId) NewLabel("HOST", "player-row__tag").AddTo(row);
                    if (manager && member.clientId == manager.LocalClientId) NewLabel("YOU", "player-row__tag", "player-row__tag--you").AddTo(row);
                }
                // Open seats stay visible, so the party's size reads at a glance.
                for (; seat < session.maxPlayers; seat++)
                {
                    var row = NewBox("player-row", "player-row--empty").AddTo(playerList);
                    NewLabel(Seat(seat), "player-row__seat").AddTo(row);
                    NewLabel("Waiting for a survivor…", "player-row__name").AddTo(row);
                }
                playerCount.SetText($"{roster.Members.Count} / {session.maxPlayers}");
            }
            mapValue.SetText(roster.MapName);
            difficultyValue.SetText(roster.DifficultyName.ToUpperInvariant());
            if (shownDifficulty != roster.Difficulty.Value && roster.difficultyConfig)
            {
                shownDifficulty = roster.Difficulty.Value; var p = roster.difficultyConfig.Get(shownDifficulty);
                difficultyDetail.SetText($"{p.targetRoomCount} rooms  •  {p.requiredObjectiveCount} objectives  •  {p.enemyCount} {(p.enemyCount == 1 ? "monster" : "monsters")}");
            }
            foreach (var b in new[] { mapPrev, mapNext, difficultyPrev, difficultyNext }) b.SetEnabled(host && !locked);
            hostHint.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            startButton.SetEnabled(!locked);
            startButton.text = locked ? "STARTING…" : "START GAME";
            waitingLabel.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            lobbyStatus.SetText(session.Status);
        }
        void UpdateEnd(RoundManager round)
        {
            var phase = round.Phase.Value; var manager = NetworkManager.Singleton; bool host = manager && manager.IsServer;
            endTitle.SetText(phase switch { RoundPhase.Won => "YOU ESCAPED", RoundPhase.Lost => "NO ONE SURVIVED", _ => "ROUND COULD NOT START" });
            endTitle.EnableInClassList("end-title--won", phase == RoundPhase.Won);
            endTitle.EnableInClassList("end-title--lost", phase != RoundPhase.Won);
            endBody.SetText(phase switch { RoundPhase.Won => "At least one survivor made it out.", RoundPhase.Lost => "The house keeps what it takes.", _ => round.Failure.Value.ToString() });
            endStats.SetText(phase == RoundPhase.Failed ? "" : $"{MapName()}  •  seed {round.Seed.Value}");
            backToLobby.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            backToLobby.SetEnabled(GameSession.Instance && !GameSession.Instance.Transitioning);
            endHint.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
        }
        void UpdateHud(NetworkPlayer player, RoundManager round)
        {
            if (player != cachedPlayer)
            {
                cachedPlayer = player; playerStamina = player.GetComponent<PlayerStamina>();
                playerVignette = player.GetComponent<DownedVignette>(); playerSpectator = player.GetComponent<SpectatorController>();
                for (int i = 0; i < 3; i++) shownItems[i] = null;
            }
            var life = player.Life.Value;
            bool alive = life == PlayerLife.Alive, down = life == PlayerLife.Downed, watching = !alive && !down;
            // Vignette opacity/scale come from DownedVignette, which follows the replicated bleed-out timer.
            float opacity = playerVignette ? playerVignette.Opacity : 0;
            vignette.style.display = opacity > .001f ? DisplayStyle.Flex : DisplayStyle.None;
            if (opacity > .001f)
            {
                var colour = playerVignette.colour; vignette.style.unityBackgroundImageTintColor = new Color(colour.r, colour.g, colour.b, colour.a * opacity);
                vignette.style.scale = new Scale(Vector3.one * playerVignette.Scale);
            }
            crosshair.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            slotsRow.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            hints.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            stamina.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            objective.style.display = watching ? DisplayStyle.None : DisplayStyle.Flex;
            downed.style.display = down ? DisplayStyle.Flex : DisplayStyle.None;
            spectator.style.display = watching ? DisplayStyle.Flex : DisplayStyle.None;

            string text = alive && !paused ? player.Interaction.CurrentPrompt : "";
            prompt.EnableInClassList("prompt--hidden", string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) { promptText.SetText(text); prompt.EnableInClassList("prompt--unavailable", !player.Interaction.PromptAvailable); }

            if (!exit && Time.unscaledTime >= nextExitSearch) { nextExitSearch = Time.unscaledTime + .5f; exit = round.Exit ? round.Exit : FindAnyObjectByType<ExitDoor>(); }
            if (exit && (exit.Deposited.Value != shownDeposited || exit.Required.Value != shownRequired))
            {
                shownDeposited = exit.Deposited.Value; shownRequired = exit.Required.Value;
                objectiveCount.SetText($"{shownDeposited} / {shownRequired}");
                objectiveHint.SetText(exit.Unlocked ? "Exit unlocked  —  escape!" : "Carry objectives to the exit door");
                objective.EnableInClassList("objective--complete", exit.Unlocked);
            }
            if (Debug.isDebugBuild && shownSeed != round.Seed.Value) { shownSeed = round.Seed.Value; debugLine.SetText($"DEV  {MapName()}  seed {shownSeed}  {round.World.Rooms.Count} rooms"); }

            if (alive)
            {
                var inventory = player.Inventory;
                for (int i = 0; i < 3; i++)
                {
                    var item = inventory.Slots[i];
                    if (item != shownItems[i]) { shownItems[i] = item; slotNames[i].SetText(item ? item.displayName : "Empty"); slots[i].EnableInClassList("slot--empty", !item); }
                    slots[i].EnableInClassList("slot--selected", inventory.CurrentSlot == i && item);
                    string tag = ""; bool on = false;
                    if (item && item.TryGetComponent<WalkieTalkieUse>(out var radio)) { on = radio.Powered.Value; tag = on ? "RADIO ON" : "RADIO OFF"; }
                    else if (item && item.TryGetComponent<PlayerFlashlight>(out var light)) { on = light.IsOn; tag = on ? "LIGHT ON" : "F  LIGHT"; }
                    else if (item && item.GetComponent<ObjectiveItem>()) tag = "OBJECTIVE";
                    else if (item && item.GetComponent<MedkitItem>()) tag = "REVIVES";
                    slotTags[i].SetText(tag); slotTags[i].EnableInClassList("slot__tag--on", on);
                }
                float fraction = playerStamina ? Mathf.Clamp01(player.Stamina.Value / Mathf.Max(1, playerStamina.Maximum)) : 1;
                stamina.style.opacity = fraction > .995f ? .25f : 1;
                staminaFill.style.width = Length.Percent(fraction * 100);
                stamina.EnableInClassList("stamina--low", fraction < .25f);
            }
            if (down)
            {
                int seconds = Mathf.CeilToInt(player.BleedOutRemaining);
                if (seconds != shownSeconds) { shownSeconds = seconds; bleedTime.SetText($"{seconds / 60}:{seconds % 60:00}"); }
                bleedFill.style.width = Length.Percent((1 - player.BleedOutProgress) * 100);
            }
            if (watching)
            {
                var target = playerSpectator ? playerSpectator.Target : null;
                spectatorLabel.SetText(life == PlayerLife.Escaped ? "ESCAPED  —  WATCHING" : "SPECTATING");
                spectatorName.SetText(target ? target.DisplayName : "No survivors remain");
            }
        }
    }
}
