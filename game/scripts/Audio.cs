using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// SFX & musik chiptune hasil tools/generate_audio.py. WAV dibaca sebagai byte
/// mentah (sama seperti PNG aset) lalu diubah jadi AudioStreamWav, jadi tidak
/// bergantung pada importer Godot.
/// </summary>
public static class AudioLibrary
{
    public const string ManifestPath = "res://assets/audio/audio.json";
    public const string Folder = "res://assets/audio/";

    private static readonly Dictionary<string, AudioStreamWav> Streams = new();
    private static bool _loaded;

    public static int Count => Streams.Count;
    public static IEnumerable<string> Cues => Streams.Keys;

    public static void LoadAll()
    {
        if (_loaded) return;
        _loaded = true;
        using var doc = Assets.LoadJson(ManifestPath);
        foreach (var group in new[] { "sfx", "music" })
        {
            if (!doc.RootElement.TryGetProperty(group, out var section)) continue;
            foreach (var item in section.EnumerateObject())
            {
                var file = item.Value.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
                if (file.Length == 0) continue;
                var loop = item.Value.TryGetProperty("loop", out var l) && l.GetBoolean();
                var stream = LoadWav($"{Folder}{file}", loop);
                if (stream is not null) Streams[item.Name] = stream;
            }
        }
        GD.Print($"[audio] {Streams.Count} cue dimuat");
    }

    public static AudioStreamWav? Get(string name) =>
        Streams.TryGetValue(name, out var stream) ? stream : null;

    /// <summary>Lepas semua stream (dipanggil saat pemutar audio keluar dari tree)
    /// supaya Godot tidak melaporkan objek tertahan di akhir proses.</summary>
    public static void Clear()
    {
        Streams.Clear();
        _loaded = false;
    }

    /// <summary>Parser WAV minimal: PCM 16-bit mono/stereo (cukup untuk aset kita).</summary>
    public static AudioStreamWav? LoadWav(string resPath, bool loop)
    {
        var bytes = FileAccess.GetFileAsBytes(resPath);
        if (bytes is null || bytes.Length < 44)
        {
            GD.PrintErr($"[audio] file kosong / terlalu kecil: {resPath}");
            return null;
        }
        if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F')
        {
            GD.PrintErr($"[audio] bukan RIFF/WAV: {resPath}");
            return null;
        }

        var channels = 1;
        var rate = 22050;
        var bits = 16;
        var dataOffset = -1;
        var dataLength = 0;

        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            var body = offset + 8;
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, bytes.Length - body);
                break;
            }
            offset = body + size + (size % 2);
        }

        if (dataOffset < 0 || dataLength <= 0 || bits != 16)
        {
            GD.PrintErr($"[audio] format tidak didukung (bits={bits}): {resPath}");
            return null;
        }

        var data = new byte[dataLength];
        Array.Copy(bytes, dataOffset, data, 0, dataLength);
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = channels == 2,
            Data = data,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = dataLength / (channels * 2);
        }
        return stream;
    }
}

/// <summary>Pemutar SFX (pool 8 suara) + satu kanal musik loop.</summary>
public sealed partial class AudioPlayer : Node
{
    private const int Voices = 8;

    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Voices];
    private readonly AudioStreamPlayer _music;
    private int _next;
    private string _musicCue = "";

    /// <summary>Cue SFX terakhir yang diputar (dipakai tes headless).</summary>
    public string LastCue { get; private set; } = "";
    public string MusicCue => _musicCue;

    public AudioPlayer()
    {
        Name = "Audio";
        AudioLibrary.LoadAll();
        for (var i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = $"Sfx{i}", VolumeDb = -8f };
            AddChild(_voices[i]);
        }
        _music = new AudioStreamPlayer { Name = "Music", VolumeDb = -16f };
        AddChild(_music);
    }

    /// <summary>Hentikan semua suara (dipakai sebelum keluar supaya audio server
    /// tidak menyimpan referensi saat SceneTree dibongkar).</summary>
    public void StopAll()
    {
        _music.Stop();
        _music.Stream = null;
        _musicCue = "";
        foreach (var voice in _voices)
        {
            voice.Stop();
            voice.Stream = null;
        }
    }

    public override void _ExitTree()
    {
        StopAll();
        AudioLibrary.Clear();
    }

    public bool Play(string cue, float volumeDb = -8f)
    {
        var stream = AudioLibrary.Get(cue);
        if (stream is null) return false;
        var voice = _voices[_next];
        _next = (_next + 1) % Voices;
        voice.Stream = stream;
        voice.VolumeDb = volumeDb;
        voice.Play();
        LastCue = cue;
        return true;
    }

    public bool PlayMusic(string cue)
    {
        if (cue == _musicCue) return false;
        var stream = AudioLibrary.Get(cue);
        if (stream is null) return false;
        _musicCue = cue;
        _music.Stream = stream;
        _music.Play();
        return true;
    }
}
