using AR.Iec61850.Mms;

namespace AR.Iec61850.Simulation;

/// <summary>
/// Composes multiple per-association MMS runtimes. The first runtime that claims
/// a target owns the read/write result. This lets the persistent simulator server
/// keep the standard report-control-block runtime while an application adds
/// process controls or writable setting points without duplicating the MMS stack.
/// </summary>
public sealed class MmsCompositeAssociationRuntime : IMmsAssociationRuntime, IDisposable
{
    private readonly IMmsAssociationRuntime[] _runtimes;
    private bool _disposed;

    public MmsCompositeAssociationRuntime(params IMmsAssociationRuntime[] runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        _runtimes = runtimes.Where(x => x is not null).ToArray();
        if (_runtimes.Length == 0)
            throw new ArgumentException("At least one association runtime is required.", nameof(runtimes));
    }

    public bool TryReadRcbAttribute(string iecTarget, out MmsDataValue value)
    {
        foreach (var runtime in _runtimes)
        {
            if (runtime.TryReadRcbAttribute(iecTarget, out value))
                return true;
        }

        value = MmsDataValue.Boolean(false);
        return false;
    }

    public bool TryWriteRcbAttribute(string iecTarget, MmsDataValue value, out int dataAccessError)
    {
        foreach (var runtime in _runtimes)
        {
            if (runtime.TryWriteRcbAttribute(iecTarget, value, out dataAccessError))
                return true;
        }

        dataAccessError = 0;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var runtime in _runtimes.Reverse())
        {
            if (runtime is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
