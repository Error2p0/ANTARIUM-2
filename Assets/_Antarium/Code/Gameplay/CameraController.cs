using Antarium.Presentation;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>Maps input to the pixel camera: keyboard/stick pan, drag pan, scroll zoom at the cursor.</summary>
    public sealed class CameraController
    {
        readonly PixelCamera _camera;
        readonly GameInput _input;
        const float EdgePixels = 6f;

        /// <summary>Player setting: pan when the pointer touches the edge of the screen.</summary>
        public bool EdgePan;

        public CameraController(PixelCamera camera, GameInput input)
        {
            _camera = camera;
            _input = input;
        }

        /// <param name="allowPan">False while the player controls an ant: the pan keys move the ant and the camera follows it.</param>
        public void Update(float deltaTime, bool allowPan = true)
        {
            Vector2 pan = _input.Pan.ReadValue<Vector2>();
            if (allowPan && EdgePan && Application.isFocused) pan += EdgeDirection();
            if (allowPan && pan.sqrMagnitude > 0f)
                _camera.PanWorld((float2)pan * _camera.PanSpeed * deltaTime);

            if (allowPan && _input.DragPan.IsPressed())
                _camera.PanScreenPixels(_input.PointerDelta.ReadValue<Vector2>());

            float scroll = _input.Zoom.ReadValue<float>();
            if (math.abs(scroll) > 0.01f && PointerInsideScreen())
                _camera.ZoomBy((int)math.sign(scroll), _input.Pointer);
        }

        Vector2 EdgeDirection()
        {
            var p = _input.Pointer;
            if (!PointerInsideScreen()) return Vector2.zero;
            return new Vector2(p.x < EdgePixels ? -1f : p.x >= Screen.width - EdgePixels ? 1f : 0f,
                p.y < EdgePixels ? -1f : p.y >= Screen.height - EdgePixels ? 1f : 0f);
        }

        bool PointerInsideScreen()
        {
            var p = _input.Pointer;
            return p.x >= 0 && p.y >= 0 && p.x < Screen.width && p.y < Screen.height;
        }
    }
}
