using Godot;

namespace RpgGame;

/// <summary>Hujan layar penuh (M4): garis miring deterministik, makin lebat saat hujan.</summary>
public sealed partial class RainOverlay : Control
{
    private const int MaxDrops = 110;

    private float _amount;
    private double _time;

    public void SetAmount(float amount)
    {
        _amount = Mathf.Clamp(amount, 0f, 1f);
        Visible = _amount > 0.01f;
    }

    public override void _Process(double delta)
    {
        if (_amount <= 0.01f) return;
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_amount <= 0.01f) return;
        var count = (int)(MaxDrops * _amount);
        var color = new Color(0.72f, 0.82f, 0.95f, 0.30f * _amount);
        for (var i = 0; i < count; i++)
        {
            var x = Hash01(i * 2 + 1) * 520f;
            var speed = 220f + Hash01(i * 2 + 2) * 180f;
            var y = (Hash01(i * 7 + 3) * 300f + (float)_time * speed) % 300f - 15f;
            var length = 7f + Hash01(i * 13 + 5) * 7f;
            x = (x + (float)_time * 26f) % 520f - 20f;
            DrawLine(new Vector2(x, y), new Vector2(x + 3.2f, y + length), color, 1f);
        }
    }

    private static float Hash01(int value)
    {
        var x = (uint)(value * 2654435761u);
        x ^= x >> 15;
        x *= 2246822519u;
        x ^= x >> 13;
        return (x & 0xFFFFFFu) / 16777216f;
    }
}
