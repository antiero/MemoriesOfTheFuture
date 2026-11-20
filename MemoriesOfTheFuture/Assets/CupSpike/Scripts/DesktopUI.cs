using System;
using System.Collections.Generic;
using Memories.Audio;
using PictureChanges;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupSpike
{
    /// <summary>
    /// The desktop testing room's screen (UI Toolkit, styled by DesktopTesting.uss): a welcome card
    /// that explains everything, a status card each for the mug and the painting, the keys, toasts,
    /// and the Tab drawer with the Sound panel, timings and help. Reads and drives DesktopSim.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DesktopUI : MonoBehaviour
    {
        [SerializeField] DesktopSim sim;
        [SerializeField] StyleSheet style;

        enum Tab { Sound, Timings, Help }

        VisualElement _root, _left, _guide, _guidePill, _drawer, _scrim, _crosshair, _lookHint, _toast;
        Label _toastText;
        float _toastUntil;
        StatusCard _mug, _painting;
        Label _paintingMode;
        VisualElement[] _pips = new VisualElement[0];
        Tab _tab;
        readonly Dictionary<Tab, Button> _tabButtons = new Dictionary<Tab, Button>();
        readonly Dictionary<Tab, ScrollView> _tabPages = new Dictionary<Tab, ScrollView>();
        int _soundStatusVersion = -1;

        // Sound tab
        VisualElement _slotList, _soloBanner, _unsavedBanner, _filesBox;
        Label _soundStatus, _soloText, _filesText;
        Button _blending;
        readonly VisualElement[] _meterFills = new VisualElement[MixDirector.SnapshotNames.Length];
        readonly List<SlotRow> _rows = new List<SlotRow>();
        string _slotKey = "";

        // Timings tab
        readonly List<Action> _timingRefresh = new List<Action>();

        void Start()
        {
            if (sim == null) sim = FindAnyObjectByType<DesktopSim>();
            var doc = GetComponent<UIDocument>();
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList("dt-root");
            if (style != null) _root.styleSheets.Add(style);
            doc.rootVisualElement.Add(_root);
            doc.rootVisualElement.pickingMode = PickingMode.Ignore;

            BuildStatus();
            BuildGuide();
            BuildCentre();
            BuildDrawer();
            BuildWelcome();

            sim.Toast += ShowToast;
            sim.PointerOverUi = PointerOverUi;
            _soundStatusVersion = sim.Sound != null ? sim.Sound.StatusVersion : 0;
        }

        void OnDestroy() { if (sim != null) sim.Toast -= ShowToast; }

        /// <summary>Is the mouse over a card or button (so a click there shouldn't grab the mouse for looking)?</summary>
        bool PointerOverUi(Vector2 screen)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(p);
            return picked != null && picked != _root;
        }

        // ------------------------------------------------------------ helpers

        static VisualElement El(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        static Button Btn(VisualElement parent, string text, Action click, params string[] classes)
        {
            var b = new Button(click) { text = text, focusable = false };
            b.AddToClassList("dt-btn");
            foreach (var c in classes) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        static Button Toggle(VisualElement parent, string text, string tooltip, Action click)
        {
            var b = new Button(click) { text = text, focusable = false, tooltip = tooltip };
            b.AddToClassList("dt-toggle");
            parent?.Add(b);
            return b;
        }

        static void Key(VisualElement parent, string key) => Text(parent, key, "dt-key");

        static void KeyRow(VisualElement parent, string what, params string[] keys)
        {
            var row = El(parent, "dt-keyrow");
            var k = El(row, "dt-keys");
            foreach (var key in keys) Key(k, key);
            Text(row, what, "dt-keytext");
        }

        static void SetText(Label l, string t) { if (l.text != t) l.text = t; }
        static void Show(VisualElement e, bool on) { var d = on ? DisplayStyle.Flex : DisplayStyle.None; if (e.style.display != d) e.style.display = d; }
        static void SetClass(VisualElement e, string c, bool on) { if (e.ClassListContains(c) != on) e.EnableInClassList(c, on); }
        static void Fill(VisualElement fill, float x) => fill.style.width = Length.Percent(Mathf.Clamp01(x) * 100f);

        // ------------------------------------------------------------ left: title and status

        class StatusCard
        {
            public VisualElement root, bar, fill;
            public Label title, hint;
        }

        void BuildStatus()
        {
            _left = El(_root, "dt-left");
            _left.pickingMode = PickingMode.Ignore;

            var brand = El(_left, "dt-card");
            Text(brand, "MEMORIES OF THE FUTURE", "dt-eyebrow");
            Text(brand, "Desktop testing", "dt-title");
            Text(brand, "A stand-in room with the mug and the painting. Tab opens the Sound panel.", "dt-sub");

            _mug = Status("THE MUG", "1 to go there", "dt-status--mug");
            _painting = Status("THE PAINTING", "", "dt-status--painting");
            _paintingMode = _painting.root.Q<Label>("mode");
            var pips = El(null, "dt-pips");
            _painting.root.Insert(_painting.root.IndexOf(_painting.bar), pips);
            int count = sim.Painting != null && sim.Painting.Stages != null ? sim.Painting.Stages.StageCount : 0;
            _pips = new VisualElement[count];
            for (int i = 0; i < count; i++) _pips[i] = El(pips, "dt-pip");
            if (sim.Painting == null) Show(_painting.root, false);
        }

        StatusCard Status(string label, string chip, string cls)
        {
            var c = new StatusCard { root = El(_left, "dt-card", "dt-status", cls) };
            var head = El(c.root, "dt-row");
            Text(head, label, "dt-status__label", "dt-grow");
            var chipLabel = Text(head, chip, "dt-chip");
            chipLabel.name = "mode";
            c.title = Text(c.root, "", "dt-status__title");
            c.hint = Text(c.root, "", "dt-status__hint");
            c.bar = El(c.root, "dt-bar");
            c.fill = El(c.bar, "dt-bar__fill");
            return c;
        }

        void RefreshStatus()
        {
            Apply(_mug, sim.MugPhase());
            if (sim.Painting == null) return;
            Apply(_painting, sim.PaintingPhase());
            SetText(_paintingMode, "2 to go there  ·  " + PictureCanvas.ModeName(sim.Painting.Mode));
            var st = sim.Painting.Stages;
            bool pips = st != null && sim.Painting.Mode == PictureCanvas.TriggerMode.UnseenSwap;
            if (_pips.Length > 0) Show(_pips[0].parent, pips);
            if (pips) for (int i = 0; i < _pips.Length; i++) SetClass(_pips[i], "dt-pip--on", i <= st.Stage);
        }

        static void Apply(StatusCard c, DesktopSim.Phase p)
        {
            SetText(c.title, p.title);
            SetText(c.hint, p.hint);
            Show(c.hint, !string.IsNullOrEmpty(p.hint));
            Show(c.bar, p.progress >= 0f);
            if (p.progress >= 0f) Fill(c.fill, p.progress);
        }

        // ------------------------------------------------------------ bottom left: keys

        void BuildGuide()
        {
            _guide = El(_root, "dt-card", "dt-guide");
            var head = El(_guide, "dt-row");
            Text(head, "KEYS", "dt-eyebrow", "dt-grow");
            var hide = El(head, "dt-row");
            Key(hide, "H");
            Text(hide, "hides this", "dt-small");

            Text(_guide, "MOVE", "dt-keygroup");
            KeyRow(_guide, "Look around (click the room first)", "Mouse");
            KeyRow(_guide, "Walk", "W", "A", "S", "D");
            KeyRow(_guide, "Go to the mug / the painting", "1", "2");

            Text(_guide, "THE MUG", "dt-keygroup");
            KeyRow(_guide, "Reach for it / pull back (or scroll)", "↑", "↓");
            KeyRow(_guide, "Lean over it / lean back", "E");
            KeyRow(_guide, "Pick it up / put it down (the dream)", "G");
            KeyRow(_guide, "Start the dream / wake up", "F");
            KeyRow(_guide, "Scream now", "Space");
            KeyRow(_guide, "Wake-up: scream or silent", "M");

            Text(_guide, "THE PAINTING", "dt-keygroup");
            KeyRow(_guide, "Start it again", "P");
            KeyRow(_guide, "Animate it now", "V");
            KeyRow(_guide, "Change how it's triggered", "T");

            Text(_guide, "SOUND", "dt-keygroup");
            KeyRow(_guide, "Sound panel, timings, help", "Tab");
            KeyRow(_guide, "Reload your sound files", "L");
            KeyRow(_guide, "Free the mouse", "Esc");

            _guidePill = El(_root, "dt-pill", "dt-guide-pill");
            Key(_guidePill, "H");
            Text(_guidePill, "Show the keys", "dt-keytext");
        }

        // ------------------------------------------------------------ centre: crosshair, hint, toast

        void BuildCentre()
        {
            _crosshair = El(_root, "dt-crosshair");
            _crosshair.pickingMode = PickingMode.Ignore;

            var bottom = El(_root, "dt-strip", "dt-strip--bottom");
            bottom.pickingMode = PickingMode.Ignore;
            _lookHint = El(bottom, "dt-pill", "dt-look-hint");
            _lookHint.pickingMode = PickingMode.Ignore;
            Text(_lookHint, "Click the room to look around", "dt-keytext");
            Key(_lookHint, "Esc");
            Text(_lookHint, "frees the mouse", "dt-keytext");

            var top = El(_root, "dt-strip", "dt-strip--top");
            top.pickingMode = PickingMode.Ignore;
            _toast = El(top, "dt-toast");
            _toast.pickingMode = PickingMode.Ignore;
            _toastText = Text(_toast, "");
        }

        void ShowToast(string message)
        {
            _toastText.text = message;
            _toast.AddToClassList("dt-toast--on");
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        // ------------------------------------------------------------ the drawer (Tab)

        void BuildDrawer()
        {
            _drawer = El(_root, "dt-card", "dt-drawer");
            var head = El(_drawer, "dt-row");
            var titles = El(head, "dt-grow");
            Text(titles, "TAB PANEL", "dt-eyebrow");
            Text(titles, "Sound, timings and help", "dt-title");
            Btn(head, "Close", () => sim.PanelOpen = false, "dt-btn--small");

            var tabs = El(_drawer, "dt-tabs");
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = new Button(() => SelectTab(tab)) { text = t.ToString(), focusable = false };
                b.AddToClassList("dt-tab");
                tabs.Add(b);
                _tabButtons[t] = b;
                var page = new ScrollView(ScrollViewMode.Vertical) { focusable = false };
                page.AddToClassList("dt-scroll");
                page.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _drawer.Add(page);
                _tabPages[t] = page;
            }
            BuildSoundTab(_tabPages[Tab.Sound].contentContainer);
            BuildTimingsTab(_tabPages[Tab.Timings].contentContainer);
            BuildHelpTab(_tabPages[Tab.Help].contentContainer);
            SelectTab(Tab.Sound);
        }

        void SelectTab(Tab t)
        {
            _tab = t;
            foreach (var kv in _tabButtons) SetClass(kv.Value, "dt-tab--on", kv.Key == t);
            foreach (var kv in _tabPages) Show(kv.Value, kv.Key == t);
        }

        // ------------------------------------------------------------ Sound tab

        void BuildSoundTab(VisualElement page)
        {
            var actions = El(page, "dt-row");
            Btn(actions, "Save mix", () => sim.Sound.SaveMix(), "dt-btn--primary");
            Btn(actions, "Reload files", sim.ReloadSounds);
            Btn(actions, "Open sounds folder", () => sim.Sound.OpenFolder());
            _soundStatus = Text(page, "", "dt-note");
            _unsavedBanner = El(page, "dt-banner");
            Text(_unsavedBanner, "Unsaved changes: press Save mix to keep them for next time.");

            Text(page, "MIX RIGHT NOW", "dt-section");
            var mix = El(page, "dt-box");
            var meters = El(mix, "dt-row");
            for (int i = 0; i < MixDirector.SnapshotNames.Length; i++)
            {
                var m = El(meters, "dt-meter");
                Text(m, MixDirector.SnapshotNames[i]);
                _meterFills[i] = El(El(m, "dt-bar"), "dt-bar__fill");
            }
            var blendRow = El(mix, "dt-row");
            blendRow.style.marginTop = 8;
            _blending = Toggle(blendRow, "Following", "Blending between snapshots", () =>
            {
                var md = MixDirector.Current;
                if (md != null) md.blending = !md.blending;
            });
            _blending.style.marginLeft = 0;
            _blending.style.marginRight = 8;
            Text(blendRow, "The mixer follows the experience. Click to hold the current snapshot while you tune it in the Audio Mixer.", "dt-small", "dt-shrink");

            _soloBanner = El(page, "dt-banner", "dt-row");
            _soloText = Text(_soloBanner, "", "dt-grow");
            Btn(_soloBanner, "Clear solo", () => SoundSlot.Soloed = null, "dt-btn--small");

            Text(page, "SOUNDS", "dt-section");
            _slotList = El(page);

            Text(page, "YOUR FILES", "dt-section");
            _filesBox = El(page, "dt-box");
            _filesText = Text(_filesBox, "", "dt-small");

            Text(page, Application.isEditor
                ? "In Unity, changes here go straight into the Sound Slot assets and are kept when you stop."
                : "Save mix writes ~/MemoriesOfTheFuture/SoundMix.json; the app loads it every time it starts. In Unity: Memories > Audio > Import Mix From Desktop App.",
                "dt-note");
        }

        class SlotRow
        {
            public SoundSlot slot;
            public VisualElement root, more;
            public Button solo, mute, expand, prev, next, builtIn, spatial, room;
            public Label group, file;
            public Field level, pitch, min, max, reverb;
        }

        class Field
        {
            public Slider slider;
            public Label value;
            public Func<float> get;
            public Func<float, string> format;
            public void Refresh()
            {
                float v = get();
                if (Mathf.Abs(slider.value - v) > 0.0001f) slider.SetValueWithoutNotify(v);
                var t = format(v);
                if (value.text != t) value.text = t;
            }
        }

        static Field MakeField(VisualElement parent, string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var row = El(parent, "dt-field");
            Text(row, label, "dt-field__label");
            var s = new Slider(min, max) { focusable = false };
            row.Add(s);
            var f = new Field { slider = s, value = Text(row, "", "dt-field__value"), get = get, format = format };
            s.RegisterValueChangedCallback(e => set(e.newValue));
            f.Refresh();
            return f;
        }

        static string Db(float db) => db <= -60f ? "off" : (db > 0f ? "+" : "") + db.ToString("0.0") + " dB";

        void RebuildSlots()
        {
            _slotList.Clear();
            _rows.Clear();
            foreach (var slot in sim.Sound.Slots) _rows.Add(MakeRow(slot));
        }

        SlotRow MakeRow(SoundSlot slot)
        {
            var sound = sim.Sound;
            var r = new SlotRow { slot = slot, root = El(_slotList, "dt-slot") };
            r.root.tooltip = slot.notes;
            var head = El(r.root, "dt-row");
            El(head, "dt-slot__dot");
            Text(head, slot.name, "dt-slot__name");
            r.group = Text(head, "", "dt-slot__group", "dt-grow");
            r.solo = Toggle(head, "S", "Solo: hear only this", () => SoundSlot.Soloed = SoundSlot.Soloed == slot ? null : slot);
            r.mute = Toggle(head, "M", "Mute", () => slot.Muted = !slot.Muted);
            r.expand = Toggle(head, "More", "Distances, room acoustics, reverb", () => Show(r.more, r.more.style.display == DisplayStyle.None));

            r.level = MakeField(r.root, "Level", -60f, 20f, () => slot.volumeDb,
                v => { slot.volumeDb = Mathf.Round(v * 10f) / 10f; sound.Edited(slot); }, Db);
            r.pitch = MakeField(r.root, "Pitch", 0.1f, 3f, () => slot.pitch,
                v => { slot.pitch = Mathf.Round(v * 100f) / 100f; sound.Edited(slot); }, v => "x" + v.ToString("0.00"));

            var file = El(r.root, "dt-field");
            Text(file, "File", "dt-field__label");
            r.prev = Btn(file, "‹", () => sound.StepFile(slot, -1), "dt-btn--icon");
            r.file = Text(file, "", "dt-slot__file");
            r.next = Btn(file, "›", () => sound.StepFile(slot, 1), "dt-btn--icon");
            r.builtIn = Btn(file, "Built-in", () => sound.UseBuiltIn(slot), "dt-btn--small");
            r.builtIn.tooltip = "Go back to the slot's own files (until the next reload)";

            r.more = El(r.root, "dt-more");
            var toggles = El(r.more, "dt-row");
            Text(toggles, "Space", "dt-field__label");
            r.spatial = Toggle(toggles, "Spatial", "On: from a point in the room. Off: stereo in the ears", () =>
            {
                slot.spatial = !slot.spatial;
                if (!slot.spatial) slot.volumeDb = Mathf.Min(slot.volumeDb, 0f);   // above 0 dB only works spatialised
                sound.Edited(slot);
            });
            r.room = Toggle(toggles, "Room acoustics", "Reflections and reverb from the room", () => { slot.roomAcoustics = !slot.roomAcoustics; sound.Edited(slot); });
            r.min = MakeField(r.more, "Full volume within", 0.05f, 10f, () => slot.minDistance,
                v => { slot.minDistance = Mathf.Round(v * 100f) / 100f; sound.Edited(slot); }, v => v.ToString("0.00") + " m");
            r.max = MakeField(r.more, "Quietest beyond", 1f, 100f, () => slot.maxDistance,
                v => { slot.maxDistance = Mathf.Max(slot.minDistance + 0.1f, Mathf.Round(v * 10f) / 10f); sound.Edited(slot); }, v => v.ToString("0.0") + " m");
            r.reverb = MakeField(r.more, "Reverb send", -60f, 20f, () => slot.reverbSendDb,
                v => { slot.reverbSendDb = Mathf.Round(v * 10f) / 10f; sound.Edited(slot); }, Db);
            Show(r.more, false);
            return r;
        }

        void RefreshSound()
        {
            var sound = sim.Sound;
            if (sound == null) return;
            var key = "";
            foreach (var s in sound.Slots) key += s.name + "|";
            if (key != _slotKey) { _slotKey = key; RebuildSlots(); }

            SetText(_soundStatus, sound.Loading ? "Loading the Sounds folder..." : sound.Status);
            Show(_unsavedBanner, sound.Unsaved && !Application.isEditor);

            var md = MixDirector.Current;
            for (int i = 0; i < _meterFills.Length; i++) Fill(_meterFills[i], md != null ? md.Weights[i] : 0f);
            _blending.text = md == null ? "No mixer" : md.blending ? "Following" : "Paused";
            SetClass(_blending, "dt-toggle--on", md != null && md.blending);

            Show(_soloBanner, SoundSlot.Soloed != null);
            if (SoundSlot.Soloed != null) SetText(_soloText, "Solo: only " + SoundSlot.Soloed.name + " is heard");

            foreach (var r in _rows) RefreshRow(r);

            var lines = sound.LoadedFiles.Count == 0
                ? "No files yet. Put a file named after a sound (scream_take2.wav, painting_soundtrack.mp3...) in " +
                  SoundMixFile.SoundsFolder + ", then press L."
                : string.Join("\n", sound.LoadedFiles);
            SetText(_filesText, lines);
        }

        void RefreshRow(SlotRow r)
        {
            var slot = r.slot;
            if (slot == null) return;
            bool playing = false;
            foreach (var v in slot.Voices) if (v.Source != null && v.Source.isPlaying && v.Source.volume > 0.0001f) { playing = true; break; }
            SetClass(r.root, "dt-slot--playing", playing);
            SetText(r.group, slot.output != null ? slot.output.name : "");
            SetClass(r.solo, "dt-toggle--solo", SoundSlot.Soloed == slot);
            SetClass(r.mute, "dt-toggle--mute", slot.Muted);
            SetClass(r.expand, "dt-toggle--on", r.more.style.display != DisplayStyle.None);

            r.level.slider.highValue = slot.spatial ? 20f : 0f;
            r.level.Refresh();
            r.pitch.Refresh();

            bool over = slot.RuntimeOverride != null;
            var valid = slot.clips.FindAll(c => c != null);
            string order = slot.variation == SoundSlot.Variation.Random ? " · random" : slot.variation == SoundSlot.Variation.InOrder ? " · in order" : "";
            SetText(r.file, over ? slot.RuntimeOverride.name + " (your file)"
                          : valid.Count == 0 ? "No file" : valid[0].name + (valid.Count > 1 ? $"  ({valid.Count} files{order})" : ""));
            SetClass(r.file, "dt-slot__file--override", over);
            Show(r.prev, !over && valid.Count > 1);
            Show(r.next, !over && valid.Count > 1);
            Show(r.builtIn, over);

            if (r.more.style.display == DisplayStyle.None) return;
            SetClass(r.spatial, "dt-toggle--on", slot.spatial);
            SetClass(r.room, "dt-toggle--on", slot.roomAcoustics);
            r.min.Refresh();
            r.max.Refresh();
            r.reverb.Refresh();
        }

        // ------------------------------------------------------------ Timings tab

        void BuildTimingsTab(VisualElement page)
        {
            var mug = sim.Mug;
            var dream = sim.Dream;
            var painting = sim.Painting;
            if (mug != null)
            {
                Text(page, "THE MUG", "dt-section");
                var box = El(page, "dt-box");
                TimingField(box, "Breath starts at", 8f, 60f, () => mug.ApproachStart * 100f, v => mug.ApproachStart = v / 100f, v => v.ToString("0") + " cm");
                TimingField(box, "Silence before scream", 0f, 2f, () => mug.SilenceBeat, v => mug.SilenceBeat = v, v => v.ToString("0.00") + " s");
                ChoiceRow(box, "Touch distance", () => (mug.TouchZone * 100f).ToString("0") + " cm", "Switch", mug.CycleZone);
            }
            if (dream != null)
            {
                Text(page, "THE DREAM", "dt-section");
                var box = El(page, "dt-box");
                TimingField(box, "Hold before it starts", 0.5f, 15f, () => dream.HoldBeforeDream, v => dream.HoldBeforeDream = v, v => v.ToString("0.0") + " s");
                TimingField(box, "Time to go fully in", 2f, 30f, () => dream.DeepenTime, v => dream.DeepenTime = v, v => v.ToString("0.0") + " s");
                TimingField(box, "Waking fade", 0.2f, 8f, () => dream.WakeFadeTime, v => dream.WakeFadeTime = v, v => v.ToString("0.0") + " s");
                ChoiceRow(box, "Waking up", () => dream.Wake == DreamState.WakeMode.Scream ? "the mug screams" : "silent", "Switch (M)", sim.CycleWake);
            }
            if (painting != null)
            {
                Text(page, "THE PAINTING", "dt-section");
                var box = El(page, "dt-box");
                ChoiceRow(box, "Trigger", () => PictureCanvas.ModeName(painting.Mode), "Switch (T)", sim.CyclePaintingMode);
                TimingField(box, "Reveal when found", 0.5f, 8f, () => painting.RevealDuration, v => painting.RevealDuration = v, v => v.ToString("0.0") + " s");
                var st = painting.Stages;
                if (st != null)
                {
                    TimingField(box, "Look before it changes", 0f, 5f, () => st.SeenDwell, v => st.SeenDwell = v, v => v.ToString("0.00") + " s");
                    TimingField(box, "Out of view to change", 0.1f, 5f, () => st.UnseenTime, v => st.UnseenTime = v, v => v.ToString("0.00") + " s");
                }
                var row = El(box, "dt-row");
                row.style.marginTop = 8;
                Btn(row, "Replay the painting (P)", sim.ReplayPainting, "dt-btn--small");
                Btn(row, "Animate now (V)", sim.AnimatePainting, "dt-btn--small");
            }
            Text(page, "Timings aren't saved: note the values you like and pass them on.", "dt-note");
        }

        void TimingField(VisualElement parent, string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var f = MakeField(parent, label, min, max, get, set, format);
            _timingRefresh.Add(f.Refresh);
        }

        void ChoiceRow(VisualElement parent, string label, Func<string> value, string button, Action click)
        {
            var row = El(parent, "dt-field");
            Text(row, label, "dt-field__label");
            var v = Text(row, "", "dt-grow");
            Btn(row, button, click, "dt-btn--small");
            _timingRefresh.Add(() => SetText(v, value()));
        }

        // ------------------------------------------------------------ Help tab and the welcome card

        static void Steps(VisualElement parent, string kind, params string[] steps)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                var row = El(parent, "dt-step", "dt-step--" + kind);
                Text(row, (i + 1).ToString(), "dt-step__n");
                Text(row, steps[i], "dt-p");
            }
        }

        static readonly string[] MugSteps =
        {
            "Reach slowly with the Up arrow (or scroll). The mug starts to breathe. Pull back with Down: it stops dead.",
            "Reach all the way: a beat of silence, then the scream.",
            "Press E to lean over it and look in: the tunnel opens.",
            "Press G to pick it up and hold it: the room drains and echoes ripple out of it, then the dream spreads. G again puts it down and you drift back.",
        };

        static readonly string[] PaintingSteps =
        {
            "Press 2 to stand in front of the painting. Look at it for a moment.",
            "Turn away (mouse). When you look back, it has changed.",
            "Walk close (W) between looks and it goes all the way: the last change comes with a scream. P starts it again.",
        };

        static readonly string[] SoundSteps =
        {
            "Press Tab for the Sound panel: every sound with its level, pitch, file and solo / mute. Changes are heard straight away.",
            "To try your own file, name it after the sound (scream_take2.wav) and put it in your MemoriesOfTheFuture/Sounds folder, then press L.",
            "Happy with it? Press Save mix and send the developer the SoundMix.json file next to that folder.",
        };

        void BuildHelpTab(VisualElement page)
        {
            Text(page, "What this is", "dt-h");
            Text(page, "A plain stand-in room with both experiences running on the same code as the headset: " +
                       "the mug on the table and the painting on the wall to your right. Walk between them, try them, " +
                       "and change the sound while it plays.", "dt-p");
            Text(page, "The mug", "dt-h");
            Steps(page, "mug", MugSteps);
            Text(page, "The painting", "dt-h");
            Steps(page, "painting", PaintingSteps);
            Text(page, "Changing the sound", "dt-h");
            Steps(page, "sound", SoundSteps);
            Text(page, "Where things are", "dt-h");
            Text(page, "Your sound files:  " + SoundMixFile.SoundsFolder + "\nYour saved mix:  " + SoundMixFile.MixPath +
                       "\nThe Sounds folder's README.txt lists every sound's file name.", "dt-p");
            Text(page, "Good to know", "dt-h");
            Text(page, "Spatial sound is only a rough guide on a laptop: check it on the headset. Headphones help a lot here.", "dt-p");
            var row = El(page, "dt-row");
            row.style.marginTop = 10;
            Btn(row, "Show the welcome card again", () => { sim.PanelOpen = false; sim.WelcomeOpen = true; });
        }

        void BuildWelcome()
        {
            _scrim = El(_root, "dt-scrim");
            var card = El(_scrim, "dt-card", "dt-welcome");
            Text(card, "MEMORIES OF THE FUTURE  ·  DESKTOP TESTING", "dt-eyebrow");
            Text(card, "Welcome to the test room", "dt-title");
            Text(card, "Everything from the headset, on your Mac: the screaming mug and the changing painting, " +
                       "with a mouse and keyboard. Try each one, then shape its sound as it plays. Headphones recommended.", "dt-lead");

            var cols = El(card, "dt-columns");
            var a = El(cols, "dt-column");
            Text(a, "THE MUG  ·  ON THE TABLE", "dt-column-title", "dt-column-title--mug");
            Steps(a, "mug", MugSteps);
            var b = El(cols, "dt-column");
            Text(b, "THE PAINTING  ·  ON YOUR RIGHT", "dt-column-title", "dt-column-title--painting");
            Steps(b, "painting", PaintingSteps);
            var c = El(cols, "dt-column", "dt-column--last");
            Text(c, "THE SOUND  ·  YOURS TO CHANGE", "dt-column-title", "dt-column-title--sound");
            Steps(c, "sound", SoundSteps);

            var keys = El(card, "dt-columns");
            keys.style.marginTop = 14;
            var k1 = El(keys, "dt-column");
            KeyRow(k1, "Click the room, then look", "Mouse");
            KeyRow(k1, "Walk", "W", "A", "S", "D");
            KeyRow(k1, "Go to the mug / painting", "1", "2");
            var k2 = El(keys, "dt-column");
            KeyRow(k2, "Reach / pull back", "↑", "↓");
            KeyRow(k2, "Lean over the mug", "E");
            KeyRow(k2, "Pick up / put down the mug", "G");
            var k3 = El(keys, "dt-column", "dt-column--last");
            KeyRow(k3, "Sound panel and help", "Tab");
            KeyRow(k3, "Reload your files", "L");
            KeyRow(k3, "Free the mouse", "Esc");

            var foot = El(card, "dt-welcome-foot");
            Btn(foot, "Start", () => sim.WelcomeOpen = false, "dt-btn--primary");
            var or = El(foot, "dt-row");
            or.style.marginLeft = 12;
            Text(or, "or press", "dt-small");
            Key(or, "Enter");
            var spacer = El(foot, "dt-grow");
            spacer.pickingMode = PickingMode.Ignore;
            Text(foot, "Every key is listed bottom left; Tab > Help has all of this again.", "dt-small");
        }

        // ------------------------------------------------------------ every frame

        void Update()
        {
            if (_root == null || sim == null) return;
            bool welcome = sim.WelcomeOpen;
            Show(_scrim, welcome);
            Show(_left, !welcome);
            Show(_guide, !welcome && sim.GuideVisible && !sim.PanelOpen);
            Show(_guidePill, !welcome && !sim.GuideVisible && !sim.PanelOpen);
            Show(_drawer, !welcome && sim.PanelOpen);
            Show(_crosshair, !welcome && sim.MouseLook);
            Show(_lookHint.parent, !welcome && !sim.MouseLook && !sim.PanelOpen);

            if (_toastUntil > 0f && Time.unscaledTime > _toastUntil) { _toast.RemoveFromClassList("dt-toast--on"); _toastUntil = 0f; }

            if (welcome) return;
            if (sim.Sound != null && sim.Sound.StatusVersion != _soundStatusVersion)
            {
                // "Saved to ...", "Loaded 2 file(s) ...": worth a toast even with the panel closed.
                _soundStatusVersion = sim.Sound.StatusVersion;
                if (!string.IsNullOrEmpty(sim.Sound.Status)) ShowToast(sim.Sound.Status);
            }
            RefreshStatus();
            if (!sim.PanelOpen) return;
            if (_tab == Tab.Sound) RefreshSound();
            else if (_tab == Tab.Timings) foreach (var r in _timingRefresh) r();
        }
    }
}
