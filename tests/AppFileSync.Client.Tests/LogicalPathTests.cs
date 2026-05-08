using FluentAssertions;

namespace AppFileSync.Client.Tests;

public sealed class LogicalPathTests
{
    [Theory]
    [InlineData(@"settings\profile.json", "settings/profile.json")]
    [InlineData("/settings//profile.json", "settings/profile.json")]
    [InlineData(" settings.json ", "settings.json")]
    public void Normalize_WhenPathIsValid_ShouldReturnStableRelativePath(string input, string expected)
    {
        LogicalPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("../settings.json")]
    [InlineData("settings/../secrets.json")]
    public void Normalize_WhenPathEscapesNamespace_ShouldRejectIt(string input)
    {
        var act = () => LogicalPath.Normalize(input);

        act.Should().Throw<ArgumentException>();
    }
}
