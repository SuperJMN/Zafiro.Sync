using System.Text;
using FluentAssertions;

namespace Zafiro.Sync.Client.Tests;

public sealed class FileIdGeneratorTests
{
    [Fact]
    public void CreateFileId_WhenPathUsesDifferentSeparators_ShouldReturnSameOpaqueId()
    {
        var key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");

        var first = FileIdGenerator.CreateFileId(key, @"settings\profile.json");
        var second = FileIdGenerator.CreateFileId(key, "settings/profile.json");

        first.Should().Be(second);
        first.Should().NotContain("settings");
        first.Should().NotContain("profile");
    }

    [Fact]
    public void CreateFileId_WhenKeyChanges_ShouldReturnDifferentOpaqueId()
    {
        var firstKey = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");
        var secondKey = Encoding.UTF8.GetBytes("abcdef0123456789abcdef0123456789");

        var first = FileIdGenerator.CreateFileId(firstKey, "settings.json");
        var second = FileIdGenerator.CreateFileId(secondKey, "settings.json");

        first.Should().NotBe(second);
    }
}
