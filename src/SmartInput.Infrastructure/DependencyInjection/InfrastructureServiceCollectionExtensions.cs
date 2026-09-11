using Microsoft.Extensions.DependencyInjection;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Infrastructure.RecoveredRules;
using SmartInput.Infrastructure.Rust;
using SmartInput.Infrastructure.Safety;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSmartInputInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IEmergencyPauseService, SettingsBackedEmergencyPauseService>();
        services.AddSingleton<IUserAutocorrectDictionaryPersistence, JsonUserAutocorrectDictionaryPersistence>();
        services.AddSingleton<IUserAutocorrectDictionaryStore, UserAutocorrectDictionaryStore>();
        services.AddSingleton<ILocalPredictionModel, StarterLocalPredictionModel>();
        services.AddSingleton<IHunspellWordFormProvider, HunspellWordFormProvider>();
        services.AddSingleton<IAutocorrectDictionary>(sp =>
            new CompositeAutocorrectDictionary(
                sp.GetRequiredService<IUserAutocorrectDictionaryStore>(),
                sp.GetRequiredService<IHunspellWordFormProvider>()));
        services.AddSingleton<IApplicationProfileStore, InMemoryApplicationProfileStore>();
        services.AddSingleton<ISnippetPersistence, JsonSnippetPersistence>();
        services.AddSingleton<ISnippetService, SnippetService>();
        services.AddSingleton<ICorrectionRejectionLearningPersistence, JsonCorrectionRejectionLearningPersistence>();
        services.AddSingleton<ICorrectionRejectionLearningStore, CorrectionRejectionLearningStore>();
        services.AddSingleton<ICorrectionRejectionPolicy, CorrectionRejectionPolicy>();
        // Local language-engine adapters used by the live bounded correction
        // path and by the explicit comparison diagnostics.
        services.AddSingleton<SymSpellSpellCorrectionProvider>(sp =>
            new SymSpellSpellCorrectionProvider(
                sp.GetRequiredService<IHunspellWordFormProvider>()));
        services.AddSingleton<IExternalAutocorrectionEvaluator, ExternalAutocorrectionEvaluator>();
        services.AddSingleton<ISpellEngineComparisonService, SpellEngineComparisonService>();
        services.AddSingleton<HunspellExternalSpellCorrectionProvider>();
        services.AddSingleton<IExternalSpellCorrectionProvider, CompositeExternalSpellCorrectionProvider>();

        // Rust is deliberately opt-in. No DLL is loaded unless
        // SMARTINPUT_RUST_ENGINE_DLL points to an explicit local build. The
        // live hybrid adapter still has no replacement APIs of its own; Core
        // safety gates remain the only authority allowed to apply text.
        services.AddSingleton<IRustShadowCandidateProvider>(_ =>
            RustNativeShadowCandidateProvider.CreateFromEnvironment());
        // Generic scorer facade remains available for audit/preview callers.
        services.AddSingleton<IAdditionalCorrectionScorer>(sp =>
            new RustAdditionalCorrectionScorer(
                sp.GetRequiredService<IRustShadowCandidateProvider>()));

        // Recovered KBM is intentionally disabled by default. The provider is
        // audit/preview-only and never participates in live replacement.
        services.AddSingleton<IKbmCandidateProvider>(_ =>
        {
            var modelDirectory = Environment.GetEnvironmentVariable("SMARTINPUT_KBM_MODEL_DIR");
            if (string.IsNullOrWhiteSpace(modelDirectory))
            {
                return new NullKbmCandidateProvider();
            }

            try
            {
                return KbmCandidateProvider.Load(
                    modelDirectory,
                    KbmCandidateProvider.KnownDawgSha256);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or FormatException
                or System.Security.Cryptography.CryptographicException)
            {
                // Optional preview model failures must never prevent startup.
                return new NullKbmCandidateProvider();
            }
        });
        services.AddSingleton<IKbmAuditComparisonService, KbmAuditComparisonService>();

        return services;
    }
}
