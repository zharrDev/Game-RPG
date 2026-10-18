using System;
using Godot;

namespace RpgGame;

/// <summary>
/// Jam dunia dan denyut visual lingkungan: siklus siang/malam (via Lighting),
/// kedip obor/lentera, dan fase riak air. Semua murni visual — tidak menyentuh
/// state permainan, jadi aman untuk refactor.
/// </summary>
public sealed class TimeSystem
{
    private double _waterTimer;

    public Lighting? Lighting { get; private set; }

    public void Configure(Lighting lighting) => Lighting = lighting;

    /// <summary>Dipanggil sekali per frame dari Game._Process.</summary>
    public void Advance(double delta, World world)
    {
        // jam dunia berjalan (ambient + kedip cahaya api); Fx.NightAmount ikut terisi
        Lighting?.Advance(delta);

        // obor & lentera berkedip hangat (ambience desa/reruntuhan)
        if (world.FlickerProps.Count > 0)
        {
            var seconds = (float)Time.GetTicksMsec() / 1000f;
            foreach (var (sprite, phase) in world.FlickerProps)
            {
                var pulse = 0.82f + 0.18f * MathF.Sin(seconds * 6.5f + phase) * MathF.Sin(seconds * 2.7f + phase * 1.7f);
                sprite.Modulate = new Color(pulse, pulse * 0.94f, pulse * 0.78f, 1f);
            }
        }

        // riak air: tukar fase tile tiap 0.6 detik (shoreline & deep water)
        _waterTimer += delta;
        if (_waterTimer >= 0.6)
        {
            _waterTimer = 0;
            world.SetWaterPhase(world.WaterPhase + 1);
        }
    }
}
