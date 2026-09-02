using System.Text.Json;
using Godot;
using LX.Runtime;

namespace LX.Validation;

/// <summary>Emits the bounded structured protocol consumed by the product smoke runner.</summary>
public static class ProductSmokeProbe
{
    private const string Prefix = "LX_SMOKE_EVENT ";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Marks the checkpoint or command that is about to execute.</summary>
    public static void Started(string id) => Emit(new { kind = "started", id = RequireId(id) });

    /// <summary>Reports a structured checkpoint result from a grouped smoke process.</summary>
    public static void Checkpoint(string id, bool success = true, string? message = null) =>
        Emit(new { kind = "checkpoint", id = RequireId(id), success, message });

    /// <summary>Reports a structured terminal failure with its narrowest known stage.</summary>
    public static void Failed(string id, string message) =>
        Emit(new
        {
            kind = "failed",
            id = RequireId(id),
            message = string.IsNullOrWhiteSpace(message)
                ? throw new ArgumentException("Smoke failure messages cannot be empty.", nameof(message))
                : message,
        });

    /// <summary>
    /// Captures ownership-sensitive state for a before/after closure assertion.
    /// The snapshot intentionally excludes timestamps, instance GUIDs, and recent action history.
    /// </summary>
    public static void Snapshot(LXContext context, string stage)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (stage is not ("before" or "after"))
        {
            throw new ArgumentException("Smoke snapshot stage must be 'before' or 'after'.", nameof(stage));
        }

        var actions = context.Actions.Snapshot();
        var metrics = context.Metrics.Snapshot();
        var state = new
        {
            resources = context.Res.Snapshot()
                .Where(record => record.LeaseCount > 0)
                .OrderBy(record => record.Path, StringComparer.Ordinal)
                .Select(record => new
                {
                    record.Path,
                    record.ResourceType,
                    record.LeaseCount,
                    policy = record.Policy.ToString(),
                }),
            ui = context.UI.Snapshot()
                .OrderBy(record => record.UIId, StringComparer.Ordinal)
                .Select(record => new
                {
                    record.UIId,
                    layer = record.Layer.ToString(),
                    state = record.State.ToString(),
                }),
            features = context.Features.Snapshot()
                .OrderBy(record => record.FeatureId, StringComparer.Ordinal)
                .Select(record => new { record.FeatureId, record.NodeName }),
            audio = BuildAudioState(context),
            input = context.Input.Snapshot().Contexts
                .OrderBy(record => record.Order)
                .Select(record => new
                {
                    record.Id,
                    mode = record.Mode.ToString(),
                    record.Actions,
                }),
            actions = actions.Active.Select(ProjectAction),
            metrics = new
            {
                gauges = metrics.Gauges,
            },
        };
        Emit(new { kind = "snapshot", stage, state });
    }

    private static object BuildAudioState(LXContext context)
    {
        var audio = context.Audio.Snapshot();
        return new
        {
            audio.MusicPlaying,
            audio.ActiveSfx,
            groups = audio.Groups
                .OrderBy(group => group.Id, StringComparer.Ordinal)
                .Select(group => new { group.Id, group.Voices }),
        };
    }

    private static object ProjectAction(LX.Core.Actions.ActionNodeSnapshot action) => new
    {
        action.Name,
        state = action.State.ToString(),
        children = action.Children.Select(ProjectAction),
    };

    private static void Emit(object value) => GD.Print(Prefix + JsonSerializer.Serialize(value, JsonOptions));

    private static string RequireId(string id) =>
        string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Smoke event IDs cannot be empty.", nameof(id))
            : id;
}
