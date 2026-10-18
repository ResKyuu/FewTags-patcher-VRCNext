using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VRCNextFewTagsPatcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--replace-self")
        {
            SelfUpdate.ReplaceAndRestart(args[1], args[2], int.Parse(args[3]));
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new PatcherForm());
    }
}

internal sealed class PatcherForm : Form
{
    private const string PayloadVersion = PatcherBuildInfo.VrcNextVersion;
    private const string ReleaseApi = "https://api.github.com/repos/shinyflvre/VRCNext/releases/latest";
    private const string PatcherReleaseApi = "https://api.github.com/repos/ResKyuu/FewTags-patcher-VRCNext/releases/latest";
    private const string FewTagsRaw = "https://raw.githubusercontent.com/Fewdys/FewTags/main/";
    private const int MaxDatabaseBytes = 32 * 1024 * 1024;
    private const long MaxPatcherBytes = 1024L * 1024 * 1024;
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly TextBox _target = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly Label _status = new() { AutoSize = false, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _patch = new() { Text = "Update VRCNext", Width = 130, Height = 34 };

    public PatcherForm()
    {
        Text = "VRCNext FewTags Patcher";
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(660, 258);
        MinimumSize = new Size(560, 270);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "Install the FewTags-enabled VRCNext build",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            Left = 18,
            Top = 16
        };
        var hint = new Label
        {
            Text = $"One-file patcher · VRCNext integration {PayloadVersion}",
            AutoSize = true,
            Left = 18,
            Top = 44
        };
        _target.SetBounds(18, 82, 505, 27);
        _target.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var browse = new Button { Text = "Browse…", Left = 533, Top = 80, Width = 105, Height = 29, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browse.Click += (_, _) => BrowseTarget();
        _status.SetBounds(18, 122, 620, 56);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _patch.SetBounds(508, 196, 130, 36);
        _patch.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        AcceptButton = _patch;
        _patch.Click += async (_, _) => await ApplyPatchAsync();
        Controls.AddRange([title, hint, _target, browse, _status, _patch]);
        Shown += async (_, _) => await CheckForPatcherUpdateAsync();

        var found = FindInstall();
        if (found is not null)
        {
            _target.Text = found;
            _status.Text = "VRCNext install found. Select Update VRCNext to apply the patch.";
        }
        else
        {
            _status.Text = "Install not found automatically. Browse to the folder containing VRCNext.exe.";
        }
    }

    private void BrowseTarget()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the VRCNext install folder (the folder containing VRCNext.exe)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_target.Text)) dialog.SelectedPath = _target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.SelectedPath;
    }

    private async Task ApplyPatchAsync()
    {
        string target;
        try { target = Path.GetFullPath(_target.Text.Trim().Trim('"')); }
        catch
        {
            MessageBox.Show(this, "Select a valid VRCNext install folder.", "Install not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!File.Exists(Path.Combine(target, "VRCNext.exe")))
        {
            MessageBox.Show(this, "That folder does not contain VRCNext.exe.", "Install not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _patch.Enabled = false;
        _status.Text = "Checking for the latest VRCNext release and FewTags databases…";
        try
        {
            await EnsurePayloadSupportsLatestAsync(target);
            var databases = await DownloadLatestFewTagsAsync();

            _status.Text = "Closing VRCNext if it is running…";
            if (!await StopRunningAppAsync())
            {
                _status.Text = "Close VRCNext and run the patcher again.";
                return;
            }

            _status.Text = "Preparing the updated app…";
            var stage = Path.Combine(Path.GetTempPath(), "VRCNext-FewTags-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                var embeddedZip = Path.Combine(stage, "payload.zip");
                await ExtractEmbeddedPayloadAsync(embeddedZip);
                var payloadStage = Path.Combine(stage, "payload");
                Directory.CreateDirectory(payloadStage);
                await Task.Run(() => ZipFile.ExtractToDirectory(embeddedZip, payloadStage));
                foreach (var (name, bytes) in databases)
                    await File.WriteAllBytesAsync(Path.Combine(payloadStage, name), bytes);
                File.Delete(embeddedZip);

                var payload = FindPayloadRoot(payloadStage);
                if (payload is null) throw new InvalidDataException("The patch archive does not contain VRCNext.exe.");

                var backup = Path.Combine(target, "VRCNext-FewTags-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                var copied = 0;
                var saved = 0;
                var sourceFiles = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).ToArray();
                _status.Text = $"Updating VRCNext (0/{sourceFiles.Length})…";

                await Task.Run(() =>
                {
                    foreach (var source in sourceFiles)
                    {
                        var relative = Path.GetRelativePath(payload, source);
                        var destination = Path.Combine(target, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        if (File.Exists(destination) && FilesEqual(source, destination)) continue;
                        if (File.Exists(destination))
                        {
                            var savedPath = Path.Combine(backup, relative);
                            Directory.CreateDirectory(Path.GetDirectoryName(savedPath)!);
                            File.Copy(destination, savedPath, overwrite: true);
                            saved++;
                        }
                        File.Copy(source, destination, overwrite: true);
                        copied++;
                    }
                });

                var liveCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCNext", "FewTags");
                Directory.CreateDirectory(liveCache);
                foreach (var (name, bytes) in databases)
                    await File.WriteAllBytesAsync(Path.Combine(liveCache, name), bytes);

                _status.Text = copied == 0
                    ? "VRCNext and FewTags are already current."
                    : $"Updated {copied} files. Backup: {Path.GetFileName(backup)}";
                MessageBox.Show(this,
                    copied == 0
                        ? "VRCNext already has this integration. The newest GitHub FewTags databases are installed."
                        : $"FewTags-enabled VRCNext is installed.\n\nUpdated {copied} files.\nBacked up {saved} replaced files to:\n{backup}\n\nThe latest FewTags databases were downloaded from GitHub. Launch VRCNext as usual.",
                    copied == 0 ? "Already current" : "VRCNext updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                try { Directory.Delete(stage, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            _status.Text = "The patch could not be completed.";
            MessageBox.Show(this, ex.Message, "Patch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _patch.Enabled = true;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VRCNext-FewTags-Patcher/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private async Task CheckForPatcherUpdateAsync()
    {
        string? pendingUpdatePath = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var response = await Http.GetAsync(PatcherReleaseApi, timeout.Token);
            if (!response.IsSuccessStatusCode) return;
            using var release = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            var root = release.RootElement;
            var latestTag = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(latestTag) || string.Equals(latestTag, PatcherBuildInfo.PatcherVersion, StringComparison.OrdinalIgnoreCase)) return;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return;

            var exeAsset = assets.EnumerateArray().FirstOrDefault(asset =>
                string.Equals(asset.GetProperty("name").GetString(), "VRCNext-FewTags-Patcher.exe", StringComparison.Ordinal));
            var hashAsset = assets.EnumerateArray().FirstOrDefault(asset =>
                string.Equals(asset.GetProperty("name").GetString(), "VRCNext-FewTags-Patcher.exe.sha256", StringComparison.Ordinal));
            if (exeAsset.ValueKind != JsonValueKind.Object || hashAsset.ValueKind != JsonValueKind.Object) return;

            var version = root.TryGetProperty("body", out var body)
                ? body.GetString()?.Split('\n').FirstOrDefault(line => line.StartsWith("VRCNext release:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2).Last().Trim()
                : null;
            var question = string.IsNullOrWhiteSpace(version)
                ? $"A newer FewTags patcher ({latestTag}) is available. Update now?"
                : $"A new FewTags patcher for VRCNext {version} is available. Update and restart the patcher now?";
            if (MessageBox.Show(this, question, "Patcher update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            _patch.Enabled = false;
            _status.Text = "Downloading the latest patcher…";
            var exeUrl = exeAsset.GetProperty("browser_download_url").GetString()!;
            var hashUrl = hashAsset.GetProperty("browser_download_url").GetString()!;
            if (!IsGitHubReleaseUrl(exeUrl) || !IsGitHubReleaseUrl(hashUrl))
                throw new InvalidDataException("The release contains an unexpected patcher download URL.");
            var hashText = await Http.GetStringAsync(hashUrl, timeout.Token);
            var hashParts = hashText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (hashParts.Length != 2 || hashParts[0].Length != 64 ||
                hashParts[0].Any(character => !Uri.IsHexDigit(character)) ||
                !string.Equals(hashParts[1], "VRCNext-FewTags-Patcher.exe", StringComparison.Ordinal))
                throw new InvalidDataException("The patcher release contains an invalid SHA-256 checksum file.");
            var expectedHash = Convert.FromHexString(hashParts[0]);
            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not locate the current patcher executable.");
            var downloadPath = Path.Combine(Path.GetTempPath(), $"VRCNext-FewTags-Patcher-{Guid.NewGuid():N}.exe");
            pendingUpdatePath = downloadPath;
            using (var download = await Http.GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                download.EnsureSuccessStatusCode();
                if (download.Content.Headers.ContentLength is > MaxPatcherBytes)
                    throw new InvalidDataException("The patcher update exceeded the 1 GiB download limit.");
                await using var source = await download.Content.ReadAsStreamAsync(timeout.Token);
                await using var destination = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                var buffer = new byte[1024 * 1024];
                long totalBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, timeout.Token);
                    if (read == 0) break;
                    totalBytes += read;
                    if (totalBytes > MaxPatcherBytes)
                        throw new InvalidDataException("The patcher update exceeded the 1 GiB download limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            await using (var downloaded = File.OpenRead(downloadPath))
            {
                var actualHash = SHA256.HashData(downloaded);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    throw new InvalidDataException("The downloaded patcher did not match the SHA-256 checksum published with the release.");
            }

            var updater = new ProcessStartInfo
            {
                FileName = downloadPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            updater.ArgumentList.Add("--replace-self");
            updater.ArgumentList.Add(currentExe);
            updater.ArgumentList.Add(downloadPath);
            updater.ArgumentList.Add(Environment.ProcessId.ToString());
            var updateProcess = Process.Start(updater);
            if (updateProcess is null) throw new InvalidOperationException("Could not start the patcher update.");
            pendingUpdatePath = null;
            _status.Text = "Restarting with the updated patcher…";
            Application.Exit();
        }
        catch (OperationCanceledException)
        {
            if (pendingUpdatePath is not null)
            {
                try { File.Delete(pendingUpdatePath); } catch { }
            }
        }
        catch (Exception ex)
        {
            if (pendingUpdatePath is not null)
            {
                try { File.Delete(pendingUpdatePath); } catch { }
            }
            _patch.Enabled = true;
            _status.Text = "Patcher update check failed; the installed patcher is still available.";
            if (ex is not HttpRequestException) MessageBox.Show(this, ex.Message, "Patcher update failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsGitHubReleaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal);

    private static async Task EnsurePayloadSupportsLatestAsync(string target)
    {
        using var response = await Http.GetAsync(ReleaseApi);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var tag = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (Version.TryParse(tag, out var latest) && Version.TryParse(PayloadVersion, out var payload) && latest > payload)
            throw new InvalidOperationException(
                $"A newer VRCNext release ({latest}) is available than this patcher's integrated build ({payload}).\n\n" +
                "This patcher will not downgrade or overwrite a newer VRCNext build. A patcher built for that release is required.");

        var installedVersion = FileVersionInfo.GetVersionInfo(Path.Combine(target, "VRCNext.exe")).ProductVersion;
        if (Version.TryParse(installedVersion?.Split('+')[0], out var installed) &&
            Version.TryParse(PayloadVersion, out var integrated) && installed > integrated)
            throw new InvalidOperationException(
                $"The selected VRCNext installation ({installed}) is newer than this patcher's integrated build ({PayloadVersion}).\n\n" +
                "The patcher stopped to avoid replacing it with an older app version.");
    }

    private static async Task<Dictionary<string, byte[]>> DownloadLatestFewTagsAsync()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in new[] { "FewTagsv2.json", "FewTags.json" })
        {
            using var response = await Http.GetAsync(FewTagsRaw + name, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxDatabaseBytes)
                throw new InvalidDataException($"The latest {name} exceeded the download size limit.");

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var block = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(block);
                if (read == 0) break;
                if (buffer.Length + read > MaxDatabaseBytes)
                    throw new InvalidDataException($"The latest {name} exceeded the download size limit.");
                buffer.Write(block, 0, read);
            }

            var bytes = buffer.ToArray();
            if (!IsValidDatabase(bytes))
                throw new InvalidDataException($"GitHub returned an invalid {name}; VRCNext was not changed.");
            files[name] = bytes;
        }
        return files;
    }

    private static bool IsValidDatabase(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array || records.GetArrayLength() == 0)
                return false;
            return records.EnumerateArray().Any(record => record.TryGetProperty("UserID", out var id) &&
                id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()));
        }
        catch { return false; }
    }

    private static async Task ExtractEmbeddedPayloadAsync(string destination)
    {
        await using var source = typeof(PatcherForm).Assembly.GetManifestResourceStream("VRCNext-FewTags-Windows.zip")
            ?? throw new InvalidOperationException("The integrated VRCNext payload is missing from this patcher.");
        await using var output = File.Create(destination);
        await source.CopyToAsync(output);
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(leftStream), SHA256.HashData(rightStream));
    }

    private static async Task<bool> StopRunningAppAsync()
    {
        foreach (var process in Process.GetProcessesByName("VRCNext"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    process.CloseMainWindow();
                    await Task.Run(() => process.WaitForExit(12000));
                    if (!process.HasExited) return false;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return false; }
            }
        }
        return true;
    }

    private static string? FindPayloadRoot(string stage)
    {
        var direct = Path.Combine(stage, "VRCNext.exe");
        if (File.Exists(direct)) return stage;
        return Directory.EnumerateFiles(stage, "VRCNext.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName).FirstOrDefault(p => p is not null);
    }

    private static string? FindInstall()
    {
        var processPath = Process.GetProcessesByName("VRCNext")
            .Select(p => { try { return p.MainModule?.FileName; } catch { return null; } })
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (processPath is not null) return Path.GetDirectoryName(processPath);

        var candidates = new List<string>();
        AddRegistryInstallLocations(candidates);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var vrcNextRoot = Path.Combine(local, "VRCNext");
        var current = Path.Combine(vrcNextRoot, "current");
        if (File.Exists(Path.Combine(current, "VRCNext.exe"))) return current;

        foreach (var root in new[] { vrcNextRoot, Path.Combine(local, "Programs", "VRCNext") })
        {
            if (File.Exists(Path.Combine(root, "VRCNext.exe"))) candidates.Add(root);
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root, "app-*", SearchOption.TopDirectoryOnly))
                if (File.Exists(Path.Combine(dir, "VRCNext.exe"))) candidates.Add(dir);
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static void AddRegistryInstallLocations(List<string> candidates)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive.Name == Registry.CurrentUser.Name ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(name);
                    var display = app?.GetValue("DisplayName")?.ToString() ?? "";
                    if (!display.Contains("VRCNext", StringComparison.OrdinalIgnoreCase)) continue;
                    var location = app?.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(location) && File.Exists(Path.Combine(location, "VRCNext.exe"))) candidates.Add(location);
                }
            }
            catch { }
        }
    }
}

internal static class SelfUpdate
{
    public static void ReplaceAndRestart(string currentPath, string downloadedPath, int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.WaitForExit(60_000)) return;
        }
        catch (ArgumentException) { }

        var tempTarget = currentPath + ".new";
        File.Copy(downloadedPath, tempTarget, overwrite: true);
        File.Move(tempTarget, currentPath, overwrite: true);
        Process.Start(new ProcessStartInfo(currentPath) { UseShellExecute = true });
        try { File.Delete(downloadedPath); } catch { }
    }
}
