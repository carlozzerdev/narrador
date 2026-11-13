using Narrador.Application;
using Narrador.Domain;

namespace Narrador.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void InnerLayersDoNotReferenceInfrastructureOrPresentation()
    {
        var domainReferences = typeof(SubtitleCue).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(domainReferences, assembly => assembly.Name!.StartsWith("Narrador."));
        var applicationReferences = typeof(GenerateNarration).Assembly.GetReferencedAssemblies();
        Assert.Contains(applicationReferences, assembly => assembly.Name == "Narrador.Domain");
        Assert.DoesNotContain(applicationReferences, assembly => assembly.Name is "Narrador.Infrastructure" or "Narrador.WinForms" or "System.Windows.Forms");
    }
}
