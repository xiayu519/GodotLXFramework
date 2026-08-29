using LX.Res;
using LX.Core.Diagnostics;
using LX.Core.Audio;
using LX.Pooling;
using Godot;
using System.Diagnostics;

namespace LX.Audio;

public sealed class AudioService : IAsyncDisposable
{
    private sealed class SfxVoice
    {
        public required long Sequence { get; init; }
        public required AudioGroupPolicy Policy { get; init; }
        public required TaskCompletionSource<AudioPlayResult> Completion { get; init; }
        public AudioStreamPlayer? Player { get; set; }
    }

    private readonly AssetRegistry _assets;
    private readonly MetricRegistry _metrics;
    private readonly Node _audioRoot;
    private readonly AudioStreamPlayer _musicPlayer;
    private readonly Node _sfxRoot;
    private readonly NodePool<AudioStreamPlayer> _sfxPool;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<TaskCompletionSource> _operations = [];
    private readonly List<SfxVoice> _voices = [];
    private readonly int _mainThreadId;
    private AssetLease<AudioStream>? _musicLease;
    private bool _disposed;
    private int _activeSfx;
    private long _musicRequestSequence;
    private long _voiceSequence;

    public AudioService(Node host, AssetRegistry assets, MetricRegistry metrics)
    {
        ArgumentNullException.ThrowIfNull(host);
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        _audioRoot = new Node { Name = "LXAudio" };
        host.AddChild(_audioRoot);
        _musicPlayer = new AudioStreamPlayer { Name = "Music", Bus = "Music" };
        _audioRoot.AddChild(_musicPlayer);
        _sfxRoot = new Node { Name = "Sfx" };
        _audioRoot.AddChild(_sfxRoot);
        _sfxPool = new NodePool<AudioStreamPlayer>(
            () => new AudioStreamPlayer { Bus = "SFX" },
            player =>
            {
                player.Stop();
                player.Stream = null;
                player.VolumeDb = 0;
                player.PitchScale = 1;
            });
        UpdateMetrics();
    }

    public ValueTask PlayMusicAsync(
        string path,
        float volumeDb = 0,
        CancellationToken cancellationToken = default) =>
        PlayMusicCoreAsync(path, AssetCachePolicy.Cached, volumeDb, cancellationToken);

    public ValueTask PlayMusicAsync(
        AssetRef<AudioStream> asset,
        float volumeDb = 0,
        CancellationToken cancellationToken = default) =>
        PlayMusicCoreAsync(asset.Path, asset.CachePolicy, volumeDb, cancellationToken);

    private async ValueTask PlayMusicCoreAsync(
        string path,
        AssetCachePolicy cachePolicy,
        float volumeDb,
        CancellationToken cancellationToken)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requestSequence = ++_musicRequestSequence;
        var tracked = TrackOperation();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            AssetLease<AudioStream>? next = null;
            try
            {
                next = await _assets.AcquireAsync<AudioStream>(path, cachePolicy, operation.Token);
                EnsureMainThread();
                operation.Token.ThrowIfCancellationRequested();
                if (requestSequence != _musicRequestSequence)
                {
                    return;
                }

                SetMusic(next, volumeDb);
                next = null;
                UpdateMetrics();
            }
            finally
            {
                next?.Dispose();
            }
        }
        finally
        {
            CompleteOperation(tracked);
        }
    }

    public void PlayPcmMusic(
        string id,
        PcmWave pcm,
        int? loopBegin = null,
        int? loopEnd = null,
        float volumeDb = 0)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(pcm);
        if (loopBegin.HasValue != loopEnd.HasValue ||
            loopBegin is < 0 || loopEnd > pcm.SampleFrames ||
            loopBegin >= loopEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(loopBegin), "Loop bounds must define a non-empty range inside the PCM stream.");
        }

        _musicRequestSequence++;
        var key = $"generated://audio/{id}";
        var next = _assets.AcquireGenerated<AudioStream>(key, () => new AudioStreamWav
        {
            Data = pcm.Data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = pcm.SampleRate,
            Stereo = pcm.Channels == 2,
            LoopMode = loopBegin.HasValue ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = loopBegin ?? 0,
            LoopEnd = loopEnd ?? 0,
        });
        SetMusic(next, volumeDb);
    }

    public void StopMusic()
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _musicRequestSequence++;
        StopMusicCore();
    }

    private void StopMusicCore()
    {
        var playback = _musicPlayer.HasStreamPlayback()
            ? _musicPlayer.GetStreamPlayback()
            : null;
        playback?.Stop();
        _musicPlayer.Stop();
        _musicPlayer.Stream = null;
        playback?.Dispose();
        _musicLease?.Dispose();
        _musicLease = null;
        UpdateMetrics();
    }

    public async ValueTask StopMusicAndDrainAsync(CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tracked = TrackOperation();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            _musicRequestSequence++;
            StopMusicCore();
            if (!_audioRoot.IsInsideTree())
            {
                _assets.PurgeIdleCache();
                return;
            }

            // Godot removes a stopped stream from AudioServer on the audio mix
            // thread after its short anti-pop fade. A SceneTree quit in the same
            // frame would strand AudioStreamPlaybackWAV until engine teardown.
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 50)
            {
                operation.Token.ThrowIfCancellationRequested();
                await _audioRoot.ToSignal(_audioRoot.GetTree(), SceneTree.SignalName.ProcessFrame);
                EnsureMainThread();
            }

            _assets.PurgeIdleCache();
        }
        finally
        {
            CompleteOperation(tracked);
        }
    }

    public async ValueTask FadeMusicVolumeAsync(
        float targetVolumeDb,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        if (_musicLease is null)
        {
            return;
        }

        var requestSequence = _musicRequestSequence;
        var tracked = TrackOperation();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            if (duration == TimeSpan.Zero)
            {
                _musicPlayer.VolumeDb = targetVolumeDb;
                return;
            }

            var startVolume = _musicPlayer.VolumeDb;
            var started = Stopwatch.StartNew();
            while (started.Elapsed < duration)
            {
                operation.Token.ThrowIfCancellationRequested();
                var ratio = Math.Clamp((float)(started.Elapsed.TotalSeconds / duration.TotalSeconds), 0, 1);
                _musicPlayer.VolumeDb = Mathf.Lerp(startVolume, targetVolumeDb, ratio);
                await _audioRoot.ToSignal(_audioRoot.GetTree(), SceneTree.SignalName.ProcessFrame);
                EnsureMainThread();
                if (requestSequence != _musicRequestSequence)
                {
                    return;
                }
            }
            if (requestSequence == _musicRequestSequence)
            {
                _musicPlayer.VolumeDb = targetVolumeDb;
            }
        }
        finally
        {
            CompleteOperation(tracked);
        }
    }

    public async ValueTask PlaySfxAsync(
        string path,
        float volumeDb = 0,
        float pitchScale = 1,
        CancellationToken cancellationToken = default) =>
        _ = await PlaySfxCoreAsync(
            path,
            AssetCachePolicy.Cached,
            AudioGroupPolicy.Default,
            volumeDb,
            pitchScale,
            cancellationToken);

    public async ValueTask PlaySfxAsync(
        AssetRef<AudioStream> asset,
        float volumeDb = 0,
        float pitchScale = 1,
        CancellationToken cancellationToken = default) =>
        _ = await PlaySfxCoreAsync(
            asset.Path,
            asset.CachePolicy,
            AudioGroupPolicy.Default,
            volumeDb,
            pitchScale,
            cancellationToken);

    public ValueTask<AudioPlayResult> PlaySfxAsync(
        string path,
        AudioGroupPolicy group,
        float volumeDb = 0,
        float pitchScale = 1,
        CancellationToken cancellationToken = default) =>
        PlaySfxCoreAsync(
            path,
            AssetCachePolicy.Cached,
            group,
            volumeDb,
            pitchScale,
            cancellationToken);

    public ValueTask<AudioPlayResult> PlaySfxAsync(
        AssetRef<AudioStream> asset,
        AudioGroupPolicy group,
        float volumeDb = 0,
        float pitchScale = 1,
        CancellationToken cancellationToken = default) =>
        PlaySfxCoreAsync(
            asset.Path,
            asset.CachePolicy,
            group,
            volumeDb,
            pitchScale,
            cancellationToken);

    public ValueTask<AudioPlayResult> PlayPcmSfxAsync(
        string id,
        PcmWave pcm,
        AudioGroupPolicy group,
        float volumeDb = 0,
        float pitchScale = 1,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(pcm);
        return PlayGeneratedSfxCoreAsync(id, pcm, group, volumeDb, pitchScale, cancellationToken);
    }

    public IReadOnlyList<AudioGroupRecord> SnapshotGroups()
    {
        EnsureMainThread();
        return _voices
            .GroupBy(voice => voice.Policy.Id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var policy = group.First().Policy;
                return new AudioGroupRecord(
                    policy.Id,
                    policy.Bus,
                    group.Count(),
                    policy.MaxConcurrent,
                    policy.OverflowPolicy);
            })
            .ToArray();
    }

    public AudioStateRecord Snapshot()
    {
        EnsureMainThread();
        return new AudioStateRecord(
            _musicLease is not null,
            _musicPlayer.VolumeDb,
            _activeSfx,
            SnapshotGroups());
    }

    private async ValueTask<AudioPlayResult> PlaySfxCoreAsync(
        string path,
        AssetCachePolicy cachePolicy,
        AudioGroupPolicy group,
        float volumeDb,
        float pitchScale,
        CancellationToken cancellationToken)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tracked = TrackOperation();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            var voice = ReserveVoice(group);
            if (voice is null)
            {
                return AudioPlayResult.Rejected;
            }
            try
            {
                using var lease = await _assets.AcquireAsync<AudioStream>(
                    path,
                    cachePolicy,
                    operation.Token);
                EnsureMainThread();
                operation.Token.ThrowIfCancellationRequested();
                return await PlayVoiceAsync(voice, lease.Resource, volumeDb, pitchScale, operation.Token);
            }
            finally
            {
                if (voice.Player is null)
                {
                    _voices.Remove(voice);
                }
            }
        }
        finally
        {
            CompleteOperation(tracked);
        }
    }

    private async ValueTask<AudioPlayResult> PlayGeneratedSfxCoreAsync(
        string id,
        PcmWave pcm,
        AudioGroupPolicy group,
        float volumeDb,
        float pitchScale,
        CancellationToken cancellationToken)
    {
        var tracked = TrackOperation();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            var voice = ReserveVoice(group);
            if (voice is null)
            {
                return AudioPlayResult.Rejected;
            }

            try
            {
                using var lease = _assets.AcquireGenerated<AudioStream>(
                    $"generated://audio/sfx/{id}",
                    () => CreatePcmStream(pcm),
                    AssetCachePolicy.Transient);
                return await PlayVoiceAsync(voice, lease.Resource, volumeDb, pitchScale, operation.Token);
            }
            finally
            {
                if (voice.Player is null)
                {
                    _voices.Remove(voice);
                }
            }
        }
        finally
        {
            CompleteOperation(tracked);
        }
    }

    private async ValueTask<AudioPlayResult> PlayVoiceAsync(
        SfxVoice voice,
        AudioStream stream,
        float volumeDb,
        float pitchScale,
        CancellationToken cancellationToken)
    {
        if (voice.Completion.Task.IsCompleted)
        {
            return await voice.Completion.Task;
        }

        var player = _sfxPool.Rent(_sfxRoot);
        voice.Player = player;
        void Finish() => voice.Completion.TrySetResult(AudioPlayResult.Completed);

        player.Finished += Finish;
        player.Stream = stream;
        player.Bus = voice.Policy.Bus;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchScale;
        _activeSfx++;
        UpdateMetrics();
        using var cancellation = cancellationToken.Register(() =>
            voice.Completion.TrySetCanceled(cancellationToken));
        try
        {
            player.Play();
            var result = await voice.Completion.Task;
            EnsureMainThread();
            return result;
        }
        finally
        {
            player.Finished -= Finish;
            player.Stop();
            _sfxPool.Return(player);
            voice.Player = null;
            _voices.Remove(voice);
            _activeSfx--;
            UpdateMetrics();
        }
    }

    private SfxVoice? ReserveVoice(AudioGroupPolicy policy)
    {
        ValidateGroup(policy);
        var group = _voices
            .Where(voice => voice.Policy.Id == policy.Id)
            .OrderBy(voice => voice.Sequence)
            .ToArray();
        if (group.Any(voice => voice.Policy != policy))
        {
            throw new InvalidOperationException(
                $"Audio group '{policy.Id}' is already active with a different policy.");
        }
        if (group.Length >= policy.MaxConcurrent)
        {
            if (policy.OverflowPolicy == AudioOverflowPolicy.RejectNew)
            {
                return null;
            }

            var oldest = group[0];
            _voices.Remove(oldest);
            oldest.Player?.Stop();
            oldest.Completion.TrySetResult(AudioPlayResult.Preempted);
        }

        var voice = new SfxVoice
        {
            Sequence = ++_voiceSequence,
            Policy = policy,
            Completion = new TaskCompletionSource<AudioPlayResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _voices.Add(voice);
        UpdateMetrics();
        return voice;
    }

    private static void ValidateGroup(AudioGroupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(policy.Id) || string.IsNullOrWhiteSpace(policy.Bus))
        {
            throw new ArgumentException("Audio group ID and bus must be non-empty.", nameof(policy));
        }
        if (policy.MaxConcurrent <= 0 || !Enum.IsDefined(policy.OverflowPolicy))
        {
            throw new ArgumentException("Audio group concurrency and overflow policy must be valid.", nameof(policy));
        }
    }

    private static AudioStreamWav CreatePcmStream(PcmWave pcm) => new()
    {
        Data = pcm.Data,
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = pcm.SampleRate,
        Stereo = pcm.Channels == 2,
    };

    public async ValueTask DisposeAsync()
    {
        EnsureMainThread();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _musicRequestSequence++;
        _shutdown.Cancel();
        if (_audioRoot.IsInsideTree() && _operations.Count > 0)
        {
            await Task.WhenAll(_operations.Select(operation => operation.Task).ToArray());
            EnsureMainThread();
        }

        StopMusicCore();
        _sfxPool.Dispose();
        // Normal SceneTree finalization flushes queued deletion again after the
        // root exits. Emergency async cleanup can nevertheless outlive that
        // final flush, so release the private audio subtree synchronously before
        // AudioServer teardown instead of depending on a later continuation.
        _audioRoot.Free();
        UpdateMetrics();
        _shutdown.Dispose();
    }

    private void UpdateMetrics()
    {
        _metrics.SetGauge("audio.music", _musicLease is null ? 0 : 1);
        _metrics.SetGauge("audio.sfx_active", _activeSfx);
        _metrics.SetGauge("audio.sfx_pool", _sfxPool.RetainedCount);
        _metrics.SetGauge("audio.operations", _operations.Count);
    }

    private void SetMusic(AssetLease<AudioStream> next, float volumeDb)
    {
        _musicPlayer.Stop();
        _musicPlayer.Stream = null;
        _musicLease?.Dispose();
        _musicLease = next;
        _musicPlayer.Stream = next.Resource;
        _musicPlayer.VolumeDb = volumeDb;
        _musicPlayer.Play();
        UpdateMetrics();
    }

    private TaskCompletionSource TrackOperation()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operations.Add(completion);
        UpdateMetrics();
        return completion;
    }

    private void CompleteOperation(TaskCompletionSource operation)
    {
        _operations.Remove(operation);
        operation.TrySetResult();
        UpdateMetrics();
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Audio operations must run on Godot's main thread.");
        }
    }
}
