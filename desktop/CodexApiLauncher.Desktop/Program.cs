using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private readonly Color windowColor = Color.FromArgb(246, 248, 251);
    private readonly Color surfaceColor = Color.FromArgb(246, 248, 251);
    private readonly Color fieldColor = Color.FromArgb(255, 255, 255);
    private readonly Color borderColor = Color.FromArgb(211, 219, 229);
    private readonly Color textColor = Color.FromArgb(26, 32, 44);
    private readonly Color mutedColor = Color.FromArgb(93, 105, 123);
    private readonly Color primaryColor = Color.FromArgb(229, 244, 241);
    private readonly Color primaryDarkColor = Color.FromArgb(21, 104, 91);
    private readonly Color softColor = Color.FromArgb(255, 255, 255);
    private readonly Color outputBackColor = Color.FromArgb(250, 252, 255);
    private readonly Color outputTextColor = Color.FromArgb(37, 47, 63);

    private ListView profileList = null!;
    private TextBox providerNameBox = null!;
    private TextBox providerIdBox = null!;
    private TextBox providerModelBox = null!;
    private TextBox providerBaseUrlBox = null!;
    private TextBox providerApiKeyBox = null!;
    private TextBox providerCodexHomeBox = null!;
    private Label savedProjectLabel = null!;
    private TextBox workspaceBox = null!;
    private CheckBox rememberCheck = null!;
    private Button startButton = null!;
    private Button addProfileButton = null!;
    private Button saveProfileButton = null!;
    private Button migrateHomeButton = null!;
    private Button cliCheckButton = null!;
    private Button httpTestButton = null!;
    private Button saveProjectButton = null!;
    private Button clearProjectButton = null!;
    private Button homeButton = null!;
    private Button copyOutputButton = null!;
    private Button clearOutputButton = null!;
    private Label outputTitleLabel = null!;
    private Label outputMetaLabel = null!;
    private RichTextBox statusText = null!;

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

    private Font MonoFont(float size = 9.0f)
    {
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fontName = families.Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        return new Font(fontName, size, FontStyle.Regular);
    }

    private void BuildUi()
    {
        Text = "CodexCLI API 多开启动器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 860);
        MinimumSize = new Size(1040, 760);
        BackColor = windowColor;
        Font = UiFont();

        var headerTitle = NewLabel("CodexCLI API 多开启动器", 24, 18, 520, 34, 17, FontStyle.Bold);
        Controls.Add(headerTitle);

        var headerText = NewLabel("选择供应商和项目文件夹，然后用独立 CODEX_HOME 启动 Codex CLI。", 24, 56, 760, 24, 9, FontStyle.Regular, mutedColor);
        Controls.Add(headerText);

        var leftPanel = NewPanel(24, 96, 300, 730);
        Controls.Add(leftPanel);

        var rightPanel = NewPanel(348, 96, 744, 730);
        Controls.Add(rightPanel);

        leftPanel.Controls.Add(NewLabel("供应商配置", 16, 16, 240, 26, 11, FontStyle.Bold));

        var refreshButton = NewButton("刷新", 16, 52, 86, 32);
        refreshButton.Click += async (_, _) => await RefreshProfilesAsync();
        leftPanel.Controls.Add(refreshButton);

        addProfileButton = NewButton("新增供应商", 116, 52, 136, 32, primary: true);
        addProfileButton.Click += async (_, _) => await AddProfileAsync();
        leftPanel.Controls.Add(addProfileButton);

        profileList = new ListView
        {
            Location = new Point(16, 100),
            Size = new Size(268, 448),
            Font = UiFont(9.0f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = softColor,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            MultiSelect = false
        };
        profileList.SelectedIndexChanged += (_, _) => UpdateSelectedProfile();
        profileList.Resize += (_, _) => ResizeProfileColumns();
        profileList.Columns.Add("名称");
        profileList.Columns.Add("模型");
        profileList.Columns.Add("供应商");
        ResizeProfileColumns();
        leftPanel.Controls.Add(profileList);

        leftPanel.Controls.Add(NewLabel("每个配置独立保存 API 凭据、CODEX_HOME、会话和日志。", 16, 568, 268, 48, 9, FontStyle.Regular, mutedColor));

        var openLaunchersButton = NewButton("打开快捷启动脚本目录", 16, 652, 268, 34);
        openLaunchersButton.Click += (_, _) => OpenFolder(bridge.GetLaunchersDir());
        leftPanel.Controls.Add(openLaunchersButton);

        rightPanel.Controls.Add(NewLabel("当前供应商", 16, 16, 170, 28, 12, FontStyle.Bold));

        saveProfileButton = NewButton("保存修改", 504, 14, 96, 32, primary: true);
        saveProfileButton.Click += async (_, _) => await SaveProfileChangesAsync();
        rightPanel.Controls.Add(saveProfileButton);

        homeButton = NewButton("打开目录", 612, 14, 96, 32);
        homeButton.Click += (_, _) =>
        {
            var profile = SelectedProfile();
            if (profile is not null)
            {
                OpenFolder(profile.CodexHome);
            }
        };
        rightPanel.Controls.Add(homeButton);

        providerNameBox = AddEditableField(rightPanel, "显示名称", 16, 58, 592);
        providerIdBox = AddEditableField(rightPanel, "供应商 ID", 16, 92, 592);
        providerModelBox = AddEditableField(rightPanel, "模型", 16, 126, 592);
        providerBaseUrlBox = AddEditableField(rightPanel, "中转地址", 16, 160, 592);
        providerApiKeyBox = AddEditableField(rightPanel, "API Key", 16, 194, 592);
        providerApiKeyBox.UseSystemPasswordChar = true;
        providerApiKeyBox.PlaceholderText = "留空则保留现有 API Key";
        providerCodexHomeBox = AddCodexHomeField(rightPanel, "配置目录", 16, 228);

        rightPanel.Controls.Add(NewSeparator(16, 274, 692));
        rightPanel.Controls.Add(NewLabel("项目文件夹", 16, 294, 180, 26, 11, FontStyle.Bold));

        savedProjectLabel = NewLabel("已保存默认项目：无", 16, 320, 692, 24, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(savedProjectLabel);

        workspaceBox = new TextBox
        {
            Location = new Point(16, 348),
            Size = new Size(568, 28),
            Font = UiFont(9.5f)
        };
        workspaceBox.TextChanged += (_, _) => UpdateButtons();
        rightPanel.Controls.Add(workspaceBox);

        var browseButton = NewButton("选择文件夹", 596, 344, 112, 32, primary: true);
        browseButton.Click += (_, _) => BrowseWorkspace();
        rightPanel.Controls.Add(browseButton);

        rememberCheck = new CheckBox
        {
            Text = "启动时记住项目",
            Location = new Point(16, 388),
            Size = new Size(190, 24),
            Font = UiFont(),
            BackColor = surfaceColor
        };
        rightPanel.Controls.Add(rememberCheck);

        saveProjectButton = NewButton("保存默认", 486, 384, 102, 32);
        saveProjectButton.Click += async (_, _) => await SaveWorkspaceAsync();
        rightPanel.Controls.Add(saveProjectButton);

        clearProjectButton = NewButton("清除默认", 600, 384, 108, 32);
        clearProjectButton.Click += async (_, _) => await ClearWorkspaceAsync();
        rightPanel.Controls.Add(clearProjectButton);

        rightPanel.Controls.Add(NewSeparator(16, 434, 692));

        startButton = NewButton("启动 Codex", 16, 458, 156, 40, primary: true);
        startButton.Font = UiFont(10.5f, FontStyle.Bold);
        startButton.Click += async (_, _) => await StartCodexAsync();
        rightPanel.Controls.Add(startButton);

        cliCheckButton = NewButton("CLI 检查", 190, 462, 104, 34);
        cliCheckButton.Click += async (_, _) => await RunCliCheckAsync();
        rightPanel.Controls.Add(cliCheckButton);

        httpTestButton = NewButton("HTTP 检查", 312, 462, 108, 34);
        httpTestButton.Click += async (_, _) => await RunHttpTestAsync();
        rightPanel.Controls.Add(httpTestButton);

        outputTitleLabel = NewLabel("运行输出", 16, 520, 140, 26, 11, FontStyle.Bold);
        rightPanel.Controls.Add(outputTitleLabel);

        outputMetaLabel = NewLabel("空闲", 108, 522, 300, 24, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(outputMetaLabel);

        copyOutputButton = NewButton("复制", 506, 516, 88, 32);
        copyOutputButton.Click += (_, _) => CopyOutput();
        copyOutputButton.Enabled = false;
        rightPanel.Controls.Add(copyOutputButton);

        clearOutputButton = NewButton("清空", 608, 516, 100, 32);
        clearOutputButton.Click += (_, _) => ClearOutput();
        rightPanel.Controls.Add(clearOutputButton);

        statusText = new RichTextBox
        {
            Location = new Point(16, 556),
            Size = new Size(692, 152),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = MonoFont(9.5f),
            BackColor = outputBackColor,
            ForeColor = outputTextColor,
            BorderStyle = BorderStyle.FixedSingle,
            DetectUrls = false,
            WordWrap = false
        };
        rightPanel.Controls.Add(statusText);

        UpdateButtons();
    }

    private async Task AddProfileAsync()
    {
        using var dialog = new AddProfileForm(UiFont, windowColor, surfaceColor, borderColor, textColor, mutedColor, primaryColor, primaryDarkColor);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile is null)
        {
            return;
        }

        var draft = dialog.Profile;
        var created = await RunUiActionAsync("正在创建供应商配置...", () => bridge.CreateProfile(draft));
        if (created)
        {
            await RefreshProfilesAsync(draft.Id);
        }
    }

    private async Task SaveProfileChangesAsync()
    {
        ProfileDraft draft;
        try
        {
            var profile = RequireProfile();
            draft = new ProfileDraft
            {
                OriginalId = profile.Id,
                Id = providerIdBox.Text.Trim(),
                Name = providerNameBox.Text.Trim(),
                BaseUrl = providerBaseUrlBox.Text.Trim(),
                Model = providerModelBox.Text.Trim(),
                ApiKey = providerApiKeyBox.Text,
                CodexHome = providerCodexHomeBox.Text.Trim(),
                Workspace = profile.Workspace ?? ""
            };

            ValidateProfileDraftForSave(draft);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ProfileInfo? savedProfile = null;
        var updated = await RunUiActionAsync("正在保存供应商配置...", () => savedProfile = bridge.UpdateProfile(draft));
        if (updated)
        {
            providerApiKeyBox.Text = "";
            await RefreshProfilesAsync(savedProfile?.Id ?? draft.Id);
        }
    }

    private async Task MigrateCodexHomeAsync()
    {
        var profile = RequireProfile();
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择新的配置目录（会迁移当前 CODEX_HOME 内容）",
            ShowNewFolderButton = true
        };

        var currentPath = providerCodexHomeBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }
        else if (!string.IsNullOrWhiteSpace(profile.CodexHome) && Directory.Exists(profile.CodexHome))
        {
            dialog.SelectedPath = profile.CodexHome;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        providerCodexHomeBox.Text = dialog.SelectedPath;
        SetStatus("已选择新的配置目录，正在迁移 CODEX_HOME 内容...");
        await SaveProfileChangesAsync();
    }

    private static void ValidateProfileDraftForSave(ProfileDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) ||
            string.IsNullOrWhiteSpace(draft.Id) ||
            string.IsNullOrWhiteSpace(draft.BaseUrl) ||
            string.IsNullOrWhiteSpace(draft.Model) ||
            string.IsNullOrWhiteSpace(draft.CodexHome))
        {
            throw new InvalidOperationException("请填写显示名称、供应商 ID、中转地址、模型和配置目录。");
        }

        if (!Uri.TryCreate(draft.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("中转地址必须是完整的 http 或 https URL。");
        }
    }

    private Panel NewPanel(int x, int y, int width, int height)
    {
        return new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.None
        };
    }

    private TextBox AddEditableField(Control parent, string label, int x, int y, int valueWidth)
    {
        parent.Controls.Add(NewLabel(label, x, y + 4, 86, 24, 9, FontStyle.Bold, mutedColor));
        var box = new TextBox
        {
            Location = new Point(x + 100, y),
            Size = new Size(valueWidth, 28),
            Font = UiFont(9.5f),
            ReadOnly = false,
            BackColor = fieldColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        parent.Controls.Add(box);
        return box;
    }

    private TextBox AddCodexHomeField(Control parent, string label, int x, int y)
    {
        parent.Controls.Add(NewLabel(label, x, y + 4, 86, 24, 9, FontStyle.Bold, mutedColor));
        var box = new TextBox
        {
            Location = new Point(x + 100, y),
            Size = new Size(468, 28),
            Font = UiFont(9.5f),
            ReadOnly = false,
            BackColor = fieldColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        parent.Controls.Add(box);

        migrateHomeButton = NewButton("迁移目录", x + 580, y - 2, 112, 32);
        migrateHomeButton.Click += async (_, _) => await MigrateCodexHomeAsync();
        parent.Controls.Add(migrateHomeButton);
        return box;
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
            ForeColor = color ?? textColor,
            BackColor = Color.Transparent
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
            BackColor = primary ? primaryColor : surfaceColor,
            ForeColor = primary ? primaryDarkColor : textColor
        };
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(157, 191, 180) : borderColor;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(205, 231, 225) : Color.FromArgb(237, 241, 246);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(190, 221, 214) : Color.FromArgb(225, 232, 240);
        return button;
    }

    private void ResizeProfileColumns()
    {
        if (profileList.Columns.Count < 3)
        {
            return;
        }

        var width = Math.Max(200, profileList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
        profileList.Columns[0].Width = (int)(width * 0.36);
        profileList.Columns[1].Width = (int)(width * 0.30);
        profileList.Columns[2].Width = width - profileList.Columns[0].Width - profileList.Columns[1].Width;
    }

    private static Label NewSeparator(int x, int y, int width)
    {
        return new Label
        {
            BackColor = Color.FromArgb(221, 227, 224),
            Location = new Point(x, y),
            Size = new Size(width, 1)
        };
    }

    private async Task RefreshProfilesAsync(string? keepId = null)
    {
        await RunUiActionAsync("正在刷新供应商配置...", () =>
        {
            var profiles = bridge.GetProfiles();
            BeginInvoke((Action)(() =>
            {
                profileList.BeginUpdate();
                profileList.Items.Clear();

                var selectedIndex = 0;
                var index = 0;
                foreach (var profile in profiles.OrderBy(p => p.Name).ThenBy(p => p.Id))
                {
                    var item = new ListViewItem(profile.Name);
                    item.SubItems.Add(profile.Model);
                    item.SubItems.Add(profile.Id);
                    item.Tag = profile;
                    profileList.Items.Add(item);

                    if (!string.IsNullOrWhiteSpace(keepId) && string.Equals(profile.Id, keepId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = index;
                    }

                    index++;
                }
                ResizeProfileColumns();
                profileList.EndUpdate();

                if (profileList.Items.Count > 0)
                {
                    var itemToSelect = profileList.Items[Math.Min(selectedIndex, profileList.Items.Count - 1)];
                    itemToSelect.Selected = true;
                    itemToSelect.EnsureVisible();
                    profileList.Select();
                    profileList.Refresh();
                    SetStatus($"已加载 {profiles.Count} 个供应商配置。选择项目文件夹后即可启动。");
                }
                else
                {
                    SetStatus("还没有找到任何供应商配置。请先新增供应商。");
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
            providerNameBox.Text = "";
            providerIdBox.Text = "";
            providerModelBox.Text = "";
            providerBaseUrlBox.Text = "";
            providerApiKeyBox.Text = "";
            providerCodexHomeBox.Text = "";
            savedProjectLabel.Text = "已保存默认项目：无";
            workspaceBox.Text = "";
            UpdateButtons();
            return;
        }

        providerNameBox.Text = profile.Name;
        providerIdBox.Text = profile.Id;
        providerModelBox.Text = profile.Model;
        providerBaseUrlBox.Text = profile.BaseUrl;
        providerApiKeyBox.Text = "";
        providerCodexHomeBox.Text = profile.CodexHome ?? "";

        if (!string.IsNullOrWhiteSpace(profile.Workspace))
        {
            savedProjectLabel.Text = $"已保存默认项目：{profile.Workspace}";
            if (string.IsNullOrWhiteSpace(workspaceBox.Text))
            {
                workspaceBox.Text = profile.Workspace;
            }
        }
        else
        {
            savedProjectLabel.Text = "已保存默认项目：无";
            workspaceBox.Text = "";
        }

        UpdateButtons();
    }

    private ProfileInfo? SelectedProfile()
    {
        return profileList.SelectedItems.Count == 0 ? null : profileList.SelectedItems[0].Tag as ProfileInfo;
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
        addProfileButton.Enabled = !isBusy;
        saveProfileButton.Enabled = !isBusy && hasProfile;
        migrateHomeButton.Enabled = !isBusy && hasProfile;
        cliCheckButton.Enabled = !isBusy && hasProfile;
        httpTestButton.Enabled = !isBusy && hasProfile;
        homeButton.Enabled = !isBusy && hasProfile;
        saveProjectButton.Enabled = !isBusy && hasProfile && hasWorkspace;
        clearProjectButton.Enabled = !isBusy && hasProfile;

        providerNameBox.Enabled = !isBusy && hasProfile;
        providerIdBox.Enabled = !isBusy && hasProfile;
        providerModelBox.Enabled = !isBusy && hasProfile;
        providerBaseUrlBox.Enabled = !isBusy && hasProfile;
        providerApiKeyBox.Enabled = !isBusy && hasProfile;
        providerCodexHomeBox.Enabled = !isBusy && hasProfile;
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
        var (workspace, usedFallback) = ResolveCliCheckWorkspace();
        await RunUiActionAsync("正在运行真实 Codex CLI 检查...", () =>
        {
            var result = bridge.RunCliCheck(profile.Id, workspace);
            var message = usedFallback
                ? $"未选择项目文件夹，已使用检查目录：{workspace}{Environment.NewLine}{Environment.NewLine}{result}"
                : result;
            BeginInvoke((Action)(() => SetStatus(message)));
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
        return SelectedProfile() ?? throw new InvalidOperationException("请先选择一个供应商配置。");
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

    private (string Workspace, bool UsedFallback) ResolveCliCheckWorkspace()
    {
        var workspace = workspaceBox.Text.Trim();
        if (workspace.Length > 0 && Directory.Exists(workspace))
        {
            return (workspace, false);
        }

        var fallback = Path.Combine(GetDefaultLauncherHome(), "cli-check-workspace");
        Directory.CreateDirectory(fallback);
        return (fallback, true);
    }

    private static string GetDefaultLauncherHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexApiLauncher");
    }

    private async Task<bool> RunUiActionAsync(string busyText, Action action, int timeoutMilliseconds = 120_000)
    {
        try
        {
            isBusy = true;
            UpdateButtons();
            SetStatus(busyText);
            await Task.Run(action).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
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
            $"状态: {TranslateProviderStatus(result.Status)}",
            $"是否通过: {result.Ok}",
            $"/models HTTP: {result.ModelsHttpStatus?.ToString() ?? ""}",
            $"/responses HTTP: {result.ResponsesHttpStatus?.ToString() ?? ""}",
            $"模型数量: {result.ModelCount?.ToString() ?? ""}",
            $"详情: {result.Details ?? ""}"
        });
    }

    private static string TranslateProviderStatus(string status)
    {
        return status switch
        {
            "passed" => "通过",
            "auth_failed" => "认证失败",
            "responses_forbidden" => "Responses 路由被拒绝",
            "responses_unsupported" => "不支持 Responses API",
            "provider_unavailable" => "Provider 暂时不可用",
            "unreachable" => "无法连接",
            "responses_unreachable" => "Responses 无法连接",
            "responses_failed" => "Responses 请求失败",
            _ => status
        };
    }

    private void SetStatus(string text)
    {
        outputMetaLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        statusText.Clear();
        statusText.SelectionColor = outputTextColor;
        statusText.AppendText(text);
        statusText.SelectionStart = 0;
        statusText.ScrollToCaret();
        copyOutputButton.Enabled = !string.IsNullOrWhiteSpace(text);
    }

    private void CopyOutput()
    {
        if (string.IsNullOrWhiteSpace(statusText.Text))
        {
            outputMetaLabel.Text = "没有可复制的输出";
            return;
        }

        Clipboard.SetText(statusText.Text);
        outputMetaLabel.Text = $"已复制 {DateTime.Now:HH:mm:ss}";
    }

    private void ClearOutput()
    {
        statusText.Clear();
        outputMetaLabel.Text = "空闲";
        copyOutputButton.Enabled = false;
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

internal sealed class AddProfileForm : Form
{
    private readonly Func<float, FontStyle, Font> uiFont;
    private readonly Color surfaceColor;
    private readonly Color borderColor;
    private readonly Color textColor;
    private readonly Color mutedColor;
    private readonly Color primaryColor;
    private readonly Color primaryDarkColor;

    private TextBox nameBox = null!;
    private TextBox idBox = null!;
    private TextBox baseUrlBox = null!;
    private TextBox modelBox = null!;
    private TextBox apiKeyBox = null!;
    private TextBox configDirBox = null!;
    private TextBox projectDirBox = null!;
    private CheckBox sameDirCheck = null!;
    private Label validationLabel = null!;

    private bool idTouched;
    private bool configDirTouched;
    private bool suppressIdTouched;
    private bool suppressConfigDirTouched;

    public ProfileDraft? Profile { get; private set; }

    public AddProfileForm(
        Func<float, FontStyle, Font> uiFont,
        Color windowColor,
        Color surfaceColor,
        Color borderColor,
        Color textColor,
        Color mutedColor,
        Color primaryColor,
        Color primaryDarkColor)
    {
        this.uiFont = uiFont;
        this.surfaceColor = surfaceColor;
        this.borderColor = borderColor;
        this.textColor = textColor;
        this.mutedColor = mutedColor;
        this.primaryColor = primaryColor;
        this.primaryDarkColor = primaryDarkColor;

        Text = "新增供应商";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 760);
        MinimumSize = new Size(980, 720);
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = windowColor;
        Font = uiFont(9, FontStyle.Regular);
        BuildUi();
    }

    private void BuildUi()
    {
        Controls.Add(NewLabel("新增供应商", 28, 20, 240, 30, 14, FontStyle.Bold));
        Controls.Add(NewLabel("供应商 ID 可手动指定，也可以从网站标题生成；项目目录可以留空。", 28, 54, 700, 24, 9, FontStyle.Regular, mutedColor));

        var panel = new Panel
        {
            Location = new Point(28, 92),
            Size = new Size(736, 464),
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.None
        };
        Controls.Add(panel);

        panel.Controls.Add(NewLabel("API 信息", 20, 18, 160, 24, 10, FontStyle.Bold));
        nameBox = AddTextRow(panel, "显示名称", 54, "例如：0xPsyche 福利中转");
        idBox = AddSupplierIdRow(panel, 98);
        baseUrlBox = AddTextRow(panel, "中转地址", 142, "例如：https://example.com/v1");
        modelBox = AddTextRow(panel, "模型", 186, "例如：gpt-5.6-sol");
        apiKeyBox = AddTextRow(panel, "API Key", 230, "不会写入 TOML 或脚本");
        apiKeyBox.UseSystemPasswordChar = true;

        panel.Controls.Add(NewLabel("目录", 20, 292, 160, 24, 10, FontStyle.Bold));
        configDirBox = AddPathRow(panel, "配置存放目录", 326, "选择 CODEX_HOME 存放位置", BrowseConfigDir);
        projectDirBox = AddPathRow(panel, "项目目录", 370, "选择 Codex 打开的项目目录", BrowseProjectDir);

        sameDirCheck = new CheckBox
        {
            Text = "项目目录同时作为配置存放目录",
            Location = new Point(166, 416),
            Size = new Size(300, 24),
            BackColor = surfaceColor,
            Font = uiFont(9, FontStyle.Regular)
        };
        sameDirCheck.CheckedChanged += (_, _) =>
        {
            if (sameDirCheck.Checked)
            {
                SetConfigDirText(projectDirBox.Text);
                configDirBox.Enabled = false;
            }
            else
            {
                configDirBox.Enabled = true;
                UpdateDefaultConfigDirFromId();
            }
        };
        panel.Controls.Add(sameDirCheck);

        nameBox.TextChanged += (_, _) =>
        {
            if (!idTouched)
            {
                SetSupplierIdText(NormalizeProfileId(nameBox.Text), markTouched: false);
                UpdateDefaultConfigDirFromId();
            }
        };
        idBox.TextChanged += (_, _) =>
        {
            if (!suppressIdTouched)
            {
                idTouched = true;
                UpdateDefaultConfigDirFromId();
            }
        };
        configDirBox.TextChanged += (_, _) =>
        {
            if (!suppressConfigDirTouched && !sameDirCheck.Checked)
            {
                configDirTouched = true;
            }
        };
        projectDirBox.TextChanged += (_, _) =>
        {
            if (sameDirCheck.Checked)
            {
                SetConfigDirText(projectDirBox.Text);
            }
        };

        validationLabel = NewLabel("", 28, 574, 540, 26, 9, FontStyle.Regular, Color.FromArgb(126, 83, 24));
        Controls.Add(validationLabel);

        var cancelButton = NewButton("取消", 560, 570, 96, 36);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var createButton = NewButton("创建供应商", 674, 570, 100, 36, primary: true);
        createButton.Click += (_, _) => TryAccept();
        Controls.Add(createButton);
    }

    private TextBox AddTextRow(Control parent, string label, int y, string placeholder)
    {
        parent.Controls.Add(NewLabel(label, 20, y + 4, 132, 24, 9, FontStyle.Bold));
        var box = new TextBox
        {
            Location = new Point(166, y),
            Size = new Size(520, 28),
            Font = uiFont(9.5f, FontStyle.Regular),
            PlaceholderText = placeholder
        };
        parent.Controls.Add(box);
        return box;
    }

    private TextBox AddSupplierIdRow(Control parent, int y)
    {
        parent.Controls.Add(NewLabel("供应商 ID", 20, y + 4, 132, 24, 9, FontStyle.Bold));
        var box = new TextBox
        {
            Location = new Point(166, y),
            Size = new Size(398, 28),
            Font = uiFont(9.5f, FontStyle.Regular),
            PlaceholderText = "手动指定，例如：welfare-0xpsyche"
        };
        parent.Controls.Add(box);

        var button = NewButton("从标题生成", 590, y - 2, 102, 32);
        button.Click += async (_, _) => await FillSupplierIdFromSiteTitleAsync();
        parent.Controls.Add(button);
        return box;
    }

    private TextBox AddPathRow(Control parent, string label, int y, string placeholder, EventHandler browseHandler)
    {
        parent.Controls.Add(NewLabel(label, 20, y + 4, 132, 24, 9, FontStyle.Bold));
        var box = new TextBox
        {
            Location = new Point(166, y),
            Size = new Size(398, 28),
            Font = uiFont(9.5f, FontStyle.Regular),
            PlaceholderText = placeholder
        };
        parent.Controls.Add(box);

        var button = NewButton("选择", 590, y - 2, 102, 32, primary: true);
        button.Click += browseHandler;
        parent.Controls.Add(button);
        return box;
    }

    private async Task FillSupplierIdFromSiteTitleAsync()
    {
        var baseUrl = baseUrlBox.Text.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            validationLabel.Text = "请先填写完整的 http 或 https 中转地址。";
            return;
        }

        var rootBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port);
        try
        {
            UseWaitCursor = true;
            validationLabel.Text = "正在读取网站标题...";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexApiLauncher/0.3");
            var html = await client.GetStringAsync(rootBuilder.Uri);
            var match = Regex.Match(html, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                validationLabel.Text = "没有读取到网页标题，请手动填写供应商 ID。";
                return;
            }

            var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, @"\s+", " ")).Trim();
            var generatedId = NormalizeProfileId(title);
            if (string.IsNullOrWhiteSpace(generatedId))
            {
                validationLabel.Text = "网页标题无法转换成供应商 ID，请手动填写。";
                return;
            }

            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                nameBox.Text = title;
            }
            SetSupplierIdText(generatedId, markTouched: true);
            UpdateDefaultConfigDirFromId();
            validationLabel.Text = $"已根据网页标题生成供应商 ID：{generatedId}";
        }
        catch (Exception ex)
        {
            validationLabel.Text = $"读取网页标题失败：{ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private Label NewLabel(string text, int x, int y, int width, int height, float size, FontStyle style, Color? color = null)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AutoEllipsis = true,
            Font = uiFont(size, style),
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
            Font = uiFont(9, primary ? FontStyle.Bold : FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? primaryColor : Color.FromArgb(250, 251, 250),
            ForeColor = primary ? primaryDarkColor : textColor
        };
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(157, 191, 180) : borderColor;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(199, 225, 216) : Color.FromArgb(238, 242, 240);
        return button;
    }

    private void BrowseConfigDir(object? sender, EventArgs e)
    {
        BrowseInto(configDirBox, "选择配置存放目录（CODEX_HOME）");
    }

    private void BrowseProjectDir(object? sender, EventArgs e)
    {
        BrowseInto(projectDirBox, "选择项目目录");
    }

    private void BrowseInto(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(target.Text) && Directory.Exists(target.Text.Trim()))
        {
            dialog.SelectedPath = target.Text.Trim();
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
            target.Text = dialog.SelectedPath;
        }
    }

    private void SetSupplierIdText(string value, bool markTouched)
    {
        suppressIdTouched = true;
        try
        {
            idBox.Text = value;
        }
        finally
        {
            suppressIdTouched = false;
        }

        if (markTouched)
        {
            idTouched = true;
        }
    }

    private void SetConfigDirText(string value)
    {
        suppressConfigDirTouched = true;
        try
        {
            configDirBox.Text = value;
        }
        finally
        {
            suppressConfigDirTouched = false;
        }
    }

    private void UpdateDefaultConfigDirFromId()
    {
        if (sameDirCheck.Checked || configDirTouched)
        {
            return;
        }

        var id = NormalizeProfileId(idBox.Text);
        if (string.IsNullOrWhiteSpace(id))
        {
            SetConfigDirText("");
            return;
        }

        SetConfigDirText(Path.Combine(GetDefaultLauncherHome(), "profiles", id));
    }

    private static string GetDefaultLauncherHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexApiLauncher");
    }

    private void TryAccept()
    {
        var name = nameBox.Text.Trim();
        var id = idBox.Text.Trim();
        var baseUrl = baseUrlBox.Text.Trim();
        var model = modelBox.Text.Trim();
        var apiKey = apiKeyBox.Text;
        var configDir = configDirBox.Text.Trim();
        var projectDir = projectDirBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(model) ||
            string.IsNullOrWhiteSpace(configDir) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            validationLabel.Text = "请填写名称、供应商 ID、中转地址、模型、API Key 和配置目录。";
            return;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            validationLabel.Text = "中转地址必须是完整的 http 或 https URL。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(projectDir) && !Directory.Exists(projectDir))
        {
            validationLabel.Text = "项目目录必须已经存在。";
            return;
        }

        Profile = new ProfileDraft
        {
            OriginalId = "",
            Id = id,
            Name = name,
            BaseUrl = baseUrl,
            Model = model,
            ApiKey = apiKey,
            CodexHome = configDir,
            Workspace = projectDir
        };
        DialogResult = DialogResult.OK;
    }

    private static string NormalizeProfileId(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                builder.Append(ch);
            }
            else if (ch is '-' or '_' || char.IsWhiteSpace(ch))
            {
                builder.Append('-');
            }
        }

        var id = builder.ToString().Trim('-', '_');
        return string.IsNullOrWhiteSpace(id) ? "" : id;
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

    public void CreateProfile(ProfileDraft draft)
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"codex-api-launcher-key-{Guid.NewGuid():N}.txt");
        File.WriteAllText(keyPath, draft.ApiKey, new UTF8Encoding(false));
        try
        {
            var command = string.Join(" ", new[]
            {
                "$apiKeyPath = " + Quote(keyPath) + ";",
                "$plainApiKey = Get-Content -Raw -LiteralPath $apiKeyPath;",
                "try {",
                "$secureApiKey = ConvertTo-SecureString $plainApiKey -AsPlainText -Force;",
                "New-CodexApiProfile",
                "-Id " + Quote(draft.Id),
                "-Name " + Quote(draft.Name),
                "-BaseUrl " + Quote(draft.BaseUrl),
                "-Model " + Quote(draft.Model),
                "-CodexHome " + Quote(draft.CodexHome),
                "-Workspace " + Quote(draft.Workspace),
                "-ApiKey $secureApiKey",
                "| Out-Null",
                "} finally {",
                "Remove-Item -LiteralPath $apiKeyPath -Force -ErrorAction SilentlyContinue;",
                "$plainApiKey = $null;",
                "$secureApiKey = $null;",
                "}"
            });
            RunModule(command);
        }
        finally
        {
            try
            {
                if (File.Exists(keyPath))
                {
                    File.Delete(keyPath);
                }
            }
            catch
            {
                // Best effort cleanup for the transient key handoff file.
            }
        }
    }

    public ProfileInfo UpdateProfile(ProfileDraft draft)
    {
        var originalId = string.IsNullOrWhiteSpace(draft.OriginalId) ? draft.Id : draft.OriginalId;
        var commandParts = new List<string>
        {
            "$result =",
            "Set-CodexApiProfile",
            "-Id " + Quote(originalId),
            "-NewId " + Quote(draft.Id),
            "-Name " + Quote(draft.Name),
            "-BaseUrl " + Quote(draft.BaseUrl),
            "-Model " + Quote(draft.Model),
            "-CodexHome " + Quote(draft.CodexHome),
            "-Workspace " + Quote(draft.Workspace),
            ";",
            "ConvertTo-Json -InputObject $result -Depth 8 -Compress"
        };
        var output = RunModule(string.Join(" ", commandParts));
        var profile = JsonSerializer.Deserialize<ProfileInfo>(output.StandardOutput.Trim(), JsonOptions) ?? new ProfileInfo { Id = draft.Id };

        if (!string.IsNullOrWhiteSpace(draft.ApiKey))
        {
            SetProfileApiKey(profile.Id, draft.ApiKey);
        }

        return profile;
    }

    private void SetProfileApiKey(string id, string apiKey)
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"codex-api-launcher-key-{Guid.NewGuid():N}.txt");
        File.WriteAllText(keyPath, apiKey, new UTF8Encoding(false));
        try
        {
            var command = string.Join(" ", new[]
            {
                "$apiKeyPath = " + Quote(keyPath) + ";",
                "$plainApiKey = Get-Content -Raw -LiteralPath $apiKeyPath;",
                "try {",
                "$secureApiKey = ConvertTo-SecureString $plainApiKey -AsPlainText -Force;",
                "Set-CodexApiProfileApiKey",
                "-Id " + Quote(id),
                "-ApiKey $secureApiKey",
                "| Out-Null",
                "} finally {",
                "Remove-Item -LiteralPath $apiKeyPath -Force -ErrorAction SilentlyContinue;",
                "$plainApiKey = $null;",
                "$secureApiKey = $null;",
                "}"
            });
            RunModule(command);
        }
        finally
        {
            try
            {
                if (File.Exists(keyPath))
                {
                    File.Delete(keyPath);
                }
            }
            catch
            {
                // Best effort cleanup for the transient key handoff file.
            }
        }
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

internal sealed class ProfileDraft
{
    public string OriginalId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string CodexHome { get; set; } = "";
    public string Workspace { get; set; } = "";
}

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
