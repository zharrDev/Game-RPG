using Godot;

namespace RpgGame;

/// <summary>
/// Kamera mengikuti pemain: limit peta, smoothing, dan offset game feel
/// (shake/hit-stop dari GameFeel). Dipisah dari Game supaya urutan _Process
/// dan komposisi kamera mudah ditelusuri.
/// </summary>
public sealed class CameraRig
{
    public Camera2D Node { get; private set; } = null!;

    public void Build(Node2D parent, Vector2 pixelSize)
    {
        Node = new Camera2D
        {
            Name = "Camera",
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 8,
            LimitLeft = 0,
            LimitTop = 0,
            LimitRight = (int)pixelSize.X,
            LimitBottom = (int)pixelSize.Y,
            Position = new Vector2(0, -12),
        };
        parent.AddChild(Node);
        Node.MakeCurrent();
    }

    /// <summary>Titik tengah layar (termasuk smoothing) untuk FX asap/abu.</summary>
    public Vector2 ScreenCenter => Node.GetScreenCenterPosition();

    public void ApplyOffset(Vector2 offset) => Node.Offset = offset;
}
