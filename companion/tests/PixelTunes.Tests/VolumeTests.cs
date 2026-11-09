using PixelTunes.Core;
using Xunit;

namespace PixelTunes.Tests;

public class VolumeTests
{
    [Theory]
    [InlineData(0.63f, true, 0.65f)]
    [InlineData(0.65f, true, 0.70f)]
    [InlineData(0.63f, false, 0.60f)]
    [InlineData(0.65f, false, 0.60f)]
    [InlineData(1.00f, true, 1.00f)]
    [InlineData(0.98f, true, 1.00f)]
    [InlineData(0.00f, false, 0.00f)]
    [InlineData(0.03f, false, 0.00f)]
    [InlineData(0.00f, true, 0.05f)]
    public void StepsSnapToFivePercentGrid(float current, bool up, float expected)
    {
        Assert.Equal((double)expected, (double)VolumeMath.Step(current, up, 5), 3);
    }

    [Fact]
    public void RepeatedStepsWalkTheWholeRange()
    {
        float v = 0;
        for (int i = 0; i < 20; i++) v = VolumeMath.Step(v, true, 5);
        Assert.Equal(100, VolumeMath.ToPercent(v));
        for (int i = 0; i < 20; i++) v = VolumeMath.Step(v, false, 5);
        Assert.Equal(0, VolumeMath.ToPercent(v));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify", "")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", "")]
    [InlineData("Chrome", "chrome", "")]
    [InlineData("MSEdge", "msedge", "")]
    [InlineData("308046B0AF4A39CB", "firefox", "")]
    [InlineData("vlc.exe", "vlc", "")]
    [InlineData(@"C:\Program Files\foobar2000\foobar2000.exe", "foobar2000", "")]
    public void MatchesDesktopAppsByProcessName(string aumid, string process, string sessionId)
    {
        var m = AudioAppMatcher.ForMediaApp(aumid);
        Assert.True(m.Matches(process, sessionId), $"{aumid} should match process {process}");
        Assert.False(m.Matches("Discord", sessionId));
    }

    [Fact]
    public void MatchesStoreAppsBySessionIdentifier()
    {
        var m = AudioAppMatcher.ForMediaApp("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic");
        var sessionId = @"{0.0.0.00000000}.{guid}|\Device\HarddiskVolume3\Program Files\WindowsApps\Microsoft.ZuneMusic_11.2408.8.0_x64__8wekyb3d8bbwe\Microsoft.Media.Player.exe%b{guid}";
        Assert.True(m.Matches("Microsoft.Media.Player", sessionId));
        Assert.False(m.Matches("Microsoft.Media.Player", @"\Device\HarddiskVolume3\Something\else.exe"));
        Assert.Equal("ZuneMusic", m.DisplayName);
    }

    [Theory]
    [InlineData("Wow")]
    [InlineData("WowClassic")]
    [InlineData("wow")]
    public void NeverTouchesWowItself(string wowProcess)
    {
        // Even a matcher that would otherwise match by name must not adjust the game.
        var m = AudioAppMatcher.ForMediaApp(wowProcess + ".exe");
        Assert.False(m.Matches(wowProcess, ""));
    }
}
