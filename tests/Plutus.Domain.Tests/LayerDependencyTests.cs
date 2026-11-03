namespace Plutus.Domain.Tests;

public class LayerDependencyTests
{
    [Fact]
    public void Domain_ShouldNotDependOnOtherProjectLayers()
    {
        var referencedAssemblies = typeof(AssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Plutus.", StringComparison.Ordinal));
    }
}
