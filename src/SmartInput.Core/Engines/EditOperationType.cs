namespace SmartInput.Core.Engines;

public enum EditOperationType
{
    Unknown,
    RepeatedAccidentalCharacter,
    AdjacentTransposition,
    MissingCharacter,
    ExtraCharacter,
    AdjacentKeySubstitution,
    VowelSubstitution,
    GeneralSubstitution,
    DirectLayout,
    CombinedLayoutSpelling,
    UnknownNameLayout,
}

public enum MutationOracleClass
{
    UniquelyRecoverable,
    Ambiguous,
    MutationIsExactKnownWord,
    InvalidMutation,
    ProtectedMutation,
    CrossLanguageCollision,
}
