using Xunit;
using Xunit.Abstractions;

namespace Kendo.Tests.Chaos;

public abstract class ChaosTestBase : IClassFixture<ChaosTestFixture>
{
    protected readonly ChaosTestFixture Fixture;
    protected readonly ITestOutputHelper Output;

    protected ChaosTestBase(ChaosTestFixture fixture, ITestOutputHelper output)
    {
        Fixture = fixture;
        Output = output;
    }

    protected void SkipIfDockerUnavailable()
    {
        if (!Fixture.IsDockerAvailable)
        {
            throw new InvalidOperationException("Docker is not available in this environment");
        }
    }
}
