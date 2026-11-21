using System;
using System.IO;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The Steam Next Fest demo (GDD 2.16). A build with <c>ANTARIUM_DEMO</c> (or the editor with *Antarium → Demo → Play as
    /// Demo in Editor*) is the demo: its own save folder, its own Steam app (or none), no achievements, the Ancient Seal in every
    /// new colony, an end screen when the colony reaches the seal (or once after a while), a Nuptial Flight teaser and Wishlist
    /// buttons. The full game offers to continue a colony from the demo's save folder; loading it opens the seal.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Editor preference: play as the demo in the editor (its saves go to a <c>Demo</c> subfolder).</summary>
        public const string DemoInEditorPref = "Antarium.DemoInEditor";
        /// <summary>Player Settings → Product Name of the demo build: it picks the demo's own save folder.</summary>
        public const string DemoProductName = "ANTARIUM 2 Demo";
        /// <summary>Seconds between reaching the seal and the end screen, so the glimpse beyond it can be seen first.</summary>
        const float SealEndDelay = 3f;

        float _demoEndDelay;
        DemoDialogKind _demoEndKind;

        /// <summary>This build is the demo (or the editor plays as the demo).</summary>
        static bool DemoBuild
        {
            get
            {
#if ANTARIUM_DEMO
                return true;
#elif UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(DemoInEditorPref, false);
#else
                return false;
#endif
            }
        }

        bool IsDemo => _content != null && _content.ActiveDemo != null;

        /// <summary>Folder for saves, settings and the profile. The demo build has its own (product name); in the editor a subfolder.</summary>
        static string DataDirectory(bool demo)
        {
            string dir = Application.persistentDataPath;
#if UNITY_EDITOR
            if (demo) dir = Path.Combine(dir, "Demo");
#endif
            return dir;
        }

        /// <summary>Where the demo keeps its saves, seen from the full game (null if it can't be worked out).</summary>
        static string DemoSaveDirectory()
        {
            string dir = Application.persistentDataPath.TrimEnd('/', '\\');
#if UNITY_EDITOR
            return Path.Combine(dir, "Demo", "Saves");
#else
            var company = Path.GetDirectoryName(dir);
            return company == null ? null : Path.Combine(company, DemoProductName, "Saves");
#endif
        }

        void WireDemo()
        {
            if (_hud.Title != null)
            {
                _hud.Title.SetDemo(IsDemo);
                _hud.Title.Wishlist += Wishlist;
                _hud.Title.FullGame += () => OpenDemoDialog(DemoDialogKind.FullGame, fromMenu: false);
                _hud.Title.ImportDemo += ImportDemoColony;
            }
            if (_hud.Menu != null)
            {
                _hud.Menu.SetDemo(IsDemo);
                _hud.Menu.Wishlist += Wishlist;
                _hud.Menu.FullGame += () => OpenDemoDialog(DemoDialogKind.FullGame, fromMenu: true);
            }
            if (_hud.DemoPanel != null)
            {
                _hud.DemoPanel.Wishlist += Wishlist;
                _hud.DemoPanel.Closed += CloseSystemDialog;
            }
        }

        /// <summary>The full game's store page: the Steam overlay, or the browser without Steam.</summary>
        void Wishlist()
        {
            uint app = _content.Demo != null ? _content.Demo.Def.fullGameAppId : PlatformServices.SteamAppId;
            _platform.OpenStorePage(app != 0 ? app : PlatformServices.SteamAppId);
        }

        void OpenDemoDialog(DemoDialogKind kind, bool fromMenu)
        {
            if (_hud.DemoPanel == null) return;
            _backToMenu = fromMenu;
            _tools?.CancelTargeting();
            _hud.DemoPanel.Show(kind);
            _hud.ShowModal(_hud.DemoPanel.Root);
        }

        /// <summary>The demo: the Flight and Genes buttons (and keys) show what the Nuptial Flight is.</summary>
        bool DemoTeaser()
        {
            if (!IsDemo || _hud.DemoPanel == null) return false;
            if (_hud.IsShowing(_hud.DemoPanel.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root) && !SystemDialogOpen) OpenDemoDialog(DemoDialogKind.Flight, fromMenu: false);
            return true;
        }

        /// <summary>
        /// Every frame: the seal reached (a message, then the end screen once the glimpse beyond it was seen), the timed offer at
        /// a calm moment, and a word when the player digs or paints at the seal.
        /// </summary>
        void UpdateDemo(float dt, bool title, bool overUi, EventBus events)
        {
            if (!IsDemo || Capturing) return;
            foreach (var e in events.Read<DemoSealReachedEvent>())
            {
                _hud.ShowToast("Your colony reached the Ancient Seal. Beyond it, the deep glows...");
                _sim.MarkDemoEndShown();
                _demoEndKind = DemoDialogKind.Seal;
                _demoEndDelay = SealEndDelay;
            }
            if (title) return;
            if (_demoEndDelay > 0f)
            {
                _demoEndDelay -= dt;
                if (_demoEndDelay <= 0f)
                {
                    if (_hud.ModalOpen || _tools.Possessing) _demoEndDelay = 0.5f; // wait for the player to close what is open
                    else OpenDemoDialog(_demoEndKind, fromMenu: false);
                }
            }
            else if (!_hud.ModalOpen && !_tools.Possessing && _sim.DemoEndDue)
            {
                _sim.MarkDemoEndShown();
                OpenDemoDialog(DemoDialogKind.Time, fromMenu: false);
            }

            if (!overUi && !_hud.ModalOpen && _input.Primary.WasPressedThisFrame() && _tools.TargetingAbility < 0 && !_tools.TargetingPossess
                && (_tools.Mode == ToolMode.Dig || _tools.Mode == ToolMode.Mine) && _sim.IsSeal(_pointerCell)
                && _sim.World[_pointerCell.x, _pointerCell.y].IsRevealed)
                _hud.ShowToast("The Ancient Seal can't be broken in the demo. In the full game the depths go on.");
        }

        // ---------------------------------------------------------------- the full game: carrying a colony over from the demo

        /// <summary>
        /// One line about the newest colony in the demo's save folder, when the full game has no saves of its own yet; else null.
        /// </summary>
        string DemoImportNote()
        {
            if (IsDemo || Capturing || _content.Demo == null) return null;
            foreach (var store in _stores)
                if (store.HasSave) return null;
            var demo = DemoStores();
            if (demo == null) return null;
            var header = demo[SaveStore.LatestSlot(demo)].TryReadHeader();
            if (header == null) return null;
            return $"From the demo: {NumberFormat.Format(header.Ants)} ants, {header.MaxDepth} cells deep, played {HudText.Duration(header.PlayTime)}. " +
                   "The Ancient Seal opens.";
        }

        /// <summary>The demo's save slots, or null when the demo has saved nothing.</summary>
        static SaveStore[] DemoStores()
        {
            string dir = DemoSaveDirectory();
            if (dir == null || !Directory.Exists(dir)) return null;
            var stores = new SaveStore[SaveStore.SlotCount];
            bool any = false;
            for (int i = 0; i < stores.Length; i++)
            {
                stores[i] = new SaveStore(dir, i);
                any |= stores[i].HasSave;
            }
            return any ? stores : null;
        }

        /// <summary>
        /// Title → Continue your demo colony: the demo's slots are copied into the full game's (only while it has none), the newest
        /// is loaded (the seal opens, see <see cref="Simulation"/>), and the game goes on with its offline time.
        /// </summary>
        void ImportDemoColony()
        {
            var demo = DemoStores();
            if (demo == null || DemoImportNote() == null) return;
            try
            {
                _store.Wait();
                Directory.CreateDirectory(_platform.SaveDirectory);
                for (int i = 0; i < demo.Length && i < _stores.Length; i++)
                {
                    CopyIfExists(demo[i].MainPath, _stores[i].MainPath);
                    CopyIfExists(demo[i].BackupPath, _stores[i].BackupPath);
                    CopyIfExists(demo[i].OldPath, _stores[i].OldPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not copy the demo's saves: {e}");
                _hud.ShowToast("The demo's colony could not be copied: " + e.Message);
                return;
            }
            _store = _stores[SaveStore.LatestSlot(demo)];
            if (!TryLoad(applyOffline: false))
            {
                ShowTitle();
                return;
            }
            LeaveTitle();
            Save(background: false, announce: false); // the opened seal is part of the save from now on (the HUD says so)
        }

        static void CopyIfExists(string from, string to)
        {
            if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
        }
    }
}
