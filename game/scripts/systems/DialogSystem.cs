using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Dialog NPC: baris percakapan, hadiah quest saat target tercapai, dan
/// penutupan yang merapikan penanda "!" warga. Permainan dijeda selama
/// dialog terbuka (dicek lewat properti Open di Game._Process).
/// </summary>
public sealed class DialogSystem
{
    private readonly Game _game;
    private readonly List<string> _lines = new();
    private Npc? _npc;
    private int _index;

    public DialogSystem(Game game) => _game = game;

    public bool Open => _lines.Count > 0;
    public string Line => _index < _lines.Count ? _lines[_index] : "";
    public string? TalkingTo => _npc?.Info.Id;

    public void OpenWith(NpcInfo info)
    {
        var game = _game;
        if (game.Player is null) return;
        _lines.Clear();
        _lines.AddRange(info.Lines);
        var quest = info.Quest;
        if (quest is not null && quest.Flag.Length > 0)
        {
            if (!game.Player.State.Flags.GetValueOrDefault(quest.Flag))
            {
                var reached = game.Player.State.Defeated.GetValueOrDefault(quest.Target) >= quest.Count;
                if (reached)
                {
                    _lines.Add(quest.DoneLine);
                    GrantQuestReward(quest, info);
                }
                else
                {
                    _lines.Add(quest.Intro);
                }
            }
            else
            {
                _lines.Add("Terima kasih sudah menolong desa ini.");
            }
        }

        _npc = game.Npcs.FirstOrDefault(n => n.Info.Id == info.Id);
        _npc?.Face(game.Player.Position);
        _index = 0;
        if (_lines.Count == 0)
        {
            game.Hud?.Toast($"{info.Name} hanya tersenyum.");
            return;
        }
        ShowLine();
    }

    private void GrantQuestReward(NpcQuestSpec quest, NpcInfo giver)
    {
        _game.Progress.CompleteQuest(quest, giver);
        _game.Spawn.RefreshNpcMarkers();
    }

    private void ShowLine()
    {
        if (_npc is null) return;
        _game.Hud?.ShowDialog($"{_npc.Info.Name} — {_npc.Info.Role}", Line);
    }

    public void Advance()
    {
        if (!Open) return;
        _index++;
        if (_index >= _lines.Count) Close();
        else ShowLine();
    }

    public void Close()
    {
        _lines.Clear();
        _npc = null;
        _index = 0;
        _game.Hud?.HideDialog();
        _game.Spawn.RefreshNpcMarkers();
    }
}
