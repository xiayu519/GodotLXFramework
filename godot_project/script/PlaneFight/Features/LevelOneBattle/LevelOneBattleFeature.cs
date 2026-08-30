using Godot;
using GameData.plane_fight;
using LX.Audio;
using LX.Camera;
using LX.Core.Actions;
using LX.Generated;
using LX.Input;
using LX.Pooling;
using LX.Res;
using LX.Runtime;
using PlaneFight.UI;

namespace PlaneFight.Features.LevelOneBattle;

public partial class LevelOneBattleFeature : LXNode
{
    private static readonly Color FrozenTint = new(0.48f, 0.82f, 1f);
    private static readonly AudioGroupPolicy WeaponAudioGroup =
        new("plane.weapon", MaxConcurrent: 8, OverflowPolicy: AudioOverflowPolicy.StopOldest);
    private static readonly AudioGroupPolicy ImpactAudioGroup =
        new("plane.impact", MaxConcurrent: 5, OverflowPolicy: AudioOverflowPolicy.StopOldest);
    private static readonly IReadOnlyDictionary<string, string> StandardWeaponNames =
        new Dictionary<string, string> { ["zh_CN"] = "标准机炮", ["en"] = "Standard Cannon" };
    private static readonly IReadOnlyDictionary<string, string> TwinWeaponNames =
        new Dictionary<string, string> { ["zh_CN"] = "双联机炮", ["en"] = "Twin Cannon" };
    private static readonly IReadOnlyDictionary<string, string> LaserWeaponNames =
        new Dictionary<string, string> { ["zh_CN"] = "高能激光", ["en"] = "High-Energy Laser" };
    private static readonly IReadOnlyDictionary<string, string> SpreadWeaponNames =
        new Dictionary<string, string> { ["zh_CN"] = "扇形弹幕", ["en"] = "Spread Barrage" };

    private readonly List<EnemyActor> _enemies = [];
    private readonly List<ProjectileActor> _projectiles = [];
    private readonly List<PickupActor> _pickups = [];
    private readonly HashSet<BattleExplosionView> _activeExplosions = [];
    private readonly HashSet<BattleIceImpactView> _activeIceImpacts = [];
    private readonly TaskCompletionSource<BattleOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LevelConfig _level = null!;
    private Node2D _battleRoot = null!;
    private Node2D _backgroundRoot = null!;
    private Node2D _effectRoot = null!;
    private Sprite2D _backgroundOne = null!;
    private Sprite2D _backgroundTwo = null!;
    private Sprite2D _player = null!;
    private Polygon2D _shieldVisual = null!;
    private Camera2DController _cameraController = null!;
    private NodePool<BattleProjectileView> _projectilePool = null!;
    private NodePool<BattleTrailView> _trailPool = null!;
    private NodePool<BattlePickupView> _pickupPool = null!;
    private NodePool<BattleEnemyView> _enemyPool = null!;
    private NodePool<BattleExplosionView> _explosionPool = null!;
    private NodePool<BattleIceImpactView> _iceImpactPool = null!;

    private Texture2D _enemyOneTexture = null!;
    private Texture2D _enemyTwoTexture = null!;
    private Texture2D _enemyFiveTexture = null!;
    private Texture2D _bossTexture = null!;
    private Texture2D _healPlusTexture = null!;
    private Texture2D _missileTexture = null!;
    private Texture2D _iceMissileTexture = null!;
    private Texture2D[] _iceImpactTextures = [];
    private SpriteFrames? _iceImpactFrames;
    private readonly Dictionary<string, Texture2D> _pickupTextures = [];
    private Texture2D[] _goldFrames = [];

    private BattleState _state = BattleState.Waiting;
    private WeaponMode _weapon = WeaponMode.Standard;
    private BattleOutcomeKind _endingKind;
    private float _hp;
    private float _maxHp;
    private int _score;
    private int _levelScore;
    private int _gold;
    private int _medals;
    private int _missiles;
    private int _iceMissiles;
    private int _nuclearBombs;
    private int _shields;
    private float _enemySpawnTimer;
    private float _pickupSpawnTimer;
    private float _fireTimer;
    private float _fireSoundTimer;
    private float _weaponSeconds;
    private float _missileSeconds;
    private float _missileFireTimer;
    private float _iceMissileSeconds;
    private float _iceMissileFireTimer;
    private float _nukeCooldown;
    private float _shieldSeconds;
    private float _shieldCooldown;
    private float _hurtInvincibility;
    private float _endingTimer;
    private float _nuclearBombDamage;
    private bool _missilePressed;
    private bool _iceMissilePressed;
    private bool _nukePressed;
    private bool _shieldPressed;
    private bool _muteAudio;
    private bool _configured;

    public BattleHudModel HudModel { get; private set; } = null!;

    public Task<BattleOutcome> Completion => _completion.Task;

    internal int ActiveProjectileCount => _projectiles.Count;

    internal int ActivePickupCount => _pickups.Count;

    internal int ActiveEnemyCount => _enemies.Count;

    internal int NuclearBombCount => _nuclearBombs;

    internal float NuclearBombCooldown => _nukeCooldown;

    internal BattleState State => _state;

    internal int ActiveTransientEffectCount => _effectRoot?.GetChildCount() ?? 0;

    internal int RentedPooledNodeCount =>
        _projectilePool.RentedCount +
        _trailPool.RentedCount +
        _pickupPool.RentedCount +
        _enemyPool.RentedCount +
        _explosionPool.RentedCount +
        _iceImpactPool.RentedCount;

    internal int RetainedPooledNodeCount =>
        _projectilePool.RetainedCount +
        _trailPool.RetainedCount +
        _pickupPool.RetainedCount +
        _enemyPool.RetainedCount +
        _explosionPool.RetainedCount +
        _iceImpactPool.RetainedCount;

    protected override void OnLXInitialized()
    {
        _muteAudio = OS.GetCmdlineUserArgs()
            .Any(argument => argument is
                "--plane-fight-smoke" or
                "--plane-fight-flow-smoke" or
                "--plane-fight-api-smoke");
        Lifetime.Own(Lifetime.Token.Register(() => _completion.TrySetCanceled(Lifetime.Token)));
    }

    internal void Configure(LevelConfig level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_configured)
        {
            throw new InvalidOperationException("The level-one battle was configured more than once.");
        }

        _configured = true;
        _level = level;
        _nuclearBombDamage = _level.Pickups
            .Single(pickup => pickup.Effect == PickupEffect.NUCLEAR_BOMB)
            .Amount;
        if (_nuclearBombDamage <= 0)
        {
            throw new InvalidOperationException("The nuclear bomb pickup damage must be positive.");
        }
        AcquireTextures();
        BuildWorld();
        CreateFrameworkPools();
        Lifetime.Defer(ReleaseRentedNodes);
        ResetRuntimeState();
    }

    private void CreateFrameworkPools()
    {
        _projectilePool = Lifetime.Own(new NodePool<BattleProjectileView>(
            static () => new BattleProjectileView(),
            static view => view.ResetForPool(),
            maxRetained: 192));
        _trailPool = Lifetime.Own(new NodePool<BattleTrailView>(
            static () => new BattleTrailView(),
            static view => view.ResetForPool(),
            maxRetained: 48));
        _pickupPool = Lifetime.Own(new NodePool<BattlePickupView>(
            static () => new BattlePickupView(),
            static view => view.ResetForPool(),
            maxRetained: 24));
        _enemyPool = Lifetime.Own(new NodePool<BattleEnemyView>(
            static () => new BattleEnemyView(),
            static view => view.ResetForPool(),
            maxRetained: 24));
        _explosionPool = Lifetime.Own(new NodePool<BattleExplosionView>(
            static () => new BattleExplosionView(),
            static view => view.ResetForPool(),
            maxRetained: 32));
        _iceImpactPool = Lifetime.Own(new NodePool<BattleIceImpactView>(
            static () => new BattleIceImpactView(),
            static view => view.ResetForPool(),
            maxRetained: 16));
    }

    public void StartBattle()
    {
        if (!_configured)
        {
            throw new InvalidOperationException("The level-one battle must be configured before it starts.");
        }
        if (_state != BattleState.Waiting)
        {
            return;
        }

        _state = BattleState.Waves;
        Lifetime.Own(LX.Input.PushContext(new InputContextDescriptor(
            "plane_fight_battle",
            new HashSet<InputActionId>
            {
                InputCatalog.MoveLeft,
                InputCatalog.MoveRight,
                InputCatalog.MoveUp,
                InputCatalog.MoveDown,
                InputCatalog.Missile,
                InputCatalog.IceMissile,
                InputCatalog.NuclearBomb,
                InputCatalog.Shield,
            })));
        LX.Metrics.Increment("plane.battles.started");
        _enemySpawnTimer = 0;
        _pickupSpawnTimer = _level.PickupSpawnInterval;
        _fireTimer = 0;
        if (!_muteAudio)
        {
            _ = PlayMusicSafelyAsync();
        }
    }

    internal void CompleteForSmoke(BattleOutcomeKind kind)
    {
        if (!_muteAudio)
        {
            throw new InvalidOperationException("Forced battle completion is only available during product smoke.");
        }
        BeginEnding(kind);
    }

    internal void VerifyNuclearBombContractForSmoke()
    {
        if (!_muteAudio || _state != BattleState.Waiting)
        {
            throw new InvalidOperationException(
                "The nuclear bomb contract probe requires a waiting product-smoke battle.");
        }

        var initialInventory = _nuclearBombs;
        _state = BattleState.BossWarning;
        TryUseNuclearBomb();
        if (_nuclearBombs != initialInventory)
        {
            throw new InvalidOperationException(
                "The nuclear bomb was consumed during the targetless boss-warning phase.");
        }

        _state = BattleState.Boss;
        SpawnBoss();
        var boss = _enemies.Single(enemy => enemy.IsBoss);
        var initialBossHp = boss.Hp;
        var pickup = _level.Pickups.Single(item => item.Effect == PickupEffect.NUCLEAR_BOMB);
        _nukeCooldown = 1;
        if (ApplyPickup(pickup) || boss.Hp != initialBossHp)
        {
            throw new InvalidOperationException(
                "A cooling nuclear pickup was consumed or damaged the boss.");
        }

        _nukeCooldown = 0;
        if (!ApplyPickup(pickup))
        {
            throw new InvalidOperationException("A ready nuclear pickup was not consumed.");
        }
        var expectedBossHp = initialBossHp - pickup.Amount * 2;
        if (!Mathf.IsEqualApprox(boss.Hp, expectedBossHp) || _nuclearBombs != initialInventory)
        {
            throw new InvalidOperationException(
                "The nuclear pickup did not use its Luban damage or changed player inventory.");
        }

        ClearProjectiles();
        ClearPickups();
        ClearEnemies();
        ClearTransientEffects();
        _cameraController.StopShake();
        _state = BattleState.Waiting;
        _nukeCooldown = 0;
        _nuclearBombs = initialInventory;
        HudModel.BossVisible = false;
        HudModel.BossWarningVisible = false;
        HudModel.BossHp = _level.Boss.Hp;
        GD.Print("PLANE_FIGHT_NUCLEAR_CONTRACT_PASS");
    }

    public override void _Process(double delta)
    {
        var seconds = (float)delta;
        if (_state == BattleState.Waiting || _state == BattleState.Completed)
        {
            UpdateHud();
            return;
        }

        ScrollBackground(seconds);

        if (_state == BattleState.Ending)
        {
            UpdateEnding(seconds);
            UpdateEffectsAndPickups(seconds, allowCollection: false);
            UpdateHud();
            return;
        }

        UpdateTimers(seconds);
        UpdatePlayerInput(seconds);
        UpdatePlayerWeapons(seconds);
        UpdateEnemies(seconds);
        UpdateProjectiles(seconds);
        UpdateEffectsAndPickups(seconds, allowCollection: true);
        UpdateStageFlow(seconds);
        UpdateHud();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_state is not (BattleState.Waves or BattleState.Clearing or BattleState.BossWarning or BattleState.Boss))
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            MovePlayer(mouseMotion.Relative);
        }
        else if (inputEvent is InputEventScreenDrag screenDrag)
        {
            MovePlayer(screenDrag.Relative);
        }
    }

    private void AcquireTextures()
    {
        var background = Acquire(ResCatalog.PfLevel1Background);
        _enemyOneTexture = Acquire(ResCatalog.PfEnemy1);
        _enemyTwoTexture = Acquire(ResCatalog.PfEnemy2);
        _enemyFiveTexture = Acquire(ResCatalog.PfEnemy5);
        _bossTexture = Acquire(ResCatalog.PfBoss2);
        var playerTexture = Acquire(ResCatalog.PfPlayer);
        _healPlusTexture = Acquire(ResCatalog.PfPickupHealPlus);
        _missileTexture = Acquire(ResCatalog.PfMissileProjectile);
        _iceMissileTexture = Acquire(ResCatalog.PfIceMissileProjectile);
        _iceImpactTextures =
        [
            Acquire(ResCatalog.PfIceImpact1),
            Acquire(ResCatalog.PfIceImpact2),
            Acquire(ResCatalog.PfIceImpact3),
            Acquire(ResCatalog.PfIceImpact4),
        ];
        _pickupTextures.Add("nuke", Acquire(ResCatalog.PfPickupNuke));
        _pickupTextures.Add("missile", Acquire(ResCatalog.PfPickupMissile));
        _pickupTextures.Add("ice_missile", Acquire(ResCatalog.PfPickupIceMissile));
        _pickupTextures.Add("weapon_spread", Acquire(ResCatalog.PfPickupWeaponSpread));
        _pickupTextures.Add("weapon_laser", Acquire(ResCatalog.PfPickupWeaponLaser));
        _pickupTextures.Add("weapon_twin", Acquire(ResCatalog.PfPickupWeaponTwin));
        _pickupTextures.Add("heal", Acquire(ResCatalog.PfPickupHeal));
        _pickupTextures.Add("medal", Acquire(ResCatalog.PfPickupMedal));
        _goldFrames =
        [
            Acquire(ResCatalog.PfPickupGold1),
            Acquire(ResCatalog.PfPickupGold2),
            Acquire(ResCatalog.PfPickupGold3),
            Acquire(ResCatalog.PfPickupGold4),
            Acquire(ResCatalog.PfPickupGold5),
            Acquire(ResCatalog.PfPickupGold6),
            Acquire(ResCatalog.PfPickupGold7),
        ];
        _pickupTextures.Add("gold", _goldFrames[0]);

        var musicLease = Lifetime.Own(LX.Res.Acquire(ResCatalog.PfLevelBgm));
        if (musicLease.Resource is AudioStreamMP3 music)
        {
            music.Loop = true;
        }

        _backgroundRoot = new Node2D { Name = "Background" };
        _backgroundOne = CreateSprite(background, 1.5f);
        _backgroundTwo = CreateSprite(background, 1.5f);
        _player = CreateSprite(playerTexture, 0.66f);
    }

    private Texture2D Acquire(AssetRef<Texture2D> asset) =>
        Lifetime.Own(LX.Res.Acquire(asset)).Resource;

    private void BuildWorld()
    {
        _battleRoot = new Node2D { Name = "BattleRoot" };
        AddChild(_battleRoot);

        var camera = new Camera2D
        {
            Name = "BattleCamera",
            Position = new Vector2(_level.DesignWidth / 2f, _level.DesignHeight / 2f),
            Enabled = true,
        };
        _battleRoot.AddChild(camera);
        _cameraController = Camera2DController.Attach(camera, Lifetime);
        _cameraController.SetCenterBounds(new Rect2(camera.Position, Vector2.Zero));

        _battleRoot.AddChild(_backgroundRoot);
        var backgroundHeight = _backgroundOne.Texture.GetHeight() * _backgroundOne.Scale.Y;
        _backgroundOne.Position = new Vector2(_level.DesignWidth / 2f, _level.DesignHeight / 2f);
        _backgroundTwo.Position = _backgroundOne.Position - new Vector2(0, backgroundHeight);
        _backgroundOne.ZIndex = -20;
        _backgroundTwo.ZIndex = -20;
        _backgroundRoot.AddChild(_backgroundOne);
        _backgroundRoot.AddChild(_backgroundTwo);

        var shade = new ColorRect
        {
            Name = "BattleShade",
            Color = new Color(0.02f, 0.08f, 0.16f, 0.18f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(_level.DesignWidth, _level.DesignHeight),
            ZIndex = -15,
        };
        _battleRoot.AddChild(shade);

        _effectRoot = new Node2D { Name = "Effects", ZIndex = 8 };
        _battleRoot.AddChild(_effectRoot);

        var exhaust = new Polygon2D
        {
            Name = "EngineExhaust",
            Polygon =
            [
                new Vector2(-15, 32),
                new Vector2(0, 78),
                new Vector2(15, 32),
            ],
            Color = new Color(0.2f, 0.85f, 1f, 0.82f),
            ZIndex = -1,
        };
        _player.AddChild(exhaust);
        _player.Position = new Vector2(_level.DesignWidth / 2f, 1080);
        _player.ZIndex = 5;
        _battleRoot.AddChild(_player);

        _shieldVisual = new Polygon2D
        {
            Name = "Shield",
            Polygon = CirclePoints(78, 40),
            Color = new Color(0.18f, 0.78f, 1f, 0.28f),
            Visible = false,
            ZIndex = -2,
        };
        _player.AddChild(_shieldVisual);
    }

    private void ResetRuntimeState()
    {
        _maxHp = _level.Player.AircraftHp + _level.Player.ShieldBonusHp;
        _hp = _maxHp;
        _missiles = _level.Player.MissileCount;
        _iceMissiles = _level.Player.IceMissileCount;
        _nuclearBombs = _level.Player.NuclearBombCount;
        _shields = _level.Player.ShieldCount;
        HudModel = new BattleHudModel
        {
            MaxHp = _maxHp,
            Hp = _hp,
            PassScore = _level.PassScore,
            MissileCount = _missiles,
            IceMissileCount = _iceMissiles,
            NuclearBombCount = _nuclearBombs,
            NuclearBombCooldownSeconds = _nukeCooldown,
            CanUseNuclearBomb = false,
            ShieldCount = _shields,
            BossMaxHp = _level.Boss.Hp,
            BossHp = _level.Boss.Hp,
            UseMissile = TryUseMissile,
            UseIceMissile = TryUseIceMissile,
            UseNuclearBomb = TryUseNuclearBomb,
            UseShield = TryUseShield,
        };
        UpdateHud();
    }

    private void ScrollBackground(float delta)
    {
        const float scrollSpeed = 120;
        var backgroundHeight = _backgroundOne.Texture.GetHeight() * _backgroundOne.Scale.Y;
        _backgroundOne.Position += Vector2.Down * scrollSpeed * delta;
        _backgroundTwo.Position += Vector2.Down * scrollSpeed * delta;
        var recycleY = _level.DesignHeight / 2f + backgroundHeight;
        if (_backgroundOne.Position.Y >= recycleY)
        {
            _backgroundOne.Position -= Vector2.Down * backgroundHeight * 2;
        }
        if (_backgroundTwo.Position.Y >= recycleY)
        {
            _backgroundTwo.Position -= Vector2.Down * backgroundHeight * 2;
        }
    }

    private void UpdateTimers(float delta)
    {
        _fireTimer -= delta;
        _fireSoundTimer = Math.Max(0, _fireSoundTimer - delta);
        _weaponSeconds = Math.Max(0, _weaponSeconds - delta);
        _missileSeconds = Math.Max(0, _missileSeconds - delta);
        _iceMissileSeconds = Math.Max(0, _iceMissileSeconds - delta);
        _nukeCooldown = Math.Max(0, _nukeCooldown - delta);
        _shieldSeconds = Math.Max(0, _shieldSeconds - delta);
        _shieldCooldown = Math.Max(0, _shieldCooldown - delta);
        _hurtInvincibility = Math.Max(0, _hurtInvincibility - delta);
        _shieldVisual.Visible = _shieldSeconds > 0;
        _shieldVisual.Rotation += delta * 1.6f;

        if (_weapon != WeaponMode.Standard && _weaponSeconds <= 0)
        {
            _weapon = WeaponMode.Standard;
        }
    }

    private void UpdatePlayerInput(float delta)
    {
        var direction = LX.Input.Direction(
            InputCatalog.MoveLeft,
            InputCatalog.MoveRight,
            InputCatalog.MoveUp,
            InputCatalog.MoveDown);
        if (direction != Vector2.Zero)
        {
            MovePlayer(direction * _level.Player.MoveSpeed * delta);
        }

        HandleActionEdge(LX.Input.IsPressed(InputCatalog.Missile), ref _missilePressed, TryUseMissile);
        HandleActionEdge(LX.Input.IsPressed(InputCatalog.IceMissile), ref _iceMissilePressed, TryUseIceMissile);
        HandleActionEdge(LX.Input.IsPressed(InputCatalog.NuclearBomb), ref _nukePressed, TryUseNuclearBomb);
        HandleActionEdge(LX.Input.IsPressed(InputCatalog.Shield), ref _shieldPressed, TryUseShield);
    }

    private static void HandleActionEdge(bool pressed, ref bool previous, Action action)
    {
        if (pressed && !previous)
        {
            action();
        }
        previous = pressed;
    }

    private void MovePlayer(Vector2 offset)
    {
        var next = _player.Position + offset;
        next.X = Math.Clamp(next.X, 70, _level.DesignWidth - 70);
        next.Y = Math.Clamp(next.Y, 210, _level.DesignHeight - 180);
        _player.Position = next;
    }

    private void UpdatePlayerWeapons(float delta)
    {
        if (_fireTimer <= 0)
        {
            FirePrimaryWeapon();
            _fireTimer = _weapon == WeaponMode.Laser
                ? _level.Player.FireInterval * 0.7f
                : _level.Player.FireInterval;
        }

        if (_missileSeconds > 0)
        {
            _missileFireTimer -= delta;
            if (_missileFireTimer <= 0)
            {
                SpawnMissile(freezes: false);
                _missileFireTimer = 0.3f;
            }
        }

        if (_iceMissileSeconds > 0)
        {
            _iceMissileFireTimer -= delta;
            if (_iceMissileFireTimer <= 0)
            {
                SpawnMissile(freezes: true);
                _iceMissileFireTimer = 0.6f;
            }
        }
    }

    private void FirePrimaryWeapon()
    {
        var damage = _level.Player.BulletDamage * (1 + _level.Player.Power * 0.15f);
        switch (_weapon)
        {
            case WeaponMode.Twin:
                SpawnPlayerBullet(new Vector2(-22, -54), new Vector2(0, -980), damage, new Color(0.25f, 0.9f, 1f));
                SpawnPlayerBullet(new Vector2(22, -54), new Vector2(0, -980), damage, new Color(0.25f, 0.9f, 1f));
                break;
            case WeaponMode.Laser:
                SpawnPlayerBullet(new Vector2(0, -70), new Vector2(0, -1200), damage * 1.45f, new Color(0.35f, 0.7f, 1f), 12);
                break;
            case WeaponMode.Spread:
                for (var angle = -24; angle <= 24; angle += 12)
                {
                    SpawnPlayerBullet(
                        new Vector2(0, -52),
                        Vector2.Up.Rotated(Mathf.DegToRad(angle)) * 900,
                        damage * 0.7f,
                        new Color(1f, 0.78f, 0.18f));
                }
                break;
            default:
                SpawnPlayerBullet(new Vector2(0, -56), new Vector2(0, -940), damage, new Color(0.32f, 0.95f, 1f));
                break;
        }

        if (_fireSoundTimer <= 0)
        {
            PlaySfx(ResCatalog.PfWeaponFireAlt, -12);
            _fireSoundTimer = 0.55f;
        }
    }

    private void SpawnPlayerBullet(
        Vector2 offset,
        Vector2 velocity,
        float damage,
        Color color,
        float radius = 7)
    {
        SpawnProjectile(
            _player.Position + offset,
            velocity,
            damage,
            radius,
            fromPlayer: true,
            homing: false,
            freezes: false,
            color);
    }

    private void SpawnMissile(bool freezes)
    {
        if (!freezes)
        {
            SpawnProjectile(
                _player.Position + new Vector2(NextRandomFloat(-40, 40), -40),
                Vector2.Up * 1200,
                3 + _level.Player.Power,
                25,
                fromPlayer: true,
                homing: false,
                freezes: false,
                new Color(1f, 0.36f, 0f),
                _missileTexture,
                textureScale: 1.613f,
                accelerates: true,
                lifeSeconds: 3,
                trailColor: new Color(1f, 0.36f, 0f),
                trailWidth: 20,
                trailPointLimit: 18);
            return;
        }

        for (var index = 0; index < 12; index++)
        {
            var direction = Vector2.Right.Rotated(Mathf.DegToRad((index + 1) * 30));
            SpawnProjectile(
                _player.Position + new Vector2(0, -40),
                direction * 900,
                2 + _level.Player.Power,
                25,
                fromPlayer: true,
                homing: true,
                freezes: true,
                new Color(0.45f, 0.9f, 1f),
                _iceMissileTexture,
                textureScale: 1,
                accelerates: false,
                lifeSeconds: 3,
                trailColor: new Color(1f, 1f, 0.96f),
                trailWidth: 20,
                trailPointLimit: 12);
        }
    }

    private void UpdateEnemies(float delta)
    {
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            var enemy = _enemies[index];
            var wasFrozen = enemy.SlowSeconds > 0;
            enemy.SlowSeconds = Math.Max(0, enemy.SlowSeconds - delta);
            UpdateFrozenVisual(enemy, delta, wasFrozen);
            var speedScale = enemy.SlowSeconds > 0 ? 0.42f : 1f;

            if (enemy.IsBoss)
            {
                if (enemy.Root.Position.Y < _level.Boss.StopY)
                {
                    enemy.Root.Position += Vector2.Down * enemy.Speed * speedScale * delta;
                    if (enemy.Root.Position.Y > _level.Boss.StopY)
                    {
                        enemy.Root.Position = new Vector2(enemy.Root.Position.X, _level.Boss.StopY);
                    }
                }
            }
            else if (enemy.ChasesPlayer)
            {
                if (_state == BattleState.Clearing)
                {
                    enemy.Root.Position += Vector2.Down * enemy.Speed * speedScale * delta;
                }
                else
                {
                    var toPlayer = _player.Position - enemy.Root.Position;
                    if (toPlayer.Length() > 460)
                    {
                        enemy.Root.Position += toPlayer.Normalized() * enemy.Speed * speedScale * delta;
                    }
                }
            }
            else
            {
                enemy.Root.Position += Vector2.Down * enemy.Speed * speedScale * delta;
            }

            if (enemy.Fires && enemy.Root.Position.Y > 30 && enemy.Root.Position.Y < 650)
            {
                enemy.FireTimer -= delta;
                if (enemy.FireTimer <= 0)
                {
                    FireEnemyWeapon(enemy);
                    enemy.FireTimer = enemy.FireInterval;
                }
            }

            if (enemy.Root.Position.DistanceTo(_player.Position) < enemy.Radius + 34)
            {
                DamagePlayer(enemy.ContactDamage);
                if (!enemy.IsBoss)
                {
                    DestroyEnemy(index, awardScore: false, dropPickup: false);
                    continue;
                }
            }

            if (!enemy.IsBoss && enemy.Root.Position.Y > _level.DesignHeight + 130)
            {
                DestroyEnemy(index, awardScore: false, dropPickup: false);
            }
        }
    }

    private void FireEnemyWeapon(EnemyActor enemy)
    {
        var aim = (_player.Position - enemy.Root.Position).Normalized();
        if (enemy.IsBoss)
        {
            for (var angle = -42; angle <= 42; angle += 14)
            {
                var direction = Vector2.Down.Rotated(Mathf.DegToRad(angle));
                SpawnProjectile(
                    enemy.Root.Position + new Vector2(0, 112),
                    direction * 430,
                    1,
                    10,
                    fromPlayer: false,
                    homing: false,
                    freezes: false,
                    new Color(1f, 0.22f, 0.16f));
            }
            SpawnProjectile(
                enemy.Root.Position + new Vector2(0, 90),
                aim * 520,
                1,
                12,
                fromPlayer: false,
                homing: false,
                freezes: false,
                new Color(1f, 0.6f, 0.12f));
            PlaySfx(ResCatalog.PfBossFire, -8);
        }
        else
        {
            var offsets = enemy.Id == "enemy_2" ? new[] { -20f, 20f } : new[] { 0f };
            foreach (var offset in offsets)
            {
                SpawnProjectile(
                    enemy.Root.Position + new Vector2(offset, 45),
                    aim * 390,
                    1,
                    9,
                    fromPlayer: false,
                    homing: false,
                    freezes: false,
                    new Color(1f, 0.28f, 0.35f));
            }
            PlaySfx(ResCatalog.PfEnemyFire, -12);
        }
    }

    private void SpawnProjectile(
        Vector2 position,
        Vector2 velocity,
        float damage,
        float radius,
        bool fromPlayer,
        bool homing,
        bool freezes,
        Color color,
        Texture2D? texture = null,
        float textureScale = 1,
        bool accelerates = false,
        float lifeSeconds = 6,
        Color? trailColor = null,
        float trailWidth = 0,
        int trailPointLimit = 0)
    {
        var root = _projectilePool.Rent(_battleRoot);
        root.Configure(
            fromPlayer ? "PlayerProjectile" : "EnemyProjectile",
            position,
            velocity,
            radius,
            homing,
            color,
            texture,
            textureScale);

        BattleTrailView? trail = null;
        if (trailColor.HasValue && trailWidth > 0 && trailPointLimit > 1)
        {
            trail = _trailPool.Rent(_battleRoot);
            trail.Configure(trailColor.Value, trailWidth);
            trail.AddPoint(position);
        }

        _projectiles.Add(new ProjectileActor
        {
            Root = root,
            Velocity = velocity,
            Damage = damage,
            Radius = radius,
            FromPlayer = fromPlayer,
            Homing = homing,
            Freezes = freezes,
            Accelerates = accelerates,
            MaximumSpeed = velocity.Length(),
            LifeSeconds = lifeSeconds,
            Trail = trail,
            TrailPointLimit = trailPointLimit,
        });
    }

    private void UpdateProjectiles(float delta)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            projectile.LifeSeconds -= delta;
            projectile.ElapsedSeconds += delta;
            if (projectile.Accelerates && projectile.Velocity != Vector2.Zero)
            {
                var speedRatio = Math.Clamp(projectile.ElapsedSeconds / 3f, 0.08f, 1f);
                projectile.Velocity = projectile.Velocity.Normalized() *
                                      projectile.MaximumSpeed * speedRatio;
            }
            if (projectile.Homing)
            {
                var target = FindNearestEnemy(projectile.Root.Position);
                if (target is not null)
                {
                    var desired = (target.Root.Position - projectile.Root.Position).Normalized() *
                                  projectile.Velocity.Length();
                    projectile.Velocity = projectile.Velocity.Lerp(
                        desired,
                        Math.Clamp(delta * 0.8f, 0, 1));
                }
            }

            projectile.Root.Position += projectile.Velocity * delta;
            projectile.Root.Rotation = projectile.Velocity.Angle() +
                                       (projectile.Trail is null ? Mathf.Pi / 2 : 0);
            if (projectile.Trail is not null)
            {
                projectile.Trail.AddPoint(projectile.Root.Position);
                while (projectile.Trail.GetPointCount() > projectile.TrailPointLimit)
                {
                    projectile.Trail.RemovePoint(0);
                }
            }
            if (projectile.LifeSeconds <= 0 ||
                projectile.Root.Position.Y < -180 ||
                projectile.Root.Position.Y > _level.DesignHeight + 180 ||
                projectile.Root.Position.X < -180 ||
                projectile.Root.Position.X > _level.DesignWidth + 180)
            {
                RemoveProjectile(index);
                continue;
            }

            if (projectile.FromPlayer)
            {
                var hit = false;
                for (var enemyIndex = _enemies.Count - 1; enemyIndex >= 0; enemyIndex--)
                {
                    var enemy = _enemies[enemyIndex];
                    if (projectile.Root.Position.DistanceTo(enemy.Root.Position) >
                        projectile.Radius + enemy.Radius)
                    {
                        continue;
                    }

                    DamageEnemy(enemyIndex, projectile.Damage, projectile.Freezes);
                    if (_state is BattleState.Ending or BattleState.Completed)
                    {
                        return;
                    }
                    hit = true;
                    break;
                }
                if (hit)
                {
                    RemoveProjectile(index);
                }
            }
            else if (projectile.Root.Position.DistanceTo(_player.Position) < projectile.Radius + 32)
            {
                DamagePlayer(projectile.Damage);
                if (_state is BattleState.Ending or BattleState.Completed)
                {
                    return;
                }
                RemoveProjectile(index);
            }
        }
    }

    private EnemyActor? FindNearestEnemy(Vector2 position)
    {
        EnemyActor? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var enemy in _enemies)
        {
            var distance = position.DistanceSquaredTo(enemy.Root.Position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
            }
        }
        return nearest;
    }

    private void DamageEnemy(int index, float damage, bool freezes)
    {
        if (index < 0 || index >= _enemies.Count)
        {
            return;
        }

        var enemy = _enemies[index];
        enemy.Hp -= damage;
        if (freezes)
        {
            enemy.SlowSeconds = Math.Max(enemy.SlowSeconds, 3);
            enemy.FrozenVisualAge = 0;
            enemy.Sprite.Modulate = FrozenTint;
            EnsureFrozenOverlay(enemy);
            SpawnIceImpact(enemy.Root.Position, enemy.Radius);
        }
        else
        {
            enemy.HitFlashSeconds = 0.1f;
            enemy.Sprite.Modulate = new Color(1f, 0.58f, 0.58f);
        }

        if (enemy.IsBoss)
        {
            HudModel.BossHp = Math.Max(0, enemy.Hp);
        }
        if (enemy.Hp <= 0)
        {
            DestroyEnemy(index, awardScore: true, dropPickup: !enemy.IsBoss);
        }
    }

    private void EnsureFrozenOverlay(EnemyActor enemy)
    {
        enemy.Root.ShowFrozen();
    }

    private void UpdateFrozenVisual(EnemyActor enemy, float delta, bool wasFrozen)
    {
        enemy.HitFlashSeconds = Math.Max(0, enemy.HitFlashSeconds - delta);
        if (enemy.SlowSeconds > 0)
        {
            enemy.FrozenVisualAge += delta;
            enemy.Sprite.Modulate = FrozenTint;
            enemy.Root.UpdateFrozenPulse(enemy.FrozenVisualAge);
            return;
        }
        if (!wasFrozen)
        {
            if (enemy.HitFlashSeconds <= 0)
            {
                enemy.Sprite.Modulate = Colors.White;
            }
            return;
        }

        enemy.FrozenVisualAge = 0;
        enemy.Sprite.Modulate = enemy.HitFlashSeconds > 0
            ? new Color(1f, 0.58f, 0.58f)
            : Colors.White;
        enemy.Root.HideFrozen();
    }

    private void DestroyEnemy(int index, bool awardScore, bool dropPickup)
    {
        var enemy = _enemies[index];
        var position = enemy.Root.Position;
        _enemies.RemoveAt(index);
        _enemyPool.Return(enemy.Root);
        SpawnExplosion(position, enemy.IsBoss ? 110 : 48, enemy.IsBoss
            ? new Color(1f, 0.65f, 0.12f)
            : new Color(1f, 0.32f, 0.18f));
        PlaySfx(ResCatalog.PfEnemyExplosion, enemy.IsBoss ? -2 : -8);

        if (awardScore)
        {
            _score += enemy.Score;
            _levelScore += enemy.Score;
            LX.Metrics.Increment(enemy.IsBoss ? "plane.kills.boss" : "plane.kills.enemy");
        }
        if (dropPickup)
        {
            TrySpawnRandomPickup(position);
        }
        if (enemy.IsBoss)
        {
            BeginEnding(BattleOutcomeKind.Victory);
        }
    }

    private void DamagePlayer(float damage)
    {
        if (_muteAudio || _state == BattleState.Ending || _shieldSeconds > 0 || _hurtInvincibility > 0)
        {
            return;
        }

        _hp = Math.Max(0, _hp - damage);
        _hurtInvincibility = 1.1f;
        _cameraController.Shake(7, TimeSpan.FromSeconds(0.28));
        _player.Modulate = new Color(1f, 0.35f, 0.35f);
        var tween = _player.CreateTween();
        tween.TweenProperty(_player, "modulate", Colors.White, 0.25);
        if (_hp <= 0)
        {
            SpawnExplosion(_player.Position, 85, new Color(0.25f, 0.78f, 1f));
            _player.Visible = false;
            BeginEnding(BattleOutcomeKind.Defeat);
        }
    }

    private void UpdateStageFlow(float delta)
    {
        if (_state == BattleState.Waves)
        {
            _enemySpawnTimer -= delta;
            if (_enemySpawnTimer <= 0)
            {
                for (var index = 0; index < _level.EnemySpawnCount; index++)
                {
                    TrySpawnEnemy();
                }
                _enemySpawnTimer = _level.EnemySpawnInterval;
            }

            _pickupSpawnTimer -= delta;
            if (_pickupSpawnTimer <= 0)
            {
                for (var index = 0; index < _level.PickupSpawnCount; index++)
                {
                    TrySpawnRandomPickup(new Vector2(
                        NextRandomFloat(80, _level.DesignWidth - 80),
                        -70));
                }
                _pickupSpawnTimer = _level.PickupSpawnInterval;
            }

            if (_levelScore >= _level.PassScore)
            {
                _state = BattleState.Clearing;
            }
        }
        else if (_state == BattleState.Clearing && _enemies.Count == 0)
        {
            _state = BattleState.BossWarning;
            HudModel.BossWarningVisible = true;
            LX.Metrics.Increment("plane.boss_warnings");
            PlaySfx(ResCatalog.PfBossWarning, -2);
            Lifetime.Own(LX.Scheduler.Schedule(
                TimeSpan.FromSeconds(_level.BossWarningSeconds),
                () =>
                {
                    if (_state != BattleState.BossWarning)
                    {
                        return;
                    }
                    HudModel.BossWarningVisible = false;
                    SpawnBoss();
                    _state = BattleState.Boss;
                },
                Lifetime));
        }
    }

    private void TrySpawnEnemy()
    {
        var roll = NextRandomFloat(0, 100);
        var definition = _level.Enemies.First(enemy => roll < enemy.SelectionThreshold);
        if (_enemies.Count(enemy => enemy.Id == definition.Id) >= definition.MaxAlive)
        {
            return;
        }

        var texture = definition.Id switch
        {
            "enemy_2" => _enemyTwoTexture,
            "enemy_5" => _enemyFiveTexture,
            _ => _enemyOneTexture,
        };
        var scale = definition.Id switch
        {
            "enemy_2" => 0.56f,
            "enemy_5" => 0.7f,
            _ => 0.62f,
        };
        var position = definition.ChasesPlayer
            ? new Vector2(LX.Random.NextDouble() > 0.5 ? 58 : _level.DesignWidth - 58, -80)
            : new Vector2(NextRandomFloat(75, _level.DesignWidth - 75), -110);
        var radius = definition.Id == "enemy_2" ? 46 : 38;
        var root = _enemyPool.Rent(_battleRoot);
        root.Configure(
            definition.Id,
            position,
            texture,
            scale,
            _iceImpactTextures[0],
            Math.Clamp(radius / 75f, 0.65f, 1.45f),
            isBoss: false);

        _enemies.Add(new EnemyActor
        {
            Root = root,
            Sprite = root.Sprite,
            Id = definition.Id,
            MaxHp = definition.Hp,
            Hp = definition.Hp,
            Score = definition.Score,
            Speed = NextRandomFloat(definition.Speed, definition.Speed * 2),
            Radius = radius,
            ContactDamage = definition.ContactDamage,
            ChasesPlayer = definition.ChasesPlayer,
            Fires = definition.Fires,
            FireInterval = definition.FireInterval,
            FireTimer = NextRandomFloat(0.25f, Math.Max(0.3f, definition.FireInterval)),
        });
    }

    private void SpawnBoss()
    {
        const float bossRadius = 104;
        var root = _enemyPool.Rent(_battleRoot);
        root.Configure(
            _level.Boss.Id,
            new Vector2(_level.DesignWidth / 2f, -230),
            _bossTexture,
            1.35f,
            _iceImpactTextures[0],
            Math.Clamp(bossRadius / 75f, 0.65f, 1.45f),
            isBoss: true);
        _enemies.Add(new EnemyActor
        {
            Root = root,
            Sprite = root.Sprite,
            Id = _level.Boss.Id,
            MaxHp = _level.Boss.Hp,
            Hp = _level.Boss.Hp,
            Score = _level.Boss.Score,
            Speed = NextRandomFloat(_level.Boss.Speed, _level.Boss.Speed * 2),
            Radius = bossRadius,
            ContactDamage = _level.Boss.ContactDamage,
            ChasesPlayer = false,
            Fires = true,
            FireInterval = _level.Boss.FireInterval,
            FireTimer = 0.8f,
            IsBoss = true,
        });
        HudModel.BossVisible = true;
        HudModel.BossHp = _level.Boss.Hp;
        HudModel.BossMaxHp = _level.Boss.Hp;
    }

    private void TrySpawnRandomPickup(Vector2 position)
    {
        var roll = NextRandomFloat(0, 100);
        var definition = _level.Pickups.FirstOrDefault(pickup => roll < pickup.SelectionThreshold);
        if (definition is null)
        {
            return;
        }
        SpawnPickup(definition, position);
    }

    private void SpawnPickup(PickupConfig definition, Vector2 position)
    {
        var root = _pickupPool.Rent(_battleRoot);
        root.Configure(
            definition.Id,
            position,
            _pickupTextures[definition.Id],
            _healPlusTexture);
        _pickups.Add(new PickupActor
        {
            Root = root,
            Sprite = root.Sprite,
            Definition = definition,
            Frames = definition.Id == "gold" ? _goldFrames : null,
        });
    }

    private void UpdateEffectsAndPickups(float delta, bool allowCollection)
    {
        for (var index = _pickups.Count - 1; index >= 0; index--)
        {
            var pickup = _pickups[index];
            pickup.Age += delta;
            pickup.Root.Position += Vector2.Down * 200 * delta;
            pickup.Root.Rotation = Mathf.Sin(pickup.Age * 2.8f) * 0.14f;
            if (pickup.Frames is not null)
            {
                var frame = (int)(pickup.Age * 12) % pickup.Frames.Length;
                pickup.Sprite.Texture = pickup.Frames[frame];
            }

            if (allowCollection && pickup.Root.Position.DistanceTo(_player.Position) < 58)
            {
                if (ApplyPickup(pickup.Definition))
                {
                    LX.Metrics.Increment("plane.pickups.collected");
                    RemovePickup(index);
                }
            }
            else if (pickup.Root.Position.Y > _level.DesignHeight + 90)
            {
                RemovePickup(index);
            }
        }
    }

    private bool ApplyPickup(PickupConfig pickup)
    {
        switch (pickup.Effect)
        {
            case PickupEffect.NUCLEAR_BOMB:
                return DetonateNuclearBomb(consumeInventory: false, pickup.Amount);
            case PickupEffect.ADD_MISSILE:
                _missiles += (int)pickup.Amount;
                PlaySfx(ResCatalog.PfUiClick);
                break;
            case PickupEffect.ADD_ICE_MISSILE:
                _iceMissiles += (int)pickup.Amount;
                PlaySfx(ResCatalog.PfUiClick);
                break;
            case PickupEffect.SPREAD_WEAPON:
                ActivateWeapon(WeaponMode.Spread, pickup.Duration, ResCatalog.PfPlayerFire);
                break;
            case PickupEffect.LASER_WEAPON:
                ActivateWeapon(WeaponMode.Laser, pickup.Duration, ResCatalog.PfIceMissile);
                break;
            case PickupEffect.TWIN_WEAPON:
                ActivateWeapon(WeaponMode.Twin, pickup.Duration, ResCatalog.PfWeaponUpgrade);
                break;
            case PickupEffect.HEAL:
                _hp = Math.Min(_maxHp, _hp + _maxHp * pickup.Amount);
                PlaySfx(ResCatalog.PfHeal);
                break;
            case PickupEffect.ADD_MEDAL:
                _medals += (int)pickup.Amount;
                PlaySfx(ResCatalog.PfUiClick);
                break;
            case PickupEffect.ADD_GOLD:
                _gold += (int)pickup.Amount;
                PlaySfx(ResCatalog.PfUiClick);
                break;
        }
        return true;
    }

    private void ActivateWeapon(WeaponMode weapon, float duration, AssetRef<AudioStream> sound)
    {
        _weapon = weapon;
        _weaponSeconds = duration;
        PlaySfx(sound);
    }

    private void TryUseMissile()
    {
        if (!CanUseSkills() || _missiles <= 0 || _missileSeconds > 0)
        {
            return;
        }
        _missiles--;
        _missileSeconds = 6;
        _missileFireTimer = 0;
        PlaySfx(ResCatalog.PfMissile);
    }

    private void TryUseIceMissile()
    {
        if (!CanUseSkills() || _iceMissiles <= 0 || _iceMissileSeconds > 0)
        {
            return;
        }
        _iceMissiles--;
        _iceMissileSeconds = 5;
        _iceMissileFireTimer = 0;
        PlaySfx(ResCatalog.PfMissile);
    }

    private void TryUseNuclearBomb()
    {
        if (!CanUseNuclearBomb() || _nuclearBombs <= 0 || _nukeCooldown > 0)
        {
            return;
        }
        _ = DetonateNuclearBomb(consumeInventory: true, _nuclearBombDamage);
    }

    private bool DetonateNuclearBomb(bool consumeInventory, float baseDamage)
    {
        if (_nukeCooldown > 0)
        {
            return false;
        }
        if (consumeInventory)
        {
            _nuclearBombs--;
        }
        _nukeCooldown = 3;
        var targetsHit = _enemies.Count;
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            var damage = _enemies[index].IsBoss ? baseDamage * 2 : baseDamage;
            DamageEnemy(index, damage, freezes: false);
        }
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            if (!_projectiles[index].FromPlayer)
            {
                RemoveProjectile(index);
            }
        }
        _ = PlayNuclearBombPresentationSafelyAsync();
        PlaySfx(ResCatalog.PfNuclearBomb, 1);
        LX.Metrics.Increment("plane.nuclear_bombs.detonated");
        LX.Events.Publish(new NuclearBombDetonated(baseDamage, consumeInventory, targetsHit));
        return true;
    }

    private void TryUseShield()
    {
        if (!CanUseSkills() || _shields <= 0 || _shieldSeconds > 0 || _shieldCooldown > 0)
        {
            return;
        }
        _shields--;
        _shieldSeconds = _level.Player.ShieldDuration;
        _shieldCooldown = _level.Player.ShieldCooldown;
        _shieldVisual.Visible = true;
        PlaySfx(ResCatalog.PfShield);
    }

    private bool CanUseSkills() =>
        _state is BattleState.Waves or BattleState.Clearing or BattleState.BossWarning or BattleState.Boss;

    private bool CanUseNuclearBomb() =>
        _state is BattleState.Waves or BattleState.Clearing or BattleState.Boss;

    private void BeginEnding(BattleOutcomeKind kind)
    {
        if (_state is BattleState.Ending or BattleState.Completed)
        {
            return;
        }
        _endingKind = kind;
        _state = BattleState.Ending;
        _endingTimer = kind == BattleOutcomeKind.Victory ? 1.8f : 1.15f;
        _weapon = WeaponMode.Standard;
        _weaponSeconds = 0;
        _missileSeconds = 0;
        _iceMissileSeconds = 0;
        _nukeCooldown = 0;
        _shieldSeconds = 0;
        _shieldCooldown = 0;
        _shieldVisual.Visible = false;
        ClearProjectiles();
        ClearPickups();
        ClearFrozenEnemyVisuals();
        HudModel.BossWarningVisible = false;
        HudModel.BossVisible = false;
        LX.Audio.StopMusic();
        PlaySfx(kind == BattleOutcomeKind.Victory ? ResCatalog.PfVictory : ResCatalog.PfGameOver);
    }

    private void UpdateEnding(float delta)
    {
        _endingTimer -= delta;
        if (_endingKind == BattleOutcomeKind.Victory && _player.Visible)
        {
            _player.Position += _endingTimer > 1.25f
                ? Vector2.Down * 220 * delta
                : Vector2.Up * 820 * delta;
        }
        if (_endingTimer > 0)
        {
            return;
        }

        ClearEnemies();
        ClearTransientEffects();
        _battleRoot.Position = Vector2.Zero;
        _state = BattleState.Completed;
        LX.Metrics.Increment(_endingKind == BattleOutcomeKind.Victory
            ? "plane.battles.victory"
            : "plane.battles.defeat");
        LX.Events.Publish(new BattleFinished(_state, _score, _gold, _medals));
        _completion.TrySetResult(new BattleOutcome(_endingKind, _score, _gold, _medals));
    }

    private void ClearProjectiles()
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            RemoveProjectile(index);
        }
    }

    private void ClearPickups()
    {
        for (var index = _pickups.Count - 1; index >= 0; index--)
        {
            RemovePickup(index);
        }
    }

    private void ClearEnemies()
    {
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            var enemy = _enemies[index];
            _enemies.RemoveAt(index);
            _enemyPool.Return(enemy.Root);
        }
    }

    private void ClearFrozenEnemyVisuals()
    {
        foreach (var enemy in _enemies)
        {
            enemy.SlowSeconds = 0;
            enemy.FrozenVisualAge = 0;
            enemy.HitFlashSeconds = 0;
            enemy.Sprite.Modulate = Colors.White;
            enemy.Root.HideFrozen();
        }
    }

    private void ClearTransientEffects()
    {
        foreach (var explosion in _activeExplosions.ToArray())
        {
            if (_activeExplosions.Remove(explosion))
            {
                _explosionPool.Return(explosion);
            }
        }
        foreach (var impact in _activeIceImpacts.ToArray())
        {
            if (_activeIceImpacts.Remove(impact))
            {
                _iceImpactPool.Return(impact);
            }
        }
        foreach (var child in _effectRoot.GetChildren())
        {
            _effectRoot.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ReleaseRentedNodes()
    {
        ClearProjectiles();
        ClearPickups();
        ClearEnemies();
        ClearTransientEffects();
        _iceImpactFrames?.Dispose();
        _iceImpactFrames = null;
    }

    private void UpdateHud()
    {
        if (HudModel is null)
        {
            return;
        }
        HudModel.Hp = _hp;
        HudModel.MaxHp = _maxHp;
        HudModel.Score = _score;
        HudModel.LevelScore = _levelScore;
        HudModel.PassScore = _level.PassScore;
        HudModel.Gold = _gold;
        HudModel.Medals = _medals;
        HudModel.MissileCount = _missiles;
        HudModel.IceMissileCount = _iceMissiles;
        HudModel.NuclearBombCount = _nuclearBombs;
        HudModel.NuclearBombCooldownSeconds = _nukeCooldown;
        HudModel.CanUseNuclearBomb = CanUseNuclearBomb() &&
            _nuclearBombs > 0 &&
            _nukeCooldown <= 0;
        HudModel.ShieldCount = _shields;
        HudModel.WeaponName = LX.Localization.ResolveVariant(_weapon switch
        {
            WeaponMode.Twin => TwinWeaponNames,
            WeaponMode.Laser => LaserWeaponNames,
            WeaponMode.Spread => SpreadWeaponNames,
            _ => StandardWeaponNames,
        });
        HudModel.WeaponSeconds = _weaponSeconds;
        HudModel.ShieldSeconds = _shieldSeconds;
        HudModel.ShieldCooldownSeconds = _shieldSeconds > 0 ? 0 : _shieldCooldown;
    }

    private void SpawnExplosion(Vector2 position, float radius, Color color)
    {
        var explosion = _explosionPool.Rent(_effectRoot);
        _activeExplosions.Add(explosion);
        explosion.PlayEffect(position, radius, color, ReturnExplosion);
    }

    private void SpawnIceImpact(Vector2 position, float targetRadius)
    {
        var impact = _iceImpactPool.Rent(_effectRoot);
        _activeIceImpacts.Add(impact);
        impact.PlayEffect(
            GetIceImpactFrames(),
            position,
            Math.Clamp(targetRadius / 75f, 0.65f, 1.45f),
            ReturnIceImpact);
    }

    private SpriteFrames GetIceImpactFrames()
    {
        if (_iceImpactFrames is not null)
        {
            return _iceImpactFrames;
        }

        _iceImpactFrames = new SpriteFrames();
        _iceImpactFrames.SetAnimationLoopMode("default", SpriteFrames.LoopMode.None);
        _iceImpactFrames.SetAnimationSpeed("default", 1f / 0.06f);
        foreach (var texture in _iceImpactTextures)
        {
            _iceImpactFrames.AddFrame("default", texture);
        }
        return _iceImpactFrames;
    }

    private void ReturnExplosion(BattleExplosionView explosion)
    {
        if (_activeExplosions.Remove(explosion))
        {
            _explosionPool.Return(explosion);
        }
    }

    private void ReturnIceImpact(BattleIceImpactView impact)
    {
        if (_activeIceImpacts.Remove(impact))
        {
            _iceImpactPool.Return(impact);
        }
    }

    private void FlashScreen(Color color)
    {
        var flash = new ColorRect
        {
            Color = color,
            Size = new Vector2(_level.DesignWidth, _level.DesignHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 20,
        };
        _battleRoot.AddChild(flash);
        var tween = flash.CreateTween();
        tween.TweenProperty(flash, "modulate", Colors.Transparent, 0.5);
        tween.TweenCallback(Callable.From(flash.QueueFree));
    }

    private void RemoveProjectile(int index)
    {
        var projectile = _projectiles[index];
        _projectiles.RemoveAt(index);
        if (projectile.Trail is not null)
        {
            _trailPool.Return(projectile.Trail);
        }
        _projectilePool.Return(projectile.Root);
    }

    private void RemovePickup(int index)
    {
        var pickup = _pickups[index];
        _pickups.RemoveAt(index);
        _pickupPool.Return(pickup.Root);
    }

    private static Sprite2D CreateSprite(Texture2D texture, float scale)
    {
        return new Sprite2D
        {
            Texture = texture,
            Centered = true,
            Scale = Vector2.One * scale,
        };
    }

    private static Vector2[] CirclePoints(float radius, int segments)
    {
        var points = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Mathf.Tau * index / segments;
            points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    private float NextRandomFloat(float inclusiveMin, float exclusiveMax) =>
        inclusiveMin + (float)LX.Random.NextDouble() * (exclusiveMax - inclusiveMin);

    private void PlaySfx(AssetRef<AudioStream> sound, float volumeDb = -6)
    {
        if (_muteAudio)
        {
            return;
        }
        _ = PlaySfxSafelyAsync(sound, volumeDb);
    }

    private async Task PlayNuclearBombPresentationSafelyAsync()
    {
        var presentation = LXActions.Finally(
            LXActions.Sequence(
                LXActions.Invoke(
                    () =>
                    {
                        FlashScreen(new Color(1f, 0.85f, 0.3f, 0.82f));
                        _cameraController.Shake(13, TimeSpan.FromSeconds(0.55));
                    },
                    "nuclear_flash"),
                LXActions.Delay(TimeSpan.FromMilliseconds(120), "nuclear_aftershock"),
                LXActions.Invoke(
                    () => FlashScreen(new Color(1f, 0.45f, 0.12f, 0.28f)),
                    "nuclear_afterglow")),
            LXActions.Invoke(
                () => LX.Metrics.Increment("plane.nuclear_presentations.completed"),
                "nuclear_metrics"),
            "nuclear_presentation");
        try
        {
            await LX.Actions.RunAsync(presentation, Lifetime, Lifetime.Token);
        }
        catch (OperationCanceledException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private async Task PlaySfxSafelyAsync(AssetRef<AudioStream> sound, float volumeDb)
    {
        try
        {
            var group = sound == ResCatalog.PfPlayerFire || sound == ResCatalog.PfWeaponFireAlt
                ? WeaponAudioGroup
                : ImpactAudioGroup;
            var result = await LX.Audio.PlaySfxAsync(
                sound,
                group,
                volumeDb,
                cancellationToken: Lifetime.Token);
            if (result == AudioPlayResult.Rejected)
            {
                LX.Metrics.Increment("plane.audio.rejected");
            }
        }
        catch (OperationCanceledException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private async Task PlayMusicSafelyAsync()
    {
        try
        {
            await LX.Audio.PlayMusicAsync(ResCatalog.PfLevelBgm, -8, Lifetime.Token);
        }
        catch (OperationCanceledException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
    }
}
