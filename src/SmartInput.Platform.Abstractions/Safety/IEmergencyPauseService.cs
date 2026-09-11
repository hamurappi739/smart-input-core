namespace SmartInput.Platform.Abstractions.Safety;

public interface IEmergencyPauseService
{
    bool IsPaused { get; }

    void Pause();

    void Resume();
}
