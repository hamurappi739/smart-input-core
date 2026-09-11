namespace SmartInput.Core.Engines;

/// <summary>
/// Documented production edit-cost table. All production generators and indexes
/// must read costs from here rather than scattering literals.
///
/// Costs follow <see cref="VerificationEditProvenance"/> strength order:
/// repeated-accidental is cheapest; adjacent-key and transposition beat
/// generic insert/delete; ExtraCharacter matches MissingCharacter (not repeated).
/// </summary>
public static class ProductionEditCostTable
{
    public const double RepeatedAccidentalCharacter = 0.35;
    public const double AdjacentTransposition = 0.75;
    public const double AdjacentKeySubstitution = 0.65;
    public const double VowelSubstitution = 0.80;
    public const double MissingCharacter = 1.0;
    /// <summary>Non-repeated single insertion. Must not share the repeated-accidental cost.</summary>
    public const double ExtraCharacter = 1.0;
    public const double GeneralSubstitution = 1.0;
    public const double VowelInsertion = 0.90;
    public const double Unknown = 2.0;

    public static double ForOperation(EditOperationType operation)
        => operation switch
        {
            EditOperationType.RepeatedAccidentalCharacter => RepeatedAccidentalCharacter,
            EditOperationType.AdjacentTransposition => AdjacentTransposition,
            EditOperationType.AdjacentKeySubstitution => AdjacentKeySubstitution,
            EditOperationType.VowelSubstitution => VowelSubstitution,
            EditOperationType.MissingCharacter => MissingCharacter,
            EditOperationType.ExtraCharacter => ExtraCharacter,
            EditOperationType.GeneralSubstitution => GeneralSubstitution,
            _ => Unknown,
        };
}
