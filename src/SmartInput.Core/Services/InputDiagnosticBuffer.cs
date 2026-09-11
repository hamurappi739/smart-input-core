using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public sealed class InputDiagnosticBuffer
{
    public const int DefaultCapacity = 50;

    private readonly object _sync = new();
    private readonly Queue<InputDiagnosticEvent> _events = new();
    private readonly int _capacity;

    public InputDiagnosticBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _events.Count;
            }
        }
    }

    public void Add(InputDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (_sync)
        {
            _events.Enqueue(diagnosticEvent);

            while (_events.Count > _capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public IReadOnlyList<InputDiagnosticEvent> Snapshot()
    {
        lock (_sync)
        {
            return _events.ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
        }
    }
}
