using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Comms;

namespace GroundWar.Game;

/// <summary>
/// Battle sound (milestone 6): rifle and machine-gun shots, tank guns and shell bursts placed in 3D where they
/// happen, a radio squelch when one of your messages gets through, and a far-off battle rumble that swells with
/// the amount of shooting. The sounds are placeholders made by tools/make_sounds.py; swap in recordings with the
/// same names in sounds/. M mutes. See docs/systems/spectacle.md.
/// </summary>
public partial class SoundView : Node3D
{
    private const int Voices = 24;
    private readonly Battle _battle;
    private readonly Camera3D _camera;
    private readonly List<AudioStreamPlayer3D> _voices = new();
    private int _next;
    private AudioStream? _rifle, _mg, _tankGun, _explosion, _radio;
    private AudioStreamPlayer? _ambient, _ui;
    private float _intensity;
    private readonly HashSet<int> _heard = new();

    public bool Muted { get; set; }

    public SoundView(Battle battle, Camera3D camera) { _battle = battle; _camera = camera; }

    public override void _Ready()
    {
        _rifle = Load("rifle"); _mg = Load("mg"); _tankGun = Load("tank_gun"); _explosion = Load("explosion"); _radio = Load("radio");
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 25f, MaxDistance = 900f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Logarithmic, MaxPolyphony = 1 };
            AddChild(p);
            _voices.Add(p);
        }
        if (Load("distant") is AudioStreamWav distant)
        {
            distant.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            distant.LoopEnd = distant.Data.Length / 2; // 16-bit mono: two bytes a sample
            _ambient = new AudioStreamPlayer { Stream = distant, VolumeDb = -40f };
            AddChild(_ambient);
            _ambient.Play();
        }
        _ui = new AudioStreamPlayer { Stream = _radio, VolumeDb = -12f };
        AddChild(_ui);
        // Messages already delivered when the view starts (a jump in a replay) don't squelch.
        foreach (var m in _battle.Messages) if (m.Status != MessageStatus.InTransit) _heard.Add(m.Id);
    }

    private static AudioStream? Load(string name)
    {
        string path = $"res://sounds/{name}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }

    /// <summary>After each simulation step: sound for this tick's shots. Busy ticks are thinned out, keeping the
    /// shots nearest the camera, so a big firefight is a roar rather than a wall of noise.</summary>
    public void AfterStep(float gameSpeed)
    {
        var shots = _battle.Shots;
        _intensity += shots.Count;
        if (Muted || shots.Count == 0 || gameSpeed > 4f) return;
        var cam = _camera.GlobalPosition;
        int budget = gameSpeed > 2f ? 1 : 3;
        // Big guns always; small arms nearest first.
        foreach (var s in shots)
        {
            if (!s.Shell) continue;
            // A tank gun's boom, or (pitched up and quieter) the whoosh of a bazooka; the burst comes when it lands.
            bool tank = _battle.GetSoldier(s.ShooterId)?.IsTank == true;
            Play(_tankGun, Draw.To3(s.From, 1.5f), tank ? 0f : -6f, tank ? 0.9f : 1.6f, tank ? 1.05f : 1.8f);
        }
        int played = 0;
        foreach (int i in NearestFirst(shots, cam))
        {
            if (played++ >= budget) break;
            var s = shots[i];
            if (s.Shell) continue;
            Play(s.MachineGun ? _mg : _rifle, Draw.To3(s.From, 1.2f), s.MachineGun ? -4f : -2f, 0.92f, 1.1f);
        }
    }

    private static IEnumerable<int> NearestFirst(IReadOnlyList<ShotEvent> shots, Vector3 cam)
    {
        var order = new List<int>(shots.Count);
        for (int i = 0; i < shots.Count; i++) order.Add(i);
        order.Sort((a, b) => Draw.To3(shots[a].From).DistanceSquaredTo(cam).CompareTo(Draw.To3(shots[b].From).DistanceSquaredTo(cam)));
        return order;
    }

    /// <summary>A shell landing (Godot calls this when the shell's tracer arrives).</summary>
    public void Burst(Vector3 at)
    {
        if (!Muted) Play(_explosion, at, 2f, 0.85f, 1.1f);
    }

    private void Play(AudioStream? stream, Vector3 at, float volumeDb, float pitchMin, float pitchMax)
    {
        if (stream == null) return;
        var v = _voices[_next];
        _next = (_next + 1) % _voices.Count;
        v.Stream = stream;
        v.GlobalPosition = at;
        v.VolumeDb = volumeDb;
        v.PitchScale = (float)GD.RandRange(pitchMin, pitchMax);
        v.Play();
    }

    public void UpdateAudio(float realDelta, GroundWar.Sim.Units.Side playerSide)
    {
        // The rumble follows how much shooting there has been lately.
        _intensity *= Mathf.Exp(-realDelta / 3f);
        if (_ambient != null) _ambient.VolumeDb = Muted ? -80f : Mathf.Lerp(-34f, -8f, Mathf.Clamp(_intensity / 60f, 0f, 1f));
        foreach (var m in _battle.Messages)
        {
            if (m.Status != MessageStatus.Delivered || _heard.Contains(m.Id)) continue;
            _heard.Add(m.Id);
            // Only battalion's own traffic, and only reports and brief-backs coming in.
            if (!Muted && m.Side == playerSide && m.Company == null && !m.Down && _ui != null && !_ui.Playing) _ui.Play();
        }
    }
}
