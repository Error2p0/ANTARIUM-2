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
        public readonly InputAction Rotate;
        public readonly InputAction NextBuilding;
        public readonly InputAction PrevBuilding;
        public readonly InputAction Overlay;
        public readonly InputAction ToggleHud;
        public readonly InputAction SpawnAnts;
        public readonly InputAction FinishBuilds;
        public readonly InputAction GiveItems;

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
            Rotate = asset.FindAction("Tools/Rotate", true);
            NextBuilding = asset.FindAction("Tools/NextBuilding", true);
            PrevBuilding = asset.FindAction("Tools/PrevBuilding", true);
            Overlay = asset.FindAction("View/Overlay", true);
            ToggleHud = asset.FindAction("Debug/ToggleHud", true);
            SpawnAnts = asset.FindAction("Debug/SpawnAnts", true);
            FinishBuilds = asset.FindAction("Debug/FinishBuilds", true);
            GiveItems = asset.FindAction("Debug/GiveItems", true);
            asset.Enable();
        }

        public Vector2 Pointer => PointerPosition.ReadValue<Vector2>();

        public void Dispose() => _asset.Disable();
    }
}
