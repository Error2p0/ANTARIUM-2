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

        public CameraController(PixelCamera camera, GameInput input)
        {
            _camera = camera;
            _input = input;
        }

        public void Update(float deltaTime)
        {
            Vector2 pan = _input.Pan.ReadValue<Vector2>();
            if (pan.sqrMagnitude > 0f)
                _camera.PanWorld((float2)pan * _camera.PanSpeed * deltaTime);

            if (_input.DragPan.IsPressed())
                _camera.PanScreenPixels(_input.PointerDelta.ReadValue<Vector2>());

            float scroll = _input.Zoom.ReadValue<float>();
            if (math.abs(scroll) > 0.01f && PointerInsideScreen())
                _camera.ZoomBy((int)math.sign(scroll), _input.Pointer);
        }

        bool PointerInsideScreen()
        {
            var p = _input.Pointer;
            return p.x >= 0 && p.y >= 0 && p.x < Screen.width && p.y < Screen.height;
        }
    }
}
