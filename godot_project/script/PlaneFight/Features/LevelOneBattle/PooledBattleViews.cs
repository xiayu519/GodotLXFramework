using Godot;

namespace PlaneFight.Features.LevelOneBattle;

internal sealed partial class BattleProjectileView : Node2D
{
    private static readonly Vector2[] HomingShape =
    [
        new(0, -1.8f),
        new(1, 1),
        new(0, 0.55f),
        new(-1, 1),
    ];

    private static readonly Vector2[] BulletShape =
    [
        new(-0.55f, -1.8f),
        new(0.55f, -1.8f),
        new(0.75f, 1.8f),
        new(-0.75f, 1.8f),
    ];

    private readonly Polygon2D _polygon;
    private readonly Sprite2D _sprite;

    public BattleProjectileView()
    {
        ZIndex = 4;
        _polygon = new Polygon2D { Visible = false };
        _sprite = new Sprite2D { Centered = true, Visible = false };
        AddChild(_polygon);
        AddChild(_sprite);
    }

    public void Configure(
        string name,
        Vector2 position,
        Vector2 velocity,
        float radius,
        bool homing,
        Color color,
        Texture2D? texture,
        float textureScale)
    {
        Name = name;
        Position = position;
        Rotation = velocity.Angle() + (texture is null ? Mathf.Pi / 2 : 0);
        if (texture is null)
        {
            _polygon.Polygon = homing ? HomingShape : BulletShape;
            _polygon.Scale = Vector2.One * radius;
            _polygon.Color = color;
            _polygon.Visible = true;
            _sprite.Visible = false;
            _sprite.Texture = null;
            return;
        }

        _polygon.Visible = false;
        _sprite.Texture = texture;
        _sprite.Scale = Vector2.One * textureScale;
        _sprite.Visible = true;
    }

    public void ResetForPool()
    {
        Name = "PooledProjectile";
        Position = Vector2.Zero;
        Rotation = 0;
        _polygon.Visible = false;
        _sprite.Visible = false;
        _sprite.Texture = null;
    }
}

internal sealed partial class BattleTrailView : Line2D
{
    public BattleTrailView()
    {
        Name = "ProjectileTrail";
        Antialiased = true;
        ZIndex = 3;
        Gradient = new Gradient { Offsets = [0, 1] };
    }

    public void Configure(Color color, float width)
    {
        Width = width;
        Gradient.Colors = [new Color(color.R, color.G, color.B, 0), color];
        ClearPoints();
    }

    public void ResetForPool()
    {
        ClearPoints();
        Width = 0;
    }
}

internal sealed partial class BattlePickupView : Node2D
{
    private readonly Sprite2D _sprite;
    private readonly Sprite2D _healPlus;

    public BattlePickupView()
    {
        ZIndex = 3;
        _sprite = new Sprite2D { Centered = true };
        _healPlus = new Sprite2D
        {
            Centered = true,
            Position = new Vector2(24, -24),
            Scale = Vector2.One * 0.7f,
            Visible = false,
        };
        AddChild(_sprite);
        AddChild(_healPlus);
    }

    public Sprite2D Sprite => _sprite;

    public void Configure(string id, Vector2 position, Texture2D texture, Texture2D healPlusTexture)
    {
        Name = $"Pickup_{id}";
        Position = position;
        Rotation = 0;
        _sprite.Texture = texture;
        _sprite.Scale = Vector2.One * (id == "medal" ? 0.92f : 0.82f);
        _healPlus.Texture = id == "heal" ? healPlusTexture : null;
        _healPlus.Visible = id == "heal";
    }

    public void ResetForPool()
    {
        Name = "PooledPickup";
        Position = Vector2.Zero;
        Rotation = 0;
        _sprite.Texture = null;
        _healPlus.Texture = null;
        _healPlus.Visible = false;
    }
}

internal sealed partial class BattleEnemyView : Node2D
{
    private readonly Sprite2D _sprite;
    private readonly Sprite2D _frozenOverlay;
    private float _frozenOverlayScale;

    public BattleEnemyView()
    {
        ZIndex = 2;
        _sprite = new Sprite2D { Centered = true };
        _frozenOverlay = new Sprite2D
        {
            Name = "FrozenOverlay",
            Centered = true,
            Modulate = new Color(0.72f, 0.9f, 1f, 0.52f),
            ZIndex = 1,
            Visible = false,
        };
        AddChild(_sprite);
        AddChild(_frozenOverlay);
    }

    public Sprite2D Sprite => _sprite;

    public void Configure(
        string id,
        Vector2 position,
        Texture2D texture,
        float scale,
        Texture2D frozenTexture,
        float frozenOverlayScale,
        bool isBoss)
    {
        Name = id;
        Position = position;
        Rotation = 0;
        ZIndex = isBoss ? 3 : 2;
        _sprite.Texture = texture;
        _sprite.Scale = Vector2.One * scale;
        _sprite.Modulate = Colors.White;
        _frozenOverlay.Texture = frozenTexture;
        _frozenOverlayScale = frozenOverlayScale;
        _frozenOverlay.Scale = Vector2.One * frozenOverlayScale;
        _frozenOverlay.Visible = false;
    }

    public void ShowFrozen() => _frozenOverlay.Visible = true;

    public void UpdateFrozenPulse(float age)
    {
        var pulse = 0.96f + Mathf.Sin(age * 5) * 0.04f;
        _frozenOverlay.Scale = Vector2.One * _frozenOverlayScale * pulse;
    }

    public void HideFrozen()
    {
        _frozenOverlay.Visible = false;
        _frozenOverlay.Scale = Vector2.One * _frozenOverlayScale;
    }

    public void ResetForPool()
    {
        Name = "PooledEnemy";
        Position = Vector2.Zero;
        Rotation = 0;
        ZIndex = 2;
        _sprite.Texture = null;
        _sprite.Scale = Vector2.One;
        _sprite.Modulate = Colors.White;
        _frozenOverlay.Texture = null;
        _frozenOverlayScale = 0;
        _frozenOverlay.Visible = false;
    }
}

internal sealed partial class BattleExplosionView : Node2D
{
    private static readonly Vector2[] UnitCircle = BuildUnitCircle(18);

    private readonly Polygon2D _burst;
    private Tween? _tween;
    private Action<BattleExplosionView>? _completed;

    public BattleExplosionView()
    {
        Name = "Explosion";
        ZIndex = 9;
        _burst = new Polygon2D { Polygon = UnitCircle };
        AddChild(_burst);
    }

    public void PlayEffect(
        Vector2 position,
        float radius,
        Color color,
        Action<BattleExplosionView> completed)
    {
        Position = position;
        Scale = Vector2.One * 0.2f;
        Modulate = Colors.White;
        _burst.Scale = Vector2.One * radius;
        _burst.Color = color;
        _completed = completed;
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One * 1.25f, 0.2);
        _tween.Parallel().TweenProperty(this, "modulate", Colors.Transparent, 0.42);
        _tween.TweenCallback(Callable.From(Complete));
    }

    public void ResetForPool()
    {
        _tween?.Kill();
        _tween = null;
        _completed = null;
        Position = Vector2.Zero;
        Scale = Vector2.One;
        Modulate = Colors.White;
        _burst.Scale = Vector2.One;
        _burst.Color = Colors.White;
    }

    private void Complete()
    {
        _tween = null;
        var completed = _completed;
        _completed = null;
        completed?.Invoke(this);
    }

    private static Vector2[] BuildUnitCircle(int segments)
    {
        var points = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Mathf.Tau * index / segments;
            points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        return points;
    }
}

internal sealed partial class BattleIceImpactView : AnimatedSprite2D
{
    private Action<BattleIceImpactView>? _completed;

    public BattleIceImpactView()
    {
        Name = "IceImpact";
        ZIndex = 10;
        AnimationFinished += Complete;
    }

    public void PlayEffect(
        SpriteFrames frames,
        Vector2 position,
        float scale,
        Action<BattleIceImpactView> completed)
    {
        SpriteFrames = frames;
        Position = position;
        Scale = Vector2.One * scale;
        Modulate = Colors.White;
        _completed = completed;
        Play();
    }

    public void ResetForPool()
    {
        Stop();
        Frame = 0;
        SpriteFrames = null;
        Position = Vector2.Zero;
        Scale = Vector2.One;
        Modulate = Colors.White;
        _completed = null;
    }

    private void Complete()
    {
        var completed = _completed;
        _completed = null;
        completed?.Invoke(this);
    }
}
