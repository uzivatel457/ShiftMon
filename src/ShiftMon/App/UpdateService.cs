using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ShiftMon.App;

internal sealed class UpdateInfo
{
    public required string Version { get; init; }
    public required string Notes { get; init; }
    public required string DownloadUrl { get; init; }
}

internal static class UpdateService
{
    static readonly HttpClient Http = CreateClient();

    public static void Schedule(AppSettings settings, SynchronizationContext ui)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                if (settings.LastUpdateCheck == today)
                    return;
                settings.LastUpdateCheck = today;
                settings.Save();
                var info = await FindNewerAsync().ConfigureAwait(false);
                if (info == null)
                    return;
                ui.Post(_ => _ = PromptAsync(info), null);
            }
            catch (Exception ex)
            {
                AppLog.Info("Kontrola aktualizace: " + ex.Message);
            }
        });
    }

    public static async Task CheckNowAsync()
    {
        try
        {
            var info = await FindNewerAsync().ConfigureAwait(true);
            if (info == null)
            {
                MessageBox.Show(
                    Loc.T($"Máte nejnovější verzi {AppPaths.Version}.", $"You have the latest version {AppPaths.Version}."),
                    "ShiftMon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            await PromptAsync(info).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Info("Kontrola aktualizace: " + ex.Message);
            MessageBox.Show(
                Loc.T("Aktualizaci se nepodařilo ověřit.\n", "The update could not be checked.\n") + ex.Message,
                "ShiftMon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public static async Task<UpdateInfo?> FindNewerAsync()
    {
        var url = $"https://api.github.com/repos/{AppPaths.GitHubOwner}/{AppPaths.GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
        var remote = Normalize(tag);
        if (!Version.TryParse(remote, out var remoteVersion) || !Version.TryParse(AppPaths.Version, out var localVersion))
            return null;
        if (remoteVersion <= localVersion)
            return null;

        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var asset = FindAsset(root);
        if (asset == null)
            return null;
        return new UpdateInfo { Version = remote, Notes = notes.Trim(), DownloadUrl = asset };
    }

    static async Task PromptAsync(UpdateInfo info)
    {
        using var form = new Ui.UpdatePromptForm(info.Version, string.IsNullOrWhiteSpace(info.Notes) ? ReleaseNotes.Current : info.Notes, offerInstall: true);
        if (form.ShowDialog() != DialogResult.OK)
            return;
        try
        {
            var path = await DownloadAsync(info).ConfigureAwait(true);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Application.Exit();
        }
        catch (Exception ex)
        {
            AppLog.Info("Stažení aktualizace: " + ex.Message);
            MessageBox.Show(
                Loc.T("Aktualizaci se nepodařilo stáhnout.\n", "The update could not be downloaded.\n") + ex.Message,
                "ShiftMon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    static async Task<string> DownloadAsync(UpdateInfo info)
    {
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri) || !IsTrusted(uri))
            throw new InvalidOperationException(Loc.T("Odkaz na aktualizaci není z GitHubu.", "The update link is not from GitHub."));

        var destination = Path.Combine(Path.GetTempPath(), "ShiftMon-Setup-" + info.Version + ".exe");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
        await using var file = File.Create(destination);
        await input.CopyToAsync(file).ConfigureAwait(true);
        return destination;
    }

    static string? FindAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets))
            return null;
        string? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                continue;
            if (name.Equals(AppPaths.SetupFileName, StringComparison.OrdinalIgnoreCase))
                return url;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fallback = url;
        }

        return fallback;
    }

    static bool IsTrusted(Uri uri)
    {
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;
        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string value)
    {
        var version = value.Trim().TrimStart('v', 'V');
        var dash = version.IndexOf('-');
        return dash >= 0 ? version[..dash] : version;
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShiftMon/" + AppPaths.Version);
        return client;
    }
}
