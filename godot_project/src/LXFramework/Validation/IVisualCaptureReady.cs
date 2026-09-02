namespace LX.Validation;

/// <summary>Allows a visual target to expose an explicit asynchronous readiness barrier before capture.</summary>
public interface IVisualCaptureReady
{
    /// <summary>Completes only after the target has reached the deterministic state intended for capture.</summary>
    ValueTask WaitForVisualCaptureReadyAsync(CancellationToken cancellationToken = default);
}
