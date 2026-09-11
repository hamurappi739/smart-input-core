using SmartInput.Core.Models;

namespace SmartInput.App.Services;

public interface IInputDiagnosticCoordinator
{
    bool IsMonitoring { get; }

    IReadOnlyList<InputDiagnosticEvent> GetRecentEvents();

    event EventHandler<InputDiagnosticEvent>? DiagnosticReceived;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ApplyProtectionStateAsync(bool enabled, CancellationToken cancellationToken = default);

    void ClearRecentEvents();
}
