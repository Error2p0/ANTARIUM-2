using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Antarium.Gameplay
{
    /// <summary>Typed access to the actions in AntariumInput.inputactions.</summary>
    public sealed class GameInput : IDisposable
    {
        readonly InputActionAsset _asset;

        public readonly InputAction Pan;
        public readonly InputAction Zoom;
        public readonly InputAction DragPan;
        public readonly InputAction PointerPosition;
        public readonly InputAction PointerDelta;
        public readonly InputAction Primary;
        public readonly InputAction Secondary;
        public readonly InputAction ToolDig;
        public readonly InputAction ToolMine;
        public readonly InputAction ToolStorage;
        public readonly InputAction ToolBuild;
        public readonly InputAction ToolPheromone;
        /// <summary>F: take control of the ant under the cursor, or let go.</summary>
        public readonly InputAction Possess;
        /// <summary>Space: the possessed ant picks up / delivers / drops.</summary>
        public readonly InputAction Interact;
        public readonly InputAction Castes;
        public readonly InputAction Rotate;
        public readonly InputAction NextBuilding;
        public readonly InputAction PrevBuilding;
        public readonly InputAction Overlay;
        public readonly InputAction Research;
        /// <summary>G: the Genetic Memory tree.</summary>
        public readonly InputAction Genetics;
        /// <summary>N: the Nuptial Flight screen.</summary>
        public readonly InputAction Flight;
        /// <summary>Z / X / C / V: the abilities in bar order.</summary>
        public readonly InputAction[] Abilities;
        public readonly InputAction Menu;
        public readonly InputAction QuickSave;
        public readonly InputAction QuickLoad;
        public readonly InputAction TriggerEvent;
        public readonly InputAction TriggerRaid;
        public readonly InputAction ToggleHud;
        public readonly InputAction SpawnAnts;
        public readonly InputAction FinishBuilds;
        public readonly InputAction GiveItems;
        public readonly InputAction GivePrestige;
        /// <summary>F1: controls and help.</summary>
        public readonly InputAction Help;
        /// <summary>F4 (dev builds): cycle the game speed.</summary>
        public readonly InputAction GameSpeed;

        /// <summary>A keyboard / mouse binding the player may change in the settings.</summary>
        public readonly struct Rebindable
        {
            public readonly string Group, Name;
            public readonly InputAction Action;
            public readonly int Binding;
            public Rebindable(string group, string name, InputAction action, int binding) { Group = group; Name = name; Action = action; Binding = binding; }
        }

        /// <summary>Every binding shown in the controls list, in display order.</summary>
        public readonly Rebindable[] Rebindables;

        /// <summary>Changes whenever bindings change, so cached key labels know to refresh.</summary>
        public int BindingsVersion { get; private set; }

        public GameInput(InputActionAsset asset)
        {
            _asset = asset ? asset : throw new ArgumentNullException(nameof(asset));
            Pan = asset.FindAction("Camera/Pan", true);
            Zoom = asset.FindAction("Camera/Zoom", true);
            DragPan = asset.FindAction("Camera/DragPan", true);
            PointerPosition = asset.FindAction("Pointer/Position", true);
            PointerDelta = asset.FindAction("Pointer/Delta", true);
            Primary = asset.FindAction("Tools/Primary", true);
            Secondary = asset.FindAction("Tools/Secondary", true);
            ToolDig = asset.FindAction("Tools/ToolDig", true);
            ToolMine = asset.FindAction("Tools/ToolMine", true);
            ToolStorage = asset.FindAction("Tools/ToolStorage", true);
            ToolBuild = asset.FindAction("Tools/ToolBuild", true);
            ToolPheromone = asset.FindAction("Tools/ToolPheromone", true);
            Possess = asset.FindAction("Tools/Possess", true);
            Interact = asset.FindAction("Tools/Interact", true);
            Castes = asset.FindAction("View/Castes", true);
            Rotate = asset.FindAction("Tools/Rotate", true);
            NextBuilding = asset.FindAction("Tools/NextBuilding", true);
            PrevBuilding = asset.FindAction("Tools/PrevBuilding", true);
            Overlay = asset.FindAction("View/Overlay", true);
            Research = asset.FindAction("View/Research", true);
            Genetics = asset.FindAction("View/Genetics", true);
            Flight = asset.FindAction("View/Flight", true);
            Abilities = new[]
            {
                asset.FindAction("Tools/Ability1", true),
                asset.FindAction("Tools/Ability2", true),
                asset.FindAction("Tools/Ability3", true),
                asset.FindAction("Tools/Ability4", true),
            };
            Menu = asset.FindAction("System/Menu", true);
            QuickSave = asset.FindAction("System/QuickSave", true);
            QuickLoad = asset.FindAction("System/QuickLoad", true);
            TriggerEvent = asset.FindAction("Debug/TriggerEvent", true);
            TriggerRaid = asset.FindAction("Debug/TriggerRaid", true);
            ToggleHud = asset.FindAction("Debug/ToggleHud", true);
            SpawnAnts = asset.FindAction("Debug/SpawnAnts", true);
            FinishBuilds = asset.FindAction("Debug/FinishBuilds", true);
            GiveItems = asset.FindAction("Debug/GiveItems", true);
            GivePrestige = asset.FindAction("Debug/GivePrestige", true);
            Help = asset.FindAction("System/Help", true);
            GameSpeed = asset.FindAction("Debug/GameSpeed", true);
            Rebindables = BuildRebindables();
            asset.Enable();
        }

        Rebindable[] BuildRebindables()
        {
            var list = new System.Collections.Generic.List<Rebindable>();
            void Add(string group, string name, InputAction action, int binding = -1)
            {
                if (binding < 0) binding = FirstPointerOrKey(action);
                if (binding >= 0) list.Add(new Rebindable(group, name, action, binding));
            }
            // The WASD composite: its parts follow it (up, down, left, right).
            int wasd = -1;
            for (int i = 0; i < Pan.bindings.Count && wasd < 0; i++)
                if (Pan.bindings[i].isComposite) wasd = i;
            if (wasd >= 0)
            {
                string[] parts = { "Camera / ant up", "Camera / ant down", "Camera / ant left", "Camera / ant right" };
                for (int i = 0; i < parts.Length && wasd + 1 + i < Pan.bindings.Count; i++)
                    Add("Camera", parts[i], Pan, wasd + 1 + i);
            }
            Add("Camera", "Drag the view", DragPan);
            Add("Tools", "Use tool / bite", Primary);
            Add("Tools", "Erase / cancel / pick up", Secondary);
            Add("Tools", "Dig tool", ToolDig);
            Add("Tools", "Mine orders", ToolMine);
            Add("Tools", "Storage zones", ToolStorage);
            Add("Tools", "Build", ToolBuild);
            Add("Tools", "Scent (pheromones)", ToolPheromone);
            Add("Tools", "Rotate / pick scent", Rotate);
            Add("Tools", "Next building / scent", NextBuilding);
            Add("Tools", "Previous building / scent", PrevBuilding);
            Add("Tools", "Possess an ant / let go", Possess);
            Add("Tools", "Possessed ant: pick up / drop", Interact);
            for (int i = 0; i < Abilities.Length; i++) Add("Abilities", $"Ability {i + 1}", Abilities[i]);
            Add("Panels", "Flow overlay", Overlay);
            Add("Panels", "Research", Research);
            Add("Panels", "Castes", Castes);
            Add("Panels", "Genetic Memory", Genetics);
            Add("Panels", "Nuptial Flight", Flight);
            Add("Game", "Menu / back", Menu);
            Add("Game", "Controls and help", Help);
            Add("Game", "Quick save", QuickSave);
            Add("Game", "Quick load", QuickLoad);
            return list.ToArray();
        }

        static int FirstPointerOrKey(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var b = action.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                string path = b.path ?? "";
                if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>")) return i;
            }
            return -1;
        }

        /// <summary>Short name of the key bound to an action (keyboard / mouse), e.g. "F" or "LMB"; empty if none.</summary>
        public static string KeyName(InputAction action)
        {
            int i = FirstPointerOrKey(action);
            return i < 0 ? "" : KeyName(action, i);
        }

        public static string KeyName(InputAction action, int binding)
        {
            string text = action.GetBindingDisplayString(binding, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return text switch
            {
                "Left Button" => "LMB",
                "Right Button" => "RMB",
                "Middle Button" => "MMB",
                "Escape" => "Esc",
                _ => text,
            };
        }

        public string SaveOverrides() => _asset.SaveBindingOverridesAsJson();

        public void LoadOverrides(string json)
        {
            _asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    _asset.LoadBindingOverridesFromJson(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not apply the saved key bindings: {e.Message}");
                }
            }
            BindingsVersion++;
        }

        public void ResetOverrides()
        {
            _asset.RemoveAllBindingOverrides();
            BindingsVersion++;
        }

        /// <summary>Call after an interactive rebind finished.</summary>
        public void BindingsChanged() => BindingsVersion++;

        public Vector2 Pointer => PointerPosition.ReadValue<Vector2>();

        public void Dispose() => _asset.Disable();
    }
}
