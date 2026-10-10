using LogiSteam.Common.Dashboard;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.Tray;
using LogiSteam.Common.Updates;

namespace LogiSteam.Common.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("2.1.0", "2.0.1", true)]
    [InlineData("v2.1.0", "2.0.1", true)]
    [InlineData("2.0.10", "2.0.9", true)] // numeric, not alphabetical
    [InlineData("2.1", "2.1.0", false)] // same version written two ways
    [InlineData("2.0.1", "2.0.1", false)]
    [InlineData("2.0.0", "2.0.1", false)]
    [InlineData(null, "2.0.1", false)]
    [InlineData("2.1.0", null, false)]
    [InlineData("not a version", "2.0.1", false)]
    public void IsNewer_ComparesVersionsNumerically_AndIsFalseWhenEitherIsUnknown(string? candidate, string? than, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsNewer(candidate, than));
    }

    [Fact]
    public void Messages_RoundTripTheSendersVersion_AndOlderOnesParseWithoutIt()
    {
        var beacon = new BeaconMessage { HostName = "W", BusId = "1-1", VendorId = "046d", ProductId = "c24f", AppVersion = "2.1.0" };
        Assert.True(BeaconMessage.TryParse(beacon.ToJson(), out var parsedBeacon));
        Assert.Equal("2.1.0", parsedBeacon!.AppVersion);

        var ack = new AckMessage { HostName = "G", AppVersion = "2.1.0" };
        Assert.True(AckMessage.TryParse(ack.ToJson(), out var parsedAck));
        Assert.Equal("2.1.0", parsedAck!.AppVersion);

        Assert.True(AckMessage.TryParse("""{"protocolVersion":1,"hostName":"G","attached":true}""", out var oldAck));
        Assert.Null(oldAck!.AppVersion);
    }
}

public class UpdateCheckerTests
{
    private const string Url = "https://github.com/BigSquidAUS/LogiSteam/releases/download/v2.1.0/LogiSteam-Setup.exe";

    private static string ReleaseJson(
        string tag = "v2.1.0", string assetName = "LogiSteam-Setup.exe", string url = Url, long size = 80_000_000,
        string? digest = "sha256:3129d62cba25cd8a200c6d96fcb4ec46f8a51fce46fca05b47cfd26b8337885a", bool draft = false, bool prerelease = false)
    {
        var digestJson = digest is null ? "null" : $"\"{digest}\"";
        return $$"""
            {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},
             "assets":[{"name":"notes.txt","browser_download_url":"https://github.com/BigSquidAUS/LogiSteam/releases/download/v2.1.0/notes.txt","size":10},
                       {"name":"{{assetName}}","browser_download_url":"{{url}}","size":{{size}},"digest":{{digestJson}}}]}
            """;
    }

    [Fact]
    public void ParseLatestRelease_ReadsVersionInstallerAndChecksum()
    {
        var release = UpdateChecker.ParseLatestRelease(ReleaseJson());

        Assert.NotNull(release);
        Assert.Equal("2.1.0", release!.Version);
        Assert.Equal(Url, release.InstallerUrl.ToString());
        Assert.Equal(80_000_000, release.InstallerSize);
        Assert.Equal("3129D62CBA25CD8A200C6D96FCB4EC46F8A51FCE46FCA05B47CFD26B8337885A", release.Sha256);
    }

    [Fact]
    public void ParseLatestRelease_AcceptsAReleaseWithoutAChecksum()
    {
        var release = UpdateChecker.ParseLatestRelease(ReleaseJson(digest: null));

        Assert.NotNull(release);
        Assert.Null(release!.Sha256);
    }

    [Fact]
    public void ParseLatestRelease_IgnoresDraftsAndPreReleases()
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(draft: true)));
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(prerelease: true)));
    }

    [Theory]
    [InlineData("http://github.com/BigSquidAUS/LogiSteam/releases/download/v2.1.0/LogiSteam-Setup.exe")] // not https
    [InlineData("https://github.com/SomeoneElse/LogiSteam/releases/download/v2.1.0/LogiSteam-Setup.exe")] // another account
    [InlineData("https://github.com.evil.example/BigSquidAUS/LogiSteam/releases/download/v2.1.0/LogiSteam-Setup.exe")]
    [InlineData("https://example.com/LogiSteam-Setup.exe")]
    public void ParseLatestRelease_RejectsAnInstallerHostedAnywhereElse(string url)
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(url: url)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]
    public void ParseLatestRelease_ReturnsNull_ForAnythingUnexpected(string json)
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(json));
    }

    [Fact]
    public void ParseLatestRelease_ReturnsNull_WhenTheInstallerIsMissingOrTheTagIsNotAVersion()
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(assetName: "Something-Else.exe")));
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(tag: "nightly")));
        Assert.Null(UpdateChecker.ParseLatestRelease(ReleaseJson(size: 0)));
    }

    [Fact]
    public void Verify_AcceptsAMatchingFile_AndRejectsAWrongSizeOrChecksum()
    {
        var path = Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4]));
            var good = new AvailableRelease("2.1.0", new Uri(Url), 4, hash);

            UpdateChecker.Verify(path, good); // does not throw
            UpdateChecker.Verify(path, good with { Sha256 = null }); // size alone, when GitHub gave no checksum

            Assert.Throws<UpdateDownloadException>(() => UpdateChecker.Verify(path, good with { InstallerSize = 5 }));
            Assert.Throws<UpdateDownloadException>(() => UpdateChecker.Verify(path, good with { Sha256 = new string('0', 64) }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class UpdateNoticesTests
{
    private static readonly DashboardState Connected = new("Wheel connected", "All good.", TrayStatusLevel.Connected, [], []);

    [Fact]
    public void NothingIsAdded_WhenBothPcsAreUpToDate()
    {
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.1.0", "2.1.0", "OTHER-PC", "2.1.0"));

        Assert.Same(Connected, state);
    }

    [Fact]
    public void OffersAnUpdate_WhenANewerReleaseExists()
    {
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.0.1", "2.1.0"));

        var notice = Assert.Single(state.Warnings);
        Assert.Equal(UpdateNotices.UpdateId, notice.Id);
        Assert.Equal("LogiSteam 2.1.0 is available", notice.Title);
        Assert.Equal("Update", notice.ActionLabel);
        Assert.Equal(TrayStatusLevel.Connected, state.Level); // an update never makes a working connection look broken
    }

    [Fact]
    public void SaysTheOtherPcIsAlreadyUpdated_WhenThatIsWhyThisOneShouldUpdate()
    {
        // The scenario the feature exists for: the new version was installed on the other PC.
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.0.1", "2.1.0", "MEDIA-PC", "2.1.0"));

        var notice = Assert.Single(state.Warnings);
        Assert.Equal(UpdateNotices.UpdateId, notice.Id);
        Assert.Contains("MEDIA-PC is already on 2.1.0", notice.Detail);
    }

    [Fact]
    public void TellsTheUpToDatePc_ThatTheOtherOneIsBehind()
    {
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.1.0", "2.1.0", "MEDIA-PC", "2.0.5"));

        var notice = Assert.Single(state.Warnings);
        Assert.Equal(UpdateNotices.PeerOutdatedId, notice.Id);
        Assert.Contains("MEDIA-PC", notice.Title);
        Assert.Contains("choose Update", notice.Detail);
        Assert.Null(notice.ActionLabel);
    }

    [Fact]
    public void TellsTheUser_WhenTheOtherPcIsTooOldToUpdateItself()
    {
        // Versions before 2.1 don't report a version and have no Update button.
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.1.0", "2.1.0", "MEDIA-PC", PeerVersion: null));

        var notice = Assert.Single(state.Warnings);
        Assert.Equal(UpdateNotices.PeerOutdatedId, notice.Id);
        Assert.Contains("Install the latest version there once", notice.Detail);
    }

    [Fact]
    public void ShowsProgress_WhileDownloading_WithNoButton()
    {
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.0.1", "2.1.0", Phase: UpdatePhase.Downloading, Progress: 0.43));

        var notice = Assert.Single(state.Warnings);
        Assert.Contains("43", notice.Title);
        Assert.Null(notice.ActionLabel);
    }

    [Fact]
    public void ShowsWhyAnUpdateFailed_AndOffersToTryAgain()
    {
        var state = UpdateNotices.Apply(Connected, new UpdateStatus("2.0.1", "2.1.0", Error: "The update couldn't be downloaded."));

        var notice = Assert.Single(state.Warnings);
        Assert.Equal("The update couldn't be downloaded.", notice.Detail);
        Assert.Equal("Try again", notice.ActionLabel);
    }

    [Fact]
    public void KeepsExistingWarnings()
    {
        var withWarning = Connected with { Warnings = [new DashboardWarning(GHubWarnings.LocalId, "G HUB", "x", "Open G HUB")] };

        var state = UpdateNotices.Apply(withWarning, new UpdateStatus("2.0.1", "2.1.0"));

        Assert.Equal([GHubWarnings.LocalId, UpdateNotices.UpdateId], state.Warnings.Select(w => w.Id));
    }
}
