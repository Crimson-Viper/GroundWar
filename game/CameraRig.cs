using Godot;

namespace GroundWar.Game;

/// <summary>
/// Free RTS camera. WASD / arrows pan, Q/E rotate, mouse wheel zooms, right-drag orbits,
/// middle-drag pans, Shift moves faster. Can follow a soldier.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Camera { get; private set; } = null!;
    public Node3D? FollowTarget { get; set; }
    /// <summary>How far the camera is from the point it looks at (the map table fades in when far out).</summary>
    public float Distance => _distance;

    // A smooth flight to a new view (the battle cam and the jump to the map table).
    private bool _flying;
    private Vector3 _flyFrom, _flyTo;
    private float _flyYaw0, _flyYaw1, _flyPitch0, _flyPitch1, _flyDist0, _flyDist1, _flyT, _flyTime;

    private Node3D _pitchNode = null!;
    private float _yaw;
    private float _pitch = Mathf.DegToRad(-55f);
    private float _distance = 160f;
    private float _targetDistance = 160f;
    private Vector2 _bounds = new(300, 200);

    private const float MinDistance = 4f;
    private const float MaxDistance = 520f;

    public override void _Ready()
    {
        _pitchNode = new Node3D { Name = "Pitch" };
        AddChild(_pitchNode);
        Camera = new Camera3D { Name = "Camera", Fov = 50f, Far = 3000f, Near = 0.1f };
        _pitchNode.AddChild(Camera);
        ApplyTransform();
    }

    /// <summary>Look at the whole map from the south-west, as at the start of a battle.</summary>
    public void Frame(float width, float height)
    {
        _bounds = new Vector2(width, height);
        Position = new Vector3(width * 0.5f, 0, height * 0.55f);
        _yaw = 0f;
        _pitch = Mathf.DegToRad(-50f);
        _distance = _targetDistance = Mathf.Max(width, height) * 0.6f;
        FollowTarget = null;
        ApplyTransform();
    }

    /// <summary>Jump the camera to look at a point from a given distance.</summary>
    public void LookAt(Vector3 focus, float distance, float yawDegrees = 0f, float pitchDegrees = -40f)
    {
        Position = new Vector3(focus.X, 0, focus.Z);
        _distance = _targetDistance = distance;
        _yaw = Mathf.DegToRad(yawDegrees);
        _pitch = Mathf.DegToRad(pitchDegrees);
        ApplyTransform();
    }

    /// <summary>Fly smoothly to look at a point from a given distance and angle.</summary>
    public void FlyTo(Vector3 focus, float distance, float yawDegrees, float pitchDegrees, float seconds = 1.5f)
    {
        _flying = true;
        _flyFrom = Position; _flyTo = new Vector3(focus.X, 0, focus.Z);
        _flyYaw0 = _yaw;
        // Turn the short way round.
        _flyYaw1 = _yaw + Mathf.Wrap(Mathf.DegToRad(yawDegrees) - _yaw, -Mathf.Pi, Mathf.Pi);
        _flyPitch0 = _pitch; _flyPitch1 = Mathf.DegToRad(pitchDegrees);
        _flyDist0 = _distance; _flyDist1 = distance;
        _flyT = 0f; _flyTime = Mathf.Max(0.1f, seconds);
    }

    /// <summary>Straight up above the middle of the map: the war-room table.</summary>
    public void ToTable() => FlyTo(new Vector3(_bounds.X * 0.5f, 0, _bounds.Y * 0.5f), Mathf.Max(_bounds.X, _bounds.Y) * 1.1f + 60f, 0f, -80f, 1.2f);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_flying)
        {
            _flyT += dt / _flyTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Min(_flyT, 1f));
            var target = FollowTarget != null && IsInstanceValid(FollowTarget) ? new Vector3(FollowTarget.GlobalPosition.X, 0, FollowTarget.GlobalPosition.Z) : _flyTo;
            Position = _flyFrom.Lerp(target, k);
            _yaw = Mathf.Lerp(_flyYaw0, _flyYaw1, k);
            _pitch = Mathf.Lerp(_flyPitch0, _flyPitch1, k);
            _distance = _targetDistance = Mathf.Lerp(_flyDist0, _flyDist1, k);
            if (_flyT >= 1f) _flying = false;
            ApplyTransform();
            return;
        }
        bool fast = Input.IsKeyPressed(Key.Shift);
        float panSpeed = (20f + _distance * 0.9f) * (fast ? 3f : 1f);

        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector3.Zero)
        {
            FollowTarget = null;
            Position += move.Normalized().Rotated(Vector3.Up, _yaw) * panSpeed * dt;
        }
        if (Input.IsKeyPressed(Key.Q)) _yaw += 1.5f * dt;
        if (Input.IsKeyPressed(Key.E)) _yaw -= 1.5f * dt;
        if (Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.KpAdd)) _targetDistance *= 1f - 1.5f * dt;
        if (Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KpSubtract)) _targetDistance *= 1f + 1.5f * dt;

        if (FollowTarget != null && IsInstanceValid(FollowTarget))
        {
            var p = FollowTarget.GlobalPosition;
            Position = Position.Lerp(new Vector3(p.X, 0, p.Z), Mathf.Min(1f, 6f * dt));
        }

        _targetDistance = Mathf.Clamp(_targetDistance, MinDistance, MaxDistance);
        _distance = Mathf.Lerp(_distance, _targetDistance, Mathf.Min(1f, 10f * dt));
        var pos = Position;
        pos.X = Mathf.Clamp(pos.X, -100f, _bounds.X + 100f);
        pos.Z = Mathf.Clamp(pos.Z, -100f, _bounds.Y + 100f);
        Position = pos;
        ApplyTransform();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _targetDistance *= 0.88f;
            else if (mb.ButtonIndex == MouseButton.WheelDown) _targetDistance *= 1.14f;
        }
        else if (e is InputEventMouseMotion mm)
        {
            if ((mm.ButtonMask & MouseButtonMask.Right) != 0)
            {
                _yaw -= mm.Relative.X * 0.005f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.005f, Mathf.DegToRad(-89f), Mathf.DegToRad(-8f));
            }
            else if ((mm.ButtonMask & MouseButtonMask.Middle) != 0)
            {
                FollowTarget = null;
                float scale = _distance * 0.0025f;
                Position += new Vector3(-mm.Relative.X * scale, 0, -mm.Relative.Y * scale).Rotated(Vector3.Up, _yaw);
            }
        }
    }

    private void ApplyTransform()
    {
        if (_pitchNode == null) return;
        // The point the camera looks at rides on the ground, so zooming in on a hilltop doesn't end up inside it.
        Position = new Vector3(Position.X, Draw.GroundAt(Position), Position.Z);
        Rotation = new Vector3(0, _yaw, 0);
        _pitchNode.Rotation = new Vector3(_pitch, 0, 0);
        Camera.Position = new Vector3(0, 0, _distance);
    }

    /// <summary>Where the mouse points on the ground, if anywhere.</summary>
    public Vector3? GroundPointUnderMouse(Vector2 screenPos)
    {
        var origin = Camera.ProjectRayOrigin(screenPos);
        var dir = Camera.ProjectRayNormal(screenPos);
        if (Mathf.Abs(dir.Y) < 1e-4f) return null;
        float t = -origin.Y / dir.Y;
        if (t < 0) return null;
        // On hilly ground: step the hit point onto the surface (a few rounds is plenty for gentle slopes).
        for (int i = 0; i < 6 && Draw.Ground != null; i++)
        {
            var p = origin + dir * t;
            t = Mathf.Max(0f, (Draw.GroundAt(p) - origin.Y) / dir.Y);
        }
        return origin + dir * t;
    }
}
