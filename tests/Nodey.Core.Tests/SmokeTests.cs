using System.Reflection;

namespace Nodey.Core.Tests;

/// <summary>
/// Smoke test: the test project builds and runs against Nodey.Core.
/// Engine/graph tests land in STEP-2/STEP-3.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void CoreAssembly_IsLoadable()
    {
        var asm = Assembly.Load("Nodey.Core");

        Assert.NotNull(asm);
        Assert.Equal("Nodey.Core", asm.GetName().Name);
    }
}
