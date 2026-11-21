using System.Reflection;
using HoodKeys.Core;

namespace HoodKeys.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Core_does_not_reference_System_Console()
    {
        var core = typeof(ProductInfo).Assembly;
        var referenced = core.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("System.Console", referenced);
    }

    [Fact]
    public void Core_types_do_not_use_System_Console_type()
    {
        var core = typeof(ProductInfo).Assembly;
        var consoleType = typeof(Console);

        var offenders = core.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => f.FieldType))
            .Where(t => t == consoleType);

        Assert.Empty(offenders);
    }
}
