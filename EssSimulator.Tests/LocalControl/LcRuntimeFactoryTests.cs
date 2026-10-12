using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcRuntimeFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("standard")]
    [InlineData("unknown-model")]
    [InlineData("trina_5.5MW")]
    [InlineData("trina_10MW")]
    [InlineData("black_start")]
    public void Create_IgnoresModelId_AlwaysProtocolBridge(string? modelId)
    {
        var runtime = LcRuntimeFactory.Create(modelId);
        Assert.IsType<StandardLcRuntime>(runtime);
    }
}
