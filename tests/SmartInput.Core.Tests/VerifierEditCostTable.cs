namespace SmartInput.Core.Tests;

/// <summary>
/// Separately implemented verifier cost table. Must stay numerically equivalent to
/// <see cref="Engines.ProductionEditCostTable"/> via equivalence tests, but must not
/// share the same static field declarations.
/// </summary>
internal static class VerifierEditCostTable
{
    // Intentionally duplicated literals — do not reference ProductionEditCostTable here.
    internal const double RepeatedAccidentalCharacter = 0.35;
    internal const double AdjacentTransposition = 0.75;
    internal const double AdjacentKeySubstitution = 0.65;
    internal const double VowelSubstitution = 0.80;
    internal const double MissingCharacter = 1.0;
    // Non-repeated single insertion — mirrors ProductionEditCostTable.ExtraCharacter.
    internal const double ExtraCharacter = 1.0;
    internal const double GeneralSubstitution = 1.0;
    internal const double VowelInsertion = 0.90;
    internal const double Unknown = 2.0;

    internal static double ForOperation(SmartInput.Core.Engines.EditOperationType operation)
        => operation switch
        {
            SmartInput.Core.Engines.EditOperationType.RepeatedAccidentalCharacter => RepeatedAccidentalCharacter,
            SmartInput.Core.Engines.EditOperationType.AdjacentTransposition => AdjacentTransposition,
            SmartInput.Core.Engines.EditOperationType.AdjacentKeySubstitution => AdjacentKeySubstitution,
            SmartInput.Core.Engines.EditOperationType.VowelSubstitution => VowelSubstitution,
            SmartInput.Core.Engines.EditOperationType.MissingCharacter => MissingCharacter,
            SmartInput.Core.Engines.EditOperationType.ExtraCharacter => ExtraCharacter,
            SmartInput.Core.Engines.EditOperationType.GeneralSubstitution => GeneralSubstitution,
            _ => Unknown,
        };
}
