namespace SmartInput.App.Services;

public enum MainWindowCloseOutcome
{
    ProceedWithClose,
    CancelAndHide,
}

public sealed class MainWindowLifecyclePolicy
{
    public bool IsCloseToTrayEnabled { get; set; }

    public bool IsExplicitShutdownRequested { get; private set; }

    public MainWindowCloseOutcome EvaluateCloseRequest()
    {
        if (IsExplicitShutdownRequested || !IsCloseToTrayEnabled)
        {
            return MainWindowCloseOutcome.ProceedWithClose;
        }

        return MainWindowCloseOutcome.CancelAndHide;
    }

    public void RequestExplicitShutdown()
    {
        IsExplicitShutdownRequested = true;
    }
}
