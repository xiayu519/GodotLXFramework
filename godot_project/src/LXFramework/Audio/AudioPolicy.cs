namespace LX.Audio;

/// <summary>音频组达到并发上限时采用的确定性处理策略。</summary>
public enum AudioOverflowPolicy
{
    /// <summary>保留所有正在播放的声音并拒绝新请求。</summary>
    RejectNew,

    /// <summary>停止组内最早开始的声音，为新请求释放一个声道。</summary>
    StopOldest,
}

/// <summary>一次受音频组策略管理的播放请求的最终结果。</summary>
public enum AudioPlayResult
{
    /// <summary>声音自然播放到结尾。</summary>
    Completed,

    /// <summary>请求因并发上限或无效状态而未开始播放。</summary>
    Rejected,

    /// <summary>声音开始播放后被更高优先级或更新的请求抢占。</summary>
    Preempted,
}

public sealed record AudioGroupPolicy(
    string Id,
    string Bus = "SFX",
    int MaxConcurrent = 8,
    AudioOverflowPolicy OverflowPolicy = AudioOverflowPolicy.StopOldest)
{
    public static AudioGroupPolicy Default { get; } = new(
        "default",
        MaxConcurrent: int.MaxValue,
        OverflowPolicy: AudioOverflowPolicy.StopOldest);
}

public sealed record AudioGroupRecord(
    string Id,
    string Bus,
    int Voices,
    int MaxConcurrent,
    AudioOverflowPolicy OverflowPolicy);

public sealed record AudioStateRecord(
    bool MusicPlaying,
    float MusicVolumeDb,
    int ActiveSfx,
    IReadOnlyList<AudioGroupRecord> Groups);
