using Godot;
using LX.Core.Actions;
using LX.Res;
using LX.Runtime;

namespace LX.Media;

/// <summary>Represents the observable state of a reusable video sequence player.</summary>
public enum VideoSequenceState
{
    /// <summary>No sequence is active.</summary>
    Idle,

    /// <summary>A sequence is actively loading or playing an item.</summary>
    Playing,

    /// <summary>The active sequence was skipped through <see cref="VideoSequencePlayer.Skip"/>.</summary>
    Skipped,

    /// <summary>The active sequence observed caller or lifetime cancellation.</summary>
    Cancelled,

    /// <summary>The most recent sequence completed all items.</summary>
    Completed,

    /// <summary>The most recent sequence terminated because playback failed.</summary>
    Failed,
}

/// <summary>Defines one stable, diagnosable item in a video sequence.</summary>
public sealed record VideoSequenceItem(
    string Id,
    AssetRef<VideoStream> Video,
    TimeSpan? Timeout = null);

/// <summary>Describes the terminal result of a video sequence.</summary>
public sealed record VideoSequenceResult(
    VideoSequenceState State,
    int CompletedItems,
    TimeSpan Elapsed);

/// <summary>Provides a bounded runtime snapshot without exposing the underlying Godot player.</summary>
public sealed record VideoSequenceSnapshot(
    VideoSequenceState State,
    string? CurrentItemId,
    int CurrentIndex,
    int ItemCount,
    double PositionSeconds,
    double DurationSeconds);

/// <summary>
/// Plays registered <see cref="VideoStream"/> assets sequentially with resource leases,
/// cancellation, skip semantics, and <c>LX.Actions</c> diagnostics.
/// Product code remains responsible for catalogs, styling, overlays, and input mapping.
/// </summary>
public partial class VideoSequencePlayer : LXNode
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VideoStreamPlayer? _player;
    private CancellationTokenSource? _skip;
    private VideoSequenceState _state;
    private string? _currentItemId;
    private int _currentIndex = -1;
    private int _itemCount;

    /// <summary>Optionally points to a scene-owned <see cref="VideoStreamPlayer"/> child.</summary>
    [Export]
    public NodePath PlayerPath { get; set; } = new();

    /// <summary>Plays every item in order and waits for actual playback completion.</summary>
    public async ValueTask<VideoSequenceResult> PlayAsync(
        IReadOnlyList<VideoSequenceItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateItems(items);
        await _gate.WaitAsync(cancellationToken);
        var started = Time.GetTicksMsec();
        var completedItems = 0;
        using var skip = new CancellationTokenSource();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            Lifetime.Token,
            cancellationToken,
            skip.Token);
        _skip = skip;
        _state = VideoSequenceState.Playing;
        _itemCount = items.Count;
        _currentIndex = -1;
        try
        {
            if (items.Count == 0)
            {
                _state = VideoSequenceState.Completed;
                return BuildResult(completedItems, started);
            }

            var actions = items.Select((item, index) =>
            {
                LXAction action = LXActions.Async(
                    async token =>
                    {
                        _currentIndex = index;
                        _currentItemId = item.Id;
                        await PlayItemAsync(item, token);
                        completedItems++;
                    },
                    $"video:{item.Id}");
                return item.Timeout is { } timeout
                    ? LXActions.Timeout(action, timeout, $"video-timeout:{item.Id}")
                    : action;
            }).ToArray();
            await LX.Actions.RunAsync(
                LXActions.Sequence(actions),
                Lifetime,
                operation.Token);
            _state = VideoSequenceState.Completed;
            return BuildResult(completedItems, started);
        }
        catch (OperationCanceledException) when (
            skip.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !Lifetime.Token.IsCancellationRequested)
        {
            _state = VideoSequenceState.Skipped;
            return BuildResult(completedItems, started);
        }
        catch (OperationCanceledException)
        {
            _state = VideoSequenceState.Cancelled;
            throw;
        }
        catch
        {
            _state = VideoSequenceState.Failed;
            throw;
        }
        finally
        {
            StopAndReleaseStream();
            _currentItemId = null;
            _currentIndex = -1;
            _itemCount = 0;
            _skip = null;
            _gate.Release();
        }
    }

    /// <summary>Requests cancellation of the complete active sequence; repeated calls are harmless.</summary>
    public void Skip() => _skip?.Cancel();

    /// <summary>Returns playback progress and the stable item identifier currently visible in diagnostics.</summary>
    public VideoSequenceSnapshot Snapshot()
    {
        var player = _player;
        return new VideoSequenceSnapshot(
            _state,
            _currentItemId,
            _currentIndex,
            _itemCount,
            player?.StreamPosition ?? 0,
            player?.GetStreamLength() ?? 0);
    }

    protected override void OnLXInitialized()
    {
        var configuredPath = PlayerPath.ToString();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            _player = GetNodeOrNull<VideoStreamPlayer>(PlayerPath) ??
                      throw new InvalidOperationException(
                          $"Video sequence player path '{configuredPath}' does not resolve to VideoStreamPlayer.");
            return;
        }

        _player = new VideoStreamPlayer
        {
            Name = "VideoStreamPlayer",
            Autoplay = false,
        };
        AddChild(_player);
    }

    private async ValueTask PlayItemAsync(
        VideoSequenceItem item,
        CancellationToken cancellationToken)
    {
        var player = _player ??
                     throw new InvalidOperationException("VideoSequencePlayer has not received an LX context.");
        using var lease = await LX.Res.AcquireAsync(item.Video, cancellationToken);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => completion.TrySetResult();
        player.Finished += OnFinished;
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            player.Stream = lease.Resource;
            player.Play();
            await completion.Task;
        }
        finally
        {
            player.Finished -= OnFinished;
            StopAndReleaseStream();
        }
    }

    private void StopAndReleaseStream()
    {
        if (_player is not { } player)
        {
            return;
        }
        player.Stop();
        player.Stream = null;
    }

    private VideoSequenceResult BuildResult(int completedItems, ulong startedMilliseconds) =>
        new(
            _state,
            completedItems,
            TimeSpan.FromMilliseconds(Time.GetTicksMsec() - startedMilliseconds));

    private static void ValidateItems(IReadOnlyList<VideoSequenceItem> items)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new ArgumentException("Video sequence item IDs cannot be empty.", nameof(items));
            }
            if (!ids.Add(item.Id))
            {
                throw new ArgumentException(
                    $"Video sequence item ID '{item.Id}' is duplicated.",
                    nameof(items));
            }
            if (item.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Video timeouts must be positive.");
            }
        }
    }
}
