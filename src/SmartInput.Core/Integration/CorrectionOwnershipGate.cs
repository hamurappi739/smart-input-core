namespace SmartInput.Core.Integration;

public enum CorrectionEngineMode
{
    Disabled,
    AuditOnly,
    Preview,
    AllowList,
    Live,
}

public sealed record CorrectionOwnershipLease(
    string Owner,
    long Generation,
    Guid LeaseId,
    CorrectionEngineMode Mode);

/// <summary>
/// Thread-safe single-owner gate for future hybrid integration. It prevents a
/// recovered provider and the existing engine from applying the same boundary
/// concurrently. The gate is not a replacement for SafetyPolicyEvaluator.
/// </summary>
public interface ICorrectionOwnershipGate
{
    CorrectionEngineMode Mode { get; }
    string? ActiveOwner { get; }
    bool TrySetMode(CorrectionEngineMode mode);
    bool TryAcquire(string owner, long generation, out CorrectionOwnershipLease? lease);
    bool CanApply(CorrectionOwnershipLease lease);
    void Release(CorrectionOwnershipLease lease);
}

public sealed class CorrectionOwnershipGate : ICorrectionOwnershipGate
{
    private readonly object _sync = new();
    private CorrectionEngineMode _mode = CorrectionEngineMode.Live;
    private CorrectionOwnershipLease? _activeLease;

    public CorrectionEngineMode Mode
    {
        get { lock (_sync) return _mode; }
    }

    public string? ActiveOwner
    {
        get { lock (_sync) return _activeLease?.Owner; }
    }

    public bool TrySetMode(CorrectionEngineMode mode)
    {
        lock (_sync)
        {
            _mode = mode;
            if (mode is CorrectionEngineMode.Disabled
                or CorrectionEngineMode.AuditOnly
                or CorrectionEngineMode.Preview)
            {
                _activeLease = null;
            }

            return true;
        }
    }

    public bool TryAcquire(string owner, long generation, out CorrectionOwnershipLease? lease)
    {
        lease = null;
        if (string.IsNullOrWhiteSpace(owner) || generation < 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_mode is CorrectionEngineMode.Disabled
                or CorrectionEngineMode.AuditOnly
                or CorrectionEngineMode.Preview)
            {
                return false;
            }

            if (_activeLease is not null
                && (_activeLease.Owner != owner || _activeLease.Generation != generation))
            {
                return false;
            }

            _activeLease ??= new CorrectionOwnershipLease(owner, generation, Guid.NewGuid(), _mode);
            lease = _activeLease;
            return true;
        }
    }

    public bool CanApply(CorrectionOwnershipLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            return (_mode is CorrectionEngineMode.AllowList or CorrectionEngineMode.Live)
                && _activeLease is not null
                && _activeLease.LeaseId == lease.LeaseId
                && _activeLease.Owner == lease.Owner
                && _activeLease.Generation == lease.Generation;
        }
    }

    public void Release(CorrectionOwnershipLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            if (_activeLease?.LeaseId == lease.LeaseId)
            {
                _activeLease = null;
            }
        }
    }
}
