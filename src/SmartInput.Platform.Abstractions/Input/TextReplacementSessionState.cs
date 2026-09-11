namespace SmartInput.Platform.Abstractions.Input;

public sealed class TextReplacementSessionState
{
    private int _sessionDepth;

    public bool IsActive => Volatile.Read(ref _sessionDepth) > 0;

    public bool AbortedByUserInput { get; private set; }

    public IDisposable BeginSession()
    {
        Interlocked.Increment(ref _sessionDepth);
        AbortedByUserInput = false;
        return new SessionScope(this);
    }

    public void NotifyKeyboardEvent(KeyEventType eventType, bool isInjectedInput)
    {
        if (!IsActive || isInjectedInput || AbortedByUserInput)
        {
            return;
        }

        if (eventType == KeyEventType.KeyDown)
        {
            AbortedByUserInput = true;
        }
    }

    public bool ShouldContinueReplacement => IsActive && !AbortedByUserInput;

    private sealed class SessionScope : IDisposable
    {
        private TextReplacementSessionState? _state;

        public SessionScope(TextReplacementSessionState state)
        {
            _state = state;
        }

        public void Dispose()
        {
            if (_state is null)
            {
                return;
            }

            Interlocked.Decrement(ref _state._sessionDepth);
            _state = null;
        }
    }
}
