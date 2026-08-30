using Godot;
using LX.Generated;
using LX.UI;

namespace PlaneFight.UI;

public partial class BattleHudScreen : UIScreen
{
    private BattleHudModel? _model;

    protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _model = payload as BattleHudModel ??
            throw new ArgumentException(
                $"{nameof(BattleHudScreen)} requires a {nameof(BattleHudModel)} payload.",
                nameof(payload));
        MissileButton.Pressed += UseMissile;
        IceMissileButton.Pressed += UseIceMissile;
        NukeButton.Pressed += UseNuclearBomb;
        ShieldButton.Pressed += UseShield;
        Activation.Defer(() => MissileButton.Pressed -= UseMissile);
        Activation.Defer(() => IceMissileButton.Pressed -= UseIceMissile);
        Activation.Defer(() => NukeButton.Pressed -= UseNuclearBomb);
        Activation.Defer(() => ShieldButton.Pressed -= UseShield);
        Refresh();
        return ValueTask.CompletedTask;
    }

    protected internal override ValueTask OnHideAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _model = null;
        return ValueTask.CompletedTask;
    }

    public override void _Process(double delta)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        HpBar.MaxValue = Math.Max(1, _model.MaxHp);
        HpBar.Value = Math.Clamp(_model.Hp, 0, _model.MaxHp);
        HpLabel.Text = $"{Mathf.CeilToInt(_model.Hp)} / {Mathf.CeilToInt(_model.MaxHp)}";
        ScoreLabel.Text = $"得分 {_model.Score}";
        ProgressLabel.Text = $"Boss 进度 {Math.Min(_model.LevelScore, _model.PassScore)} / {_model.PassScore}";
        CurrencyLabel.Text = $"金币 {_model.Gold} · 勋章 {_model.Medals}";
        WeaponLabel.Text = _model.WeaponSeconds > 0
            ? $"武器：{_model.WeaponName}  {_model.WeaponSeconds:0.0}s"
            : $"武器：{_model.WeaponName}";

        var missilePrompt = LX.Input.Prompt(InputCatalog.Missile).Text;
        var iceMissilePrompt = LX.Input.Prompt(InputCatalog.IceMissile).Text;
        var nuclearBombPrompt = LX.Input.Prompt(InputCatalog.NuclearBomb).Text;
        var shieldPrompt = LX.Input.Prompt(InputCatalog.Shield).Text;
        MissileButton.Text = $"导弹 {missilePrompt}\n× {_model.MissileCount}";
        IceMissileButton.Text = $"冰冻 {iceMissilePrompt}\n× {_model.IceMissileCount}";
        NukeButton.Text = _model.NuclearBombCooldownSeconds > 0
            ? $"核弹冷却\n{_model.NuclearBombCooldownSeconds:0.0}s"
            : $"核弹 {nuclearBombPrompt}\n× {_model.NuclearBombCount}";
        NukeButton.Disabled = !_model.CanUseNuclearBomb;
        ShieldButton.Text = _model.ShieldSeconds > 0
            ? $"护盾生效\n{_model.ShieldSeconds:0.0}s"
            : _model.ShieldCooldownSeconds > 0
                ? $"护盾冷却\n{_model.ShieldCooldownSeconds:0.0}s"
                : $"护盾 {shieldPrompt}\n× {_model.ShieldCount}";

        BossPanel.Visible = _model.BossVisible;
        BossHpBar.MaxValue = Math.Max(1, _model.BossMaxHp);
        BossHpBar.Value = Math.Clamp(_model.BossHp, 0, _model.BossMaxHp);
        WarningLabel.Visible = _model.BossWarningVisible;
    }

    private void UseMissile() => _model?.UseMissile?.Invoke();

    private void UseIceMissile() => _model?.UseIceMissile?.Invoke();

    private void UseNuclearBomb() => _model?.UseNuclearBomb?.Invoke();

    private void UseShield() => _model?.UseShield?.Invoke();
}
