using Microsoft.Extensions.Configuration;
using SmartLost.AI.Configuration;
using Xunit;

namespace DinoVision.Tests;

public sealed class ExecutionOptionsTests
{
    [Fact]
    public void From_WithEmptyConfiguration_DefaultsToCpuAndDeviceZero()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        var options = ExecutionOptions.From(config);

        Assert.Equal("cpu", options.Provider);
        Assert.Equal(0, options.DeviceId);
        Assert.False(options.UsesCuda);
    }

    [Fact]
    public void From_WithExplicitCpu_SelectsCpu()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Execution:Provider"] = "CPU",
            ["Execution:DeviceId"] = "1"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var options = ExecutionOptions.From(config);

        Assert.Equal("cpu", options.Provider);
        Assert.Equal(1, options.DeviceId);
        Assert.False(options.UsesCuda);
    }

    [Fact]
    public void From_WithCuda_SelectsCudaAndConfiguredDeviceId()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Execution:Provider"] = "cuda",
            ["Execution:DeviceId"] = "2"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var options = ExecutionOptions.From(config);

        Assert.Equal("cuda", options.Provider);
        Assert.Equal(2, options.DeviceId);
        Assert.True(options.UsesCuda);
    }

    [Fact]
    public void From_WithInvalidProvider_ThrowsArgumentException()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Execution:Provider"] = "directml"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => ExecutionOptions.From(config));
        Assert.Contains("Execution:Provider must be either 'cpu' or 'cuda'.", exception.Message);
    }

    [Fact]
    public void From_WithNegativeDeviceId_ThrowsArgumentException()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Execution:Provider"] = "cuda",
            ["Execution:DeviceId"] = "-1"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => ExecutionOptions.From(config));
        Assert.Contains("Execution:DeviceId must be a non-negative integer.", exception.Message);
    }

    [Fact]
    public void From_WithNonNumericDeviceId_ThrowsArgumentException()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Execution:Provider"] = "cuda",
            ["Execution:DeviceId"] = "gpu0"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => ExecutionOptions.From(config));
        Assert.Contains("Execution:DeviceId must be a non-negative integer.", exception.Message);
    }
}
