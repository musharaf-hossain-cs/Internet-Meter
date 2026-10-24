using InternetMeter.Services;

namespace InternetMeter.Tests;

public sealed class SpeedCalculatorTests
{
    [Fact]
    public void CalculatesDeltaOverActualElapsedTime()
    {
        var calculator = new SpeedCalculator(1);
        var result = calculator.Calculate(2_000_000, 500_000, TimeSpan.FromSeconds(2));
        Assert.Equal(1_000_000, result.Download);
        Assert.Equal(250_000, result.Upload);
    }

    [Fact]
    public void AppliesExponentialSmoothing()
    {
        var calculator = new SpeedCalculator(0.5);
        calculator.Calculate(1_000, 0, TimeSpan.FromSeconds(1));
        var result = calculator.Calculate(3_000, 0, TimeSpan.FromSeconds(1));
        Assert.Equal(2_000, result.Download);
    }

    [Fact]
    public void IgnoresCounterRollover()
    {
        var calculator = new SpeedCalculator(1);
        calculator.Calculate(1_000, 500, TimeSpan.FromSeconds(1));
        var result = calculator.Calculate(-1, 2, TimeSpan.FromSeconds(1));
        Assert.Equal(1_000, result.Download);
        Assert.Equal(500, result.Upload);
    }
}

