using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.Core.Integration;

namespace SmartInput.App.ViewModels;

public sealed record KbmRouteOption(string Title, KbmMarkerRoute Route);

/// <summary>
/// Safe KBM comparison workbench. It never applies text to another
/// application and never exposes a command that bypasses the normal apply
/// owner, safety policy or Undo transaction.
/// </summary>
public partial class KbmPreviewViewModel : ViewModelBase
{
    private readonly IKbmCandidateProvider _provider;
    private readonly IKbmAuditComparisonService _comparisonService;
    private readonly IPortableCorrectionEngine _ownEngine;

    public KbmPreviewViewModel(
        IKbmCandidateProvider provider,
        IKbmAuditComparisonService comparisonService,
        IPortableCorrectionEngine ownEngine)
    {
        _provider = provider;
        _comparisonService = comparisonService;
        _ownEngine = ownEngine;
        RouteOptions =
        [
            new("Без маркера (raw)", KbmMarkerRoute.Raw),
            new("Префикс 02", KbmMarkerRoute.Prefix02),
            new("Суффикс 03", KbmMarkerRoute.Suffix03),
            new("Префикс 02 + суффикс 03", KbmMarkerRoute.Wrapped0203),
        ];
        SelectedRoute = RouteOptions[1];
    }

    public ObservableCollection<KbmRouteOption> RouteOptions { get; }

    [ObservableProperty]
    private string _sourceToken = string.Empty;

    [ObservableProperty]
    private KbmRouteOption _selectedRoute;

    [ObservableProperty]
    private string _ownDecision = "—";

    [ObservableProperty]
    private string _ownResult = "—";

    [ObservableProperty]
    private string _kbmResult = "—";

    [ObservableProperty]
    private string _comparisonStatus = "Введите токен и выберите подтверждённый маршрут.";

    [ObservableProperty]
    private string _technicalDetails = string.Empty;

    [ObservableProperty]
    private bool _hasCandidate;

    [RelayCommand]
    private void Analyze()
    {
        if (string.IsNullOrWhiteSpace(SourceToken))
        {
            ClearResults("Введите токен для сравнения.");
            return;
        }

        KbmPreparedInput prepared;
        try
        {
            prepared = KbmPreparedInput.FromToken(SourceToken.Trim(), SelectedRoute.Route);
        }
        catch (ArgumentException)
        {
            ClearResults("Токен не удалось подготовить.");
            return;
        }

        var request = new PortableCorrectionRequest
        {
            Token = SourceToken.Trim(),
            LayoutEnabled = true,
            AutocorrectEnabled = true,
        };
        var own = _ownEngine.Evaluate(request);
        var hasCandidate = _provider.TryResolve(prepared, out var candidate);
        var comparison = _comparisonService.Compare(request, prepared, _provider);

        OwnDecision = comparison.OwnDecision;
        OwnResult = own.ReplacementToken ?? "Без замены";
        HasCandidate = hasCandidate && candidate is not null;
        KbmResult = HasCandidate ? candidate!.Text : "Кандидат не найден";
        ComparisonStatus = comparison.Outcome switch
        {
            KbmAuditComparisonOutcome.CandidateMatchesOwnApply =>
                "KBM и собственный движок предлагают один результат.",
            KbmAuditComparisonOutcome.CandidateDiffersFromOwnApply =>
                "Движки предлагают разные результаты — автоматически ничего не применять.",
            KbmAuditComparisonOutcome.OwnEngineWaits =>
                "Собственный движок ждёт: случай неоднозначный.",
            KbmAuditComparisonOutcome.OwnEngineNoChange =>
                "Собственный движок оставляет текст без изменения.",
            _ => "В KBM для выбранного маршрута нет точного кандидата.",
        };
        TechnicalDetails =
            $"Маршрут: {SelectedRoute.Title}; outputId: {comparison.KbmOutputId?.ToString() ?? "—"}; " +
            $"model SHA-256: {Shorten(comparison.KbmModelSha256)}; " +
            "сырой текст в диагностику не записывается, он показан только в этом preview.";
    }

    [RelayCommand]
    private void Clear()
    {
        SourceToken = string.Empty;
        ClearResults("Введите токен и выберите подтверждённый маршрут.");
    }

    private void ClearResults(string status)
    {
        OwnDecision = "—";
        OwnResult = "—";
        KbmResult = "—";
        ComparisonStatus = status;
        TechnicalDetails = string.Empty;
        HasCandidate = false;
    }

    private static string Shorten(string? value)
        => string.IsNullOrEmpty(value) ? "—" : value.Length <= 16 ? value : value[..16] + "…";
}
