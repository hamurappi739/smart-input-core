using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Platform.Windows.Input;

public sealed class TextReplacementSessionCoordinator : ITextReplacementSessionNotifier
{
    private readonly TextReplacementSessionState _sessionState = new();
    private readonly object _cancellationSync = new();
    private CancellationTokenSource? _sessionCancellation;

    public bool IsReplacementActive => _sessionState.IsActive;

    public ReplacementSession BeginReplacement(CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedSource;
        lock (_cancellationSync)
        {
            _sessionCancellation?.Dispose();
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sessionCancellation = linkedSource;
        }

        var scope = _sessionState.BeginSession();
        return new ReplacementSession(this, _sessionState, linkedSource, scope);
    }

    public void NotifyKeyboardEventDuringReplacement(KeyEventType eventType, KeyboardHookMetadata metadata)
    {
        if (!_sessionState.IsActive)
        {
            return;
        }

        var isInjected = InputObservationRules.IsInjectedInput(
            metadata,
            SmartInputInjectionMarkers.SmartInputExtraInfo);

        _sessionState.NotifyKeyboardEvent(eventType, isInjected);

        if (_sessionState.AbortedByUserInput)
        {
            lock (_cancellationSync)
            {
                _sessionCancellation?.Cancel();
            }
        }
    }

    internal void CompleteReplacement()
    {
        lock (_cancellationSync)
        {
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
        }
    }
}

public sealed class ReplacementSession : IDisposable
{
    private readonly TextReplacementSessionCoordinator _coordinator;
    private readonly CancellationTokenSource _linkedSource;
    private readonly IDisposable _scope;
    private bool _disposed;

    internal ReplacementSession(
        TextReplacementSessionCoordinator coordinator,
        TextReplacementSessionState sessionState,
        CancellationTokenSource linkedSource,
        IDisposable scope)
    {
        _coordinator = coordinator;
        SessionState = sessionState;
        _linkedSource = linkedSource;
        _scope = scope;
    }

    public TextReplacementSessionState SessionState { get; }

    public CancellationToken CancellationToken => _linkedSource.Token;

    public void ThrowIfCancellationRequested()
    {
        _linkedSource.Token.ThrowIfCancellationRequested();

        if (SessionState.AbortedByUserInput)
        {
            throw new OperationCanceledException(_linkedSource.Token);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scope.Dispose();
        _linkedSource.Dispose();
        _coordinator.CompleteReplacement();
    }
}
