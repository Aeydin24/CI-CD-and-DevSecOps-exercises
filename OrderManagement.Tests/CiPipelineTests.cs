using Xunit;

namespace OrderManagement.Tests;

public class CiPipelineTests
{
    [Fact]
    public void DemonstrateFailingTest()
    {
        Assert.True(false, "Intentional failure for CI exercise");
    }
}
