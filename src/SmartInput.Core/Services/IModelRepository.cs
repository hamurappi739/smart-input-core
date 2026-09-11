namespace SmartInput.Core.Services;

/// <summary>
/// The only model representation exposed to correction engines. Implementations
/// must publish a snapshot only after the container has been authenticated,
/// decrypted, decompressed and parsed successfully.
/// </summary>
public interface IModelRepository
{
    Csm1ModelSnapshot? Current { get; }
}

public sealed class Csm1ModelSnapshot
{
    private readonly byte[] _payload;

    public Csm1ModelSnapshot(
        string modelVersion,
        uint keyId,
        ulong createdAtUnixSeconds,
        ReadOnlySpan<byte> payload,
        string containerSha256,
        bool loadedFromCache)
    {
        ModelVersion = string.IsNullOrWhiteSpace(modelVersion)
            ? throw new ArgumentException("A model version is required.", nameof(modelVersion))
            : modelVersion;
        KeyId = keyId;
        CreatedAtUnixSeconds = createdAtUnixSeconds;
        _payload = payload.ToArray();
        ContainerSha256 = string.IsNullOrWhiteSpace(containerSha256)
            ? throw new ArgumentException("A container hash is required.", nameof(containerSha256))
            : containerSha256;
        LoadedFromCache = loadedFromCache;
    }

    public string ModelVersion { get; }
    public uint KeyId { get; }
    public ulong CreatedAtUnixSeconds { get; }
    public string ContainerSha256 { get; }
    public bool LoadedFromCache { get; }

    /// <summary>Returns a copy so callers cannot mutate the repository snapshot.</summary>
    public byte[] CopyPayload() => _payload.ToArray();
}
