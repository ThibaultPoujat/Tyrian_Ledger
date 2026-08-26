using System.Reflection;
using Xunit;

namespace Gw2Tp.Analytics.Tests;

public sealed class AnalyticsTestHarnessTests
{
    [Fact]
    public void Analytics_assembly_is_available_to_the_test_harness()
    {
        var assembly = Assembly.Load("Gw2Tp.Analytics");

        Assert.Equal("Gw2Tp.Analytics", assembly.GetName().Name);
    }
}
