using InternetMeter.Services;

namespace InternetMeter.Tests;

public sealed class SpeedFormatterTests
{
    [Theory]
    [InlineData(0, "0.0 B/s")]
    [InlineData(999, "999 B/s")]
    [InlineData(1_000, "1.0 KB/s")]
    [InlineData(12_500, "12.5 KB/s")]
    [InlineData(1_250_000, "1.2 MB/s")]
    public void FormatsByteRates(double input, string expected) =>
        Assert.Equal(expected, SpeedFormatter.Format(input));

    [Fact]
    public void FormatsBitRates() =>
        Assert.Equal("8.0 Mb/s", SpeedFormatter.Format(1_000_000, bits: true));

    [Fact]
    public void InvalidValuesBecomeZero()
    {
        Assert.Equal("0.0 B/s", SpeedFormatter.Format(double.NaN));
        Assert.Equal("0.0 B/s", SpeedFormatter.Format(-42));
    }
}

