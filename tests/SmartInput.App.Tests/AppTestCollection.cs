using Xunit;

namespace SmartInput.App.Tests;

[CollectionDefinition(AppTestCollection.Name, DisableParallelization = true)]
public sealed class AppTestCollectionDefinition : ICollectionFixture<AvaloniaTestFixture>
{
}

public static class AppTestCollection
{
    public const string Name = "SmartInput.App.Tests";
}
