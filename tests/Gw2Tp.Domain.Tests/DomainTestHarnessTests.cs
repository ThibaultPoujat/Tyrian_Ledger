using System.Reflection;
using Xunit;

namespace Gw2Tp.Domain.Tests;

public sealed class DomainTestHarnessTests
{
    [Fact]
    public void Domain_assembly_is_available_to_the_test_harness()
    {
        var assembly = Assembly.Load("Gw2Tp.Domain");

        Assert.Equal("Gw2Tp.Domain", assembly.GetName().Name);
    }
}
