using System.Reflection;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class InfrastructureTestHarnessTests
{
    [Fact]
    public void Infrastructure_assembly_is_available_to_the_test_harness()
    {
        var assembly = Assembly.Load("Gw2Tp.Infrastructure");

        Assert.Equal("Gw2Tp.Infrastructure", assembly.GetName().Name);
    }
}
