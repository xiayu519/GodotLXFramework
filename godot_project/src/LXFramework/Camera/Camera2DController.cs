using LX.Core.Lifetime;
using Godot;

namespace LX.Camera;

/// <summary>单个 Camera2D 的跟随、边界和震动参数。</summary>
public sealed record Camera2DFollowOptions
{
    /// <summary>目标位置附加的世界坐标偏移。</summary>
    public Vector2 TargetOffset { get; init; }

    /// <summary>相机中心周围不触发移动的世界坐标矩形尺寸。</summary>
    public Vector2 DeadZoneSize { get; init; }

    /// <summary>指数平滑速度；0 表示立即移动到目标位置。</summary>
    public float SmoothingSpeed { get; init; } = 8;

    internal void Validate()
    {
        if (!TargetOffset.IsFinite() ||
            !DeadZoneSize.IsFinite() ||
            DeadZoneSize.X < 0 ||
            DeadZoneSize.Y < 0 ||
            !float.IsFinite(SmoothingSpeed) ||
            SmoothingSpeed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Camera2DFollowOptions),
                "Camera follow offsets, dead-zone size and smoothing speed must be finite and non-negative where applicable.");
        }
    }
}

/// <summary>
/// 绑定到一个具体 Camera2D 的局部控制器。不同相机使用不同实例，因此不需要全局当前相机。
/// 控制器拥有相机的 GlobalPosition 和 Offset；外部需要改变基础 Offset 时使用 BaseOffset。
/// </summary>
/// <remarks>TODO: 3D 游戏应提供独立的 Camera3DController，不在此类型中混合 2D/3D 语义。</remarks>
public sealed partial class Camera2DController : Node
{
    private Camera2D _camera = null!;
    private Node2D? _target;
    private Camera2DFollowOptions _follow = new();
    private Rect2? _centerBounds;
    private Vector2 _baseOffset;
    private float _shakeAmplitude;
    private double _shakeDuration;
    private double _shakeElapsed;
    private float _shakeFrequency;
    private uint _shakeSequence;
    private int _mainThreadId;
    private bool _attached;

    /// <summary>当前控制的 Camera2D。</summary>
    public Camera2D Camera => _camera;

    /// <summary>震动叠加前的 Camera2D.Offset。</summary>
    public Vector2 BaseOffset
    {
        get => _baseOffset;
        set
        {
            EnsureMainThread();
            if (!value.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _baseOffset = value;
            if (_shakeDuration <= 0)
            {
                _camera.Offset = value;
            }
        }
    }

    /// <summary>当前跟随目标；null 表示只保留边界和震动处理。</summary>
    public Node2D? FollowTarget => _target;

    /// <summary>当前相机中心点边界；null 表示不限制。</summary>
    public Rect2? CenterBounds => _centerBounds;

    /// <summary>为传入相机创建唯一控制器，并把控制器回收绑定到指定生命周期。</summary>
    public static Camera2DController Attach(Camera2D camera, LifetimeScope lifetime)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.ThrowIfDisposed();
        var childCount = camera.GetChildCount();
        for (var index = 0; index < childCount; index++)
        {
            if (camera.GetChild(index) is Camera2DController)
            {
                throw new InvalidOperationException(
                    $"Camera2D '{camera.Name}' already has a Camera2DController.");
            }
        }

        var controller = new Camera2DController
        {
            Name = "LXCamera2DController",
            _camera = camera,
            _baseOffset = camera.Offset,
            _mainThreadId = System.Environment.CurrentManagedThreadId,
            _attached = true,
        };
        camera.AddChild(controller);
        lifetime.Defer(controller.Detach);
        return controller;
    }

    /// <summary>开始或更新目标跟随。</summary>
    public void Follow(Node2D target, Camera2DFollowOptions? options = null)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(target);
        options ??= new Camera2DFollowOptions();
        options.Validate();
        _target = target;
        _follow = options;
    }

    /// <summary>停止目标跟随，不改变相机当前位置。</summary>
    public void ClearFollow()
    {
        EnsureMainThread();
        _target = null;
    }

    /// <summary>立即把相机中心移动到当前目标经过死区、偏移和边界计算后的位置。</summary>
    public void SnapToTarget()
    {
        EnsureMainThread();
        if (TryGetDesiredPosition(out var desired))
        {
            _camera.GlobalPosition = desired;
        }
    }

    /// <summary>限制相机中心点的世界坐标范围；传入 null 清除边界。</summary>
    public void SetCenterBounds(Rect2? bounds)
    {
        EnsureMainThread();
        if (bounds is { } value &&
            (!value.Position.IsFinite() ||
             !value.Size.IsFinite() ||
             value.Size.X < 0 ||
             value.Size.Y < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }
        _centerBounds = bounds;
        _camera.GlobalPosition = ClampToBounds(_camera.GlobalPosition);
    }

    /// <summary>以新的衰减震动替换当前震动；振幅单位为像素，频率单位为 Hz。</summary>
    public void Shake(float amplitude, TimeSpan duration, float frequency = 24)
    {
        EnsureMainThread();
        if (!float.IsFinite(amplitude) || amplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        if (!float.IsFinite(frequency) || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        _shakeAmplitude = amplitude;
        _shakeDuration = duration.TotalSeconds;
        _shakeElapsed = 0;
        _shakeFrequency = frequency;
        _shakeSequence++;
        if (amplitude == 0 || duration == TimeSpan.Zero)
        {
            StopShake();
        }
    }

    /// <summary>立即停止震动并恢复 BaseOffset。</summary>
    public void StopShake()
    {
        EnsureMainThread();
        _shakeAmplitude = 0;
        _shakeDuration = 0;
        _shakeElapsed = 0;
        _camera.Offset = _baseOffset;
    }

    public override void _Process(double delta)
    {
        if (!_attached || !GodotObject.IsInstanceValid(_camera))
        {
            SetProcess(false);
            return;
        }

        if (TryGetDesiredPosition(out var desired))
        {
            var speed = _follow.SmoothingSpeed;
            var weight = speed <= 0
                ? 1
                : 1 - Math.Exp(-speed * Math.Max(0, delta));
            _camera.GlobalPosition = _camera.GlobalPosition.Lerp(desired, (float)weight);
        }
        else
        {
            _camera.GlobalPosition = ClampToBounds(_camera.GlobalPosition);
        }

        UpdateShake(Math.Max(0, delta));
    }

    private bool TryGetDesiredPosition(out Vector2 desired)
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target))
        {
            _target = null;
            desired = default;
            return false;
        }

        var current = _camera.GlobalPosition;
        var target = _target.GlobalPosition + _follow.TargetOffset;
        var delta = target - current;
        var halfDeadZone = _follow.DeadZoneSize * 0.5f;
        var adjustment = new Vector2(
            AxisAdjustment(delta.X, halfDeadZone.X),
            AxisAdjustment(delta.Y, halfDeadZone.Y));
        desired = ClampToBounds(current + adjustment);
        return true;
    }

    private Vector2 ClampToBounds(Vector2 position)
    {
        if (_centerBounds is not { } bounds)
        {
            return position;
        }

        return new Vector2(
            Math.Clamp(position.X, bounds.Position.X, bounds.End.X),
            Math.Clamp(position.Y, bounds.Position.Y, bounds.End.Y));
    }

    private void UpdateShake(double delta)
    {
        if (_shakeDuration <= 0 || _shakeAmplitude <= 0)
        {
            _camera.Offset = _baseOffset;
            return;
        }

        _shakeElapsed = Math.Min(_shakeDuration, _shakeElapsed + delta);
        var remaining = (float)(1 - _shakeElapsed / _shakeDuration);
        var phase = _shakeElapsed * _shakeFrequency * Math.Tau;
        var seed = _shakeSequence * 0.754877666;
        var sample = new Vector2(
            (float)Math.Sin(phase * 1.371 + seed),
            (float)Math.Cos(phase * 1.917 + seed * 1.618));
        _camera.Offset = _baseOffset + sample * (_shakeAmplitude * remaining);
        if (_shakeElapsed >= _shakeDuration)
        {
            StopShake();
        }
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (GodotObject.IsInstanceValid(_camera))
        {
            _camera.Offset = _baseOffset;
        }
        if (GodotObject.IsInstanceValid(this))
        {
            SetProcess(false);
            if (!IsQueuedForDeletion())
            {
                QueueFree();
            }
        }
    }

    private void EnsureMainThread()
    {
        if (!_attached || System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException(
                "Camera2DController operations require an attached controller on the Godot main thread.");
        }
    }

    private static float AxisAdjustment(float delta, float halfDeadZone)
    {
        if (delta > halfDeadZone)
        {
            return delta - halfDeadZone;
        }
        if (delta < -halfDeadZone)
        {
            return delta + halfDeadZone;
        }
        return 0;
    }
}
