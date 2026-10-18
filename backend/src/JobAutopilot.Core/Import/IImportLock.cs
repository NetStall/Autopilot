namespace JobAutopilot.Core.Import;

/// <summary>
/// Ensures that only one import runs at a time — across requests, background runs and API instances.
/// </summary>
public interface IImportLock
{
    /// <summary>
    /// Takes the lock without waiting. Returns a handle that releases it when disposed, or <c>null</c> when another
    /// import holds it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
