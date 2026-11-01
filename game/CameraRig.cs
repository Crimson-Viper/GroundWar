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

    private Node3D _pitchNode = null!;
    private float _yaw;
    private float _pitch = Mathf.DegToRad(-55f);
    private float _distance = 160f;
    private float _targetDistance = 160f;
    private Vector2 _bounds = new(300, 200);

    private const float MinDistance = 4f;
    private const float MaxDistance = 450f;

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

    public override void _Process(double delta)
    {
        float dt = (float)delta;
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
        Rotation = new Vector3(0, _yaw, 0);
        _pitchNode.Rotation = new Vector3(_pitch, 0, 0);
        Camera.Position = new Vector3(0, 0, _distance);
    }

    /// <summary>Where the mouse points on the ground (y = 0), if anywhere.</summary>
    public Vector3? GroundPointUnderMouse(Vector2 screenPos)
    {
        var origin = Camera.ProjectRayOrigin(screenPos);
        var dir = Camera.ProjectRayNormal(screenPos);
        if (Mathf.Abs(dir.Y) < 1e-4f) return null;
        float t = -origin.Y / dir.Y;
        if (t < 0) return null;
        return origin + dir * t;
    }
}
