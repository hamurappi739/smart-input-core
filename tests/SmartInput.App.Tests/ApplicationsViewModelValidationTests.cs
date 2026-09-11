using SmartInput.App.ViewModels;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

public class ApplicationsViewModelValidationTests
{
    [Fact]
    public void InvalidProcessName_SetsValidationErrorsAndDisablesSave()
    {
        var viewModel = CreateViewModel();

        viewModel.ExcludedApplicationsText = "bad name";

        Assert.True(viewModel.HasValidationErrors);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void ValidProcessNames_EnablesSave()
    {
        var viewModel = CreateViewModel();

        viewModel.ExcludedApplicationsText = "bankapp, customtool";

        Assert.False(viewModel.HasValidationErrors);
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public void SaveExcludedApplications_PersistsNormalizedEntries()
    {
        var settings = new FakeSettingsService(new AppSettings());
        var viewModel = CreateViewModel(settings);

        viewModel.ExcludedApplicationsText = "BankApp.exe, customtool";
        viewModel.SaveExcludedApplicationsCommand.Execute(null);

        Assert.Equal(["BankApp", "customtool"], settings.Current.ExcludedApplications);
        Assert.False(viewModel.HasValidationErrors);
    }

    private static ApplicationsViewModel CreateViewModel(FakeSettingsService? settingsService = null)
    {
        settingsService ??= new FakeSettingsService(new AppSettings());
        return new ApplicationsViewModel(settingsService, new FakeAutomationSafetyService());
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAutomationSafetyService : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext()
        {
            return new AutomationPolicyResult
            {
                State = AutomationPolicyState.Allowed,
                AllowsAutomation = true,
                AllowsManualExternalTextOperations = true,
            };
        }

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EvaluateCurrentContext());
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => true;

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public string? GetBlockedReason(AutomationOperationKind operationKind) => null;
    }
}
