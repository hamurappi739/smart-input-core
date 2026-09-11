namespace SmartInput.App.ViewModels;

/// <summary>
/// Groups the less frequently changed settings into one user-facing page.
/// Technical workbenches remain internal and are intentionally not exposed
/// through the normal navigation.
/// </summary>
public sealed class AdditionalViewModel(
    CorrectionsViewModel corrections,
    ApplicationsViewModel applications) : ViewModelBase
{
    public CorrectionsViewModel Corrections { get; } = corrections;

    public ApplicationsViewModel Applications { get; } = applications;
}
