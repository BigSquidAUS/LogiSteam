using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LogiSteam.Common.Updates;

/// <summary>A published LogiSteam release that can be installed.</summary>
/// <param name="Version">"2.1.0".</param>
/// <param name="Sha256">The installer's SHA-256 as GitHub reports it (upper-case hex), or null if it didn't.</param>
public sealed record AvailableRelease(string Version, Uri InstallerUrl, long InstallerSize, string? Sha256);

/// <summary>
/// Finds and downloads LogiSteam updates from the project's GitHub releases -- the only place an
/// update is ever taken from. The other PC on the LAN can say "I'm running a newer version",
/// which prompts a check here, but it is never the source of the installer: accepting a program
/// to run from whatever machine answers on the network would hand any device on the LAN a way
/// to run code on this PC.
/// </summary>
public sealed class UpdateChecker(HttpClient? httpClient = null)
{
    public const string Repository = "BigSquidAUS/LogiSteam";
    public const string InstallerAssetName = "LogiSteam-Setup.exe";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";

    // Installers are ~80 MB; anything wildly larger isn't one.
    private const long MaxInstallerBytes = 500L * 1024 * 1024;

    private readonly HttpClient _http = httpClient ?? CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LogiSteam", AppVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>The latest published release, or null if it can't be determined right now (offline, rate-limited).</summary>
    public async Task<AvailableRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ParseLatestRelease(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pure: reads GitHub's "latest release" JSON. Returns null unless it is a normal (not draft,
    /// not pre-release) release with a parseable version tag and a LogiSteam-Setup.exe asset
    /// hosted under this repository's own release downloads.
    /// </summary>
    public static AvailableRelease? ParseLatestRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || IsTrue(root, "draft") || IsTrue(root, "prerelease"))
            {
                return null;
            }

            var version = AppVersion.Parse(GetString(root, "tag_name"));
            if (version is null || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (GetString(asset, "name") != InstallerAssetName
                    || !Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var url)
                    || !IsOfficialDownloadUrl(url)
                    || !asset.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size)
                    || size <= 0 || size > MaxInstallerBytes)
                {
                    continue;
                }

                var digest = GetString(asset, "digest");
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..].ToUpperInvariant()
                    : null;
                return new AvailableRelease($"{version.Major}.{version.Minor}.{version.Build}", url, size, sha256);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Only https://github.com/BigSquidAUS/LogiSteam/releases/download/... is ever downloaded and run.</summary>
    public static bool IsOfficialDownloadUrl(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads the release's installer to a temp file and returns its path. Throws
    /// <see cref="UpdateDownloadException"/> if the download fails or the file isn't exactly what
    /// GitHub described (size, and SHA-256 when GitHub provided one) -- a file that doesn't
    /// verify is deleted, never run.
    /// </summary>
    public async Task<string> DownloadInstallerAsync(AvailableRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsOfficialDownloadUrl(release.InstallerUrl))
        {
            throw new UpdateDownloadException("The update's download address isn't LogiSteam's official release page.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "LogiSteam-Update");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"LogiSteam-Setup-{release.Version}.exe");

        try
        {
            using (var response = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(path);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > release.InstallerSize)
                    {
                        throw new UpdateDownloadException("The downloaded update is larger than expected.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    progress?.Report((double)total / release.InstallerSize);
                }
            }

            Verify(path, release);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UpdateDownloadException)
        {
            TryDelete(path);
            throw ex as UpdateDownloadException ?? new UpdateDownloadException($"The update couldn't be downloaded ({ex.Message}).");
        }
    }

    /// <summary>Checks a downloaded installer against what the release said it should be.</summary>
    public static void Verify(string path, AvailableRelease release)
    {
        if (new FileInfo(path).Length != release.InstallerSize)
        {
            throw new UpdateDownloadException("The downloaded update is incomplete.");
        }

        if (release.Sha256 is not null)
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateDownloadException("The downloaded update didn't match its published checksum, so it was discarded.");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the temp folder; the next attempt overwrites it.
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class UpdateDownloadException(string message) : Exception(message);
