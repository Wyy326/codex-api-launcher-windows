using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace CodexApiLauncher.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "-SmokeTest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunSmokeTest());
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }

    private static int RunSmokeTest()
    {
        try
        {
            var bridge = new PowerShellBridge(AppContext.BaseDirectory);
            _ = bridge.GetProfiles();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}

internal sealed class LauncherForm : Form
{
    private readonly PowerShellBridge bridge = new(AppContext.BaseDirectory);
    private readonly Dictionary<string, ProfileInfo> profileMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly Color windowColor = Color.FromArgb(246, 247, 245);
    private readonly Color surfaceColor = Color.White;
    private readonly Color borderColor = Color.FromArgb(214, 219, 216);
    private readonly Color textColor = Color.FromArgb(31, 35, 33);
    private readonly Color mutedColor = Color.FromArgb(87, 94, 91);
    private readonly Color primaryColor = Color.FromArgb(28, 98, 86);
    private readonly Color primaryDarkColor = Color.FromArgb(17, 70, 61);
    private readonly Color softColor = Color.FromArgb(246, 249, 248);

    private ListBox profileList = null!;
    private Label providerNameLabel = null!;
    private Label providerMetaLabel = null!;
    private Label savedProjectLabel = null!;
    private TextBox workspaceBox = null!;
    private CheckBox rememberCheck = null!;
    private Button startButton = null!;
    private Button cliCheckButton = null!;
    private Button httpTestButton = null!;
    private Button saveProjectButton = null!;
    private Button clearProjectButton = null!;
    private Button homeButton = null!;
    private TextBox statusText = null!;

    private bool isBusy;

    public LauncherForm()
    {
        BuildUi();
        Shown += async (_, _) => await RefreshProfilesAsync();
    }

    private Font UiFont(float size = 9.0f, FontStyle style = FontStyle.Regular)
    {
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fontName = families.Contains("Microsoft YaHei UI") ? "Microsoft YaHei UI" : "Segoe UI";
        return new Font(fontName, size, style);
    }

    private void BuildUi()
    {
        Text = "CodexCLI API 多开启动器";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 680);
        MinimumSize = new Size(940, 620);
        BackColor = windowColor;
        Font = UiFont();

        var headerTitle = NewLabel("CodexCLI API 多开启动器", 24, 18, 420, 34, 16, FontStyle.Bold);
        Controls.Add(headerTitle);

        var headerText = NewLabel("选择 API 配置和项目文件夹，然后用独立 CODEX_HOME 启动 Codex CLI。", 24, 54, 840, 24, 9, FontStyle.Regular, mutedColor);
        Controls.Add(headerText);

        var leftPanel = NewPanel(24, 94, 300, 520);
        Controls.Add(leftPanel);

        var rightPanel = NewPanel(342, 94, 622, 520);
        Controls.Add(rightPanel);

        leftPanel.Controls.Add(NewLabel("API 配置列表", 16, 14, 160, 26, 11, FontStyle.Bold));

        var refreshButton = NewButton("刷新", 194, 12, 84, 30);
        refreshButton.Click += async (_, _) => await RefreshProfilesAsync();
        leftPanel.Controls.Add(refreshButton);

        profileList = new ListBox
        {
            Location = new Point(16, 52),
            Size = new Size(262, 306),
            Font = UiFont(9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = softColor,
            HorizontalScrollbar = true
        };
        profileList.SelectedIndexChanged += (_, _) => UpdateSelectedProfile();
        leftPanel.Controls.Add(profileList);

        leftPanel.Controls.Add(NewLabel("每个配置都有独立的 API 凭据、CODEX_HOME、会话和日志。项目文件夹在启动时选择。", 16, 372, 262, 54, 9, FontStyle.Regular, mutedColor));

        var openLaunchersButton = NewButton("打开快捷启动脚本目录", 16, 454, 262, 34);
        openLaunchersButton.Click += (_, _) => OpenFolder(bridge.GetLaunchersDir());
        leftPanel.Controls.Add(openLaunchersButton);

        rightPanel.Controls.Add(NewLabel("当前 API 配置", 20, 16, 200, 28, 12, FontStyle.Bold));

        providerNameLabel = NewLabel("尚未选择配置", 20, 48, 372, 24, 10, FontStyle.Bold);
        rightPanel.Controls.Add(providerNameLabel);

        providerMetaLabel = NewLabel("", 20, 74, 570, 42, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(providerMetaLabel);

        homeButton = NewButton("打开 CODEX_HOME", 438, 42, 154, 32);
        homeButton.Click += (_, _) =>
        {
            var profile = SelectedProfile();
            if (profile is not null)
            {
                OpenFolder(profile.CodexHome);
            }
        };
        rightPanel.Controls.Add(homeButton);

        rightPanel.Controls.Add(NewSeparator(20, 124, 572));
        rightPanel.Controls.Add(NewLabel("项目文件夹", 20, 146, 180, 26, 11, FontStyle.Bold));

        savedProjectLabel = NewLabel("已保存默认项目: 无", 20, 174, 570, 24, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(savedProjectLabel);

        workspaceBox = new TextBox
        {
            Location = new Point(20, 204),
            Size = new Size(456, 28),
            Font = UiFont(9.5f)
        };
        workspaceBox.TextChanged += (_, _) => UpdateButtons();
        rightPanel.Controls.Add(workspaceBox);

        var browseButton = NewButton("选择文件夹", 488, 200, 104, 34, primary: true);
        browseButton.Click += (_, _) => BrowseWorkspace();
        rightPanel.Controls.Add(browseButton);

        rememberCheck = new CheckBox
        {
            Text = "启动时顺便记住这个项目文件夹",
            Location = new Point(20, 244),
            Size = new Size(300, 24),
            Font = UiFont(),
            BackColor = surfaceColor
        };
        rightPanel.Controls.Add(rememberCheck);

        saveProjectButton = NewButton("保存默认", 330, 240, 120, 32);
        saveProjectButton.Click += async (_, _) => await SaveWorkspaceAsync();
        rightPanel.Controls.Add(saveProjectButton);

        clearProjectButton = NewButton("清除默认", 464, 240, 128, 32);
        clearProjectButton.Click += async (_, _) => await ClearWorkspaceAsync();
        rightPanel.Controls.Add(clearProjectButton);

        rightPanel.Controls.Add(NewSeparator(20, 292, 572));

        startButton = NewButton("启动 Codex", 20, 316, 170, 42, primary: true);
        startButton.Font = UiFont(10.5f, FontStyle.Bold);
        startButton.Click += async (_, _) => await StartCodexAsync();
        rightPanel.Controls.Add(startButton);

        cliCheckButton = NewButton("CLI 检查", 206, 320, 112, 36);
        cliCheckButton.Click += async (_, _) => await RunCliCheckAsync();
        rightPanel.Controls.Add(cliCheckButton);

        httpTestButton = NewButton("HTTP 检查", 332, 320, 112, 36);
        httpTestButton.Click += async (_, _) => await RunHttpTestAsync();
        rightPanel.Controls.Add(httpTestButton);

        statusText = new TextBox
        {
            Location = new Point(20, 382),
            Size = new Size(572, 112),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = UiFont(),
            BackColor = softColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        rightPanel.Controls.Add(statusText);

        UpdateButtons();
    }

    private Panel NewPanel(int x, int y, int width, int height)
    {
        return new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private Label NewLabel(string text, int x, int y, int width, int height, float size, FontStyle style, Color? color = null)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AutoEllipsis = true,
            Font = UiFont(size, style),
            ForeColor = color ?? textColor
        };
    }

    private Button NewButton(string text, int x, int y, int width, int height, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = UiFont(9, primary ? FontStyle.Bold : FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? primaryColor : Color.FromArgb(250, 251, 250),
            ForeColor = primary ? Color.White : textColor
        };
        button.FlatAppearance.BorderColor = primary ? primaryDarkColor : borderColor;
        button.FlatAppearance.MouseOverBackColor = primary ? primaryDarkColor : Color.FromArgb(228, 242, 237);
        return button;
    }

    private static Label NewSeparator(int x, int y, int width)
    {
        return new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(x, y),
            Size = new Size(width, 2)
        };
    }

    private async Task RefreshProfilesAsync(string? keepId = null)
    {
        await RunUiActionAsync("正在刷新 API 配置...", () =>
        {
            var profiles = bridge.GetProfiles();
            BeginInvoke((Action)(() =>
            {
                profileList.Items.Clear();
                profileMap.Clear();

                foreach (var profile in profiles.OrderBy(p => p.Name).ThenBy(p => p.Id))
                {
                    var display = FormatProfileListItem(profile);
                    profileMap[display] = profile;
                    profileList.Items.Add(display);
                }

                if (profileList.Items.Count > 0)
                {
                    var selectedIndex = 0;
                    if (!string.IsNullOrWhiteSpace(keepId))
                    {
                        for (var i = 0; i < profileList.Items.Count; i++)
                        {
                            var item = profileList.Items[i]?.ToString() ?? "";
                            if (item.EndsWith($" | {keepId}", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                    }
                    profileList.SelectedIndex = selectedIndex;
                    SetStatus($"已加载 {profiles.Count} 个 API 配置。选择项目文件夹后即可启动。");
                }
                else
                {
                    SetStatus("还没有找到任何 API 配置。请先用 New-CodexApiProfile 创建。");
                }

                UpdateSelectedProfile();
            }));
        });
    }

    private void UpdateSelectedProfile()
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            providerNameLabel.Text = "尚未选择配置";
            providerMetaLabel.Text = "先用 New-CodexApiProfile 创建配置，然后点击刷新。";
            savedProjectLabel.Text = "已保存默认项目: 无";
            workspaceBox.Text = "";
            UpdateButtons();
            return;
        }

        providerNameLabel.Text = profile.Name;
        providerMetaLabel.Text = $"配置 ID: {profile.Id}\r\n模型: {profile.Model}\r\nBase URL: {profile.BaseUrl}";

        if (!string.IsNullOrWhiteSpace(profile.Workspace))
        {
            savedProjectLabel.Text = $"已保存默认项目: {profile.Workspace}";
            if (string.IsNullOrWhiteSpace(workspaceBox.Text))
            {
                workspaceBox.Text = profile.Workspace;
            }
        }
        else
        {
            savedProjectLabel.Text = "已保存默认项目: 无";
            workspaceBox.Text = "";
        }

        UpdateButtons();
    }

    private ProfileInfo? SelectedProfile()
    {
        var key = profileList.SelectedItem?.ToString();
        return key is not null && profileMap.TryGetValue(key, out var profile) ? profile : null;
    }

    private static string FormatProfileListItem(ProfileInfo profile)
    {
        return $"{profile.Name} | {profile.Model} | {profile.Id}";
    }

    private bool WorkspaceReady()
    {
        var path = workspaceBox.Text.Trim();
        return path.Length > 0 && Directory.Exists(path);
    }

    private void UpdateButtons()
    {
        var hasProfile = SelectedProfile() is not null;
        var hasWorkspace = WorkspaceReady();
        startButton.Enabled = !isBusy && hasProfile && hasWorkspace;
        cliCheckButton.Enabled = !isBusy && hasProfile && hasWorkspace;
        httpTestButton.Enabled = !isBusy && hasProfile;
        homeButton.Enabled = !isBusy && hasProfile;
        saveProjectButton.Enabled = !isBusy && hasProfile && hasWorkspace;
        clearProjectButton.Enabled = !isBusy && hasProfile;
    }

    private void BrowseWorkspace()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要用 Codex 打开的项目文件夹",
            ShowNewFolderButton = true
        };

        if (WorkspaceReady())
        {
            dialog.SelectedPath = workspaceBox.Text.Trim();
        }
        else
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                dialog.SelectedPath = documents;
            }
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            workspaceBox.Text = dialog.SelectedPath;
            SetStatus("已选择项目文件夹。");
        }
    }

    private async Task SaveWorkspaceAsync()
    {
        var profile = RequireProfile();
        var workspace = RequireWorkspace();
        await RunUiActionAsync("正在保存默认项目文件夹...", () => bridge.SaveWorkspace(profile.Id, workspace));
        await RefreshProfilesAsync(profile.Id);
    }

    private async Task ClearWorkspaceAsync()
    {
        var profile = RequireProfile();
        await RunUiActionAsync("正在清除默认项目文件夹...", () => bridge.ClearWorkspace(profile.Id));
        workspaceBox.Text = "";
        await RefreshProfilesAsync(profile.Id);
    }

    private async Task StartCodexAsync()
    {
        var profile = RequireProfile();
        var workspace = RequireWorkspace();

        await RunUiActionAsync("正在启动 Codex 终端...", () =>
        {
            if (rememberCheck.Checked)
            {
                bridge.SaveWorkspace(profile.Id, workspace);
            }
            var result = bridge.StartProfile(profile.Id, workspace);
            BeginInvoke((Action)(() => SetStatus($"已在新的终端窗口启动 {profile.Id}。\r\n项目: {workspace}\r\nCODEX_HOME: {result.CodexHome}")));
        });
    }

    private async Task RunCliCheckAsync()
    {
        var profile = RequireProfile();
        var workspace = RequireWorkspace();
        await RunUiActionAsync("正在运行真实 Codex CLI 检查...", () =>
        {
            var result = bridge.RunCliCheck(profile.Id, workspace);
            BeginInvoke((Action)(() => SetStatus(result)));
        }, timeoutMilliseconds: 240_000);
    }

    private async Task RunHttpTestAsync()
    {
        var profile = RequireProfile();
        await RunUiActionAsync("正在运行 HTTP 连通性检查...", () =>
        {
            var result = bridge.TestProfile(profile.Id);
            BeginInvoke((Action)(() => SetStatus(FormatHttpResult(result))));
        }, timeoutMilliseconds: 60_000);
    }

    private ProfileInfo RequireProfile()
    {
        return SelectedProfile() ?? throw new InvalidOperationException("请先选择一个 API 配置。");
    }

    private string RequireWorkspace()
    {
        var workspace = workspaceBox.Text.Trim();
        if (workspace.Length == 0 || !Directory.Exists(workspace))
        {
            throw new InvalidOperationException("请选择一个已经存在的项目文件夹。");
        }
        return workspace;
    }

    private async Task RunUiActionAsync(string busyText, Action action, int timeoutMilliseconds = 120_000)
    {
        try
        {
            isBusy = true;
            UpdateButtons();
            SetStatus(busyText);
            await Task.Run(action).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            isBusy = false;
            UpdateButtons();
        }
    }

    private string FormatHttpResult(ProfileTestResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "HTTP 连通性检查",
            $"状态: {result.Status}",
            $"是否通过: {result.Ok}",
            $"/models HTTP: {result.ModelsHttpStatus?.ToString() ?? ""}",
            $"/responses HTTP: {result.ResponsesHttpStatus?.ToString() ?? ""}",
            $"模型数量: {result.ModelCount?.ToString() ?? ""}",
            $"详情: {result.Details ?? ""}"
        });
    }

    private void SetStatus(string text)
    {
        statusText.Text = text;
    }

    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("路径为空。");
            return;
        }

        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}

internal sealed class PowerShellBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string rootDir;
    private readonly string modulePath;
    private readonly string shellPath;

    public PowerShellBridge(string rootDir)
    {
        this.rootDir = rootDir;
        modulePath = Path.Combine(rootDir, "CodexApiLauncher.psm1");
        if (!File.Exists(modulePath))
        {
            throw new FileNotFoundException("没有找到 CodexApiLauncher.psm1。请确认 exe 和脚本文件在同一个目录。", modulePath);
        }

        shellPath = ResolvePowerShell() ?? throw new FileNotFoundException("没有找到 pwsh.exe 或 powershell.exe。");
    }

    public List<ProfileInfo> GetProfiles()
    {
        var output = RunModule("$profiles = @(Get-CodexApiProfiles); ConvertTo-Json -InputObject $profiles -Depth 8 -Compress");
        var json = output.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ProfileInfo>();
        }

        return JsonSerializer.Deserialize<List<ProfileInfo>>(json, JsonOptions) ?? new List<ProfileInfo>();
    }

    public void SaveWorkspace(string id, string workspace)
    {
        RunModule($"Set-CodexApiProfileWorkspace -Id {Quote(id)} -Workspace {Quote(workspace)} | Out-Null");
    }

    public void ClearWorkspace(string id)
    {
        RunModule($"Set-CodexApiProfileWorkspace -Id {Quote(id)} -Clear | Out-Null");
    }

    public StartProfileResult StartProfile(string id, string workspace)
    {
        var command = "$result = Start-CodexApiProfile -Id " + Quote(id) + " -Workspace " + Quote(workspace) + "; " +
            "ConvertTo-Json -InputObject $result -Depth 8 -Compress";
        var output = RunModule(command);
        return JsonSerializer.Deserialize<StartProfileResult>(output.StandardOutput.Trim(), JsonOptions) ?? new StartProfileResult();
    }

    public ProfileTestResult TestProfile(string id)
    {
        var output = RunModule($"$result = Test-CodexApiProfile -Id {Quote(id)}; ConvertTo-Json -InputObject $result -Depth 8 -Compress");
        return JsonSerializer.Deserialize<ProfileTestResult>(output.StandardOutput.Trim(), JsonOptions) ?? new ProfileTestResult();
    }

    public string RunCliCheck(string id, string workspace)
    {
        var command = "Start-CodexApiProfile -Id " + Quote(id) + " -Workspace " + Quote(workspace) +
            " -InCurrentWindow -CodexArgs @('exec','--skip-git-repo-check','Reply exactly CLI_OK'); exit $global:LASTEXITCODE";
        var output = RunModule(command, throwOnNonZero: false, timeoutMilliseconds: 240_000);
        return string.Join(Environment.NewLine, new[]
        {
            $"CLI 检查退出码: {output.ExitCode}",
            "标准输出:",
            TrimForDisplay(output.StandardOutput, 1600),
            "",
            "标准错误:",
            TrimForDisplay(output.StandardError, 1000)
        });
    }

    public string GetLaunchersDir()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexApiLauncher");
        }
        return Path.Combine(home, "launchers");
    }

    private PowerShellOutput RunModule(string command, bool throwOnNonZero = true, int timeoutMilliseconds = 120_000)
    {
        var prefix = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); " +
            "$OutputEncoding = [Console]::OutputEncoding; " +
            "$ErrorActionPreference = 'Stop'; " +
            "Import-Module " + Quote(modulePath) + " -Force; ";
        return RunPowerShell(prefix + command, throwOnNonZero, timeoutMilliseconds);
    }

    private PowerShellOutput RunPowerShell(string command, bool throwOnNonZero, int timeoutMilliseconds)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            WorkingDirectory = rootDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup before reporting the timeout.
            }
            throw new TimeoutException("PowerShell 操作超时。");
        }
        process.WaitForExit();

        var output = new PowerShellOutput(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        if (throwOnNonZero && output.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(output.StandardError) ? output.StandardOutput : output.StandardError;
            throw new InvalidOperationException(detail);
        }

        return output;
    }

    private static string? ResolvePowerShell()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_PWSH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        const string preferred = @"D:\develop\PowerShell\7\pwsh.exe";
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return FindOnPath("pwsh.exe") ?? FindOnPath("powershell.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var candidate = Path.Combine(path.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Quote(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string TrimForDisplay(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

internal sealed record PowerShellOutput(int ExitCode, string StandardOutput, string StandardError);

internal sealed class ProfileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string EnvKeyName { get; set; } = "";
    public string? Workspace { get; set; }
    public string? CodexHome { get; set; }
}

internal sealed class StartProfileResult
{
    public string Id { get; set; } = "";
    public bool Started { get; set; }
    public string Shell { get; set; } = "";
    public string CodexHome { get; set; } = "";
    public string LauncherPath { get; set; } = "";
}

internal sealed class ProfileTestResult
{
    public bool Ok { get; set; }
    public string Status { get; set; } = "";
    public int? ModelsHttpStatus { get; set; }
    public int? ResponsesHttpStatus { get; set; }
    public int? ModelCount { get; set; }
    public string? Details { get; set; }
}
