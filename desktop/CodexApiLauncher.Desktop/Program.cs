using System.Diagnostics;
using System.Drawing.Drawing2D;
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

    internal static void ApplyAppIcon(Form form)
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                form.Icon = icon;
            }
        }
        catch
        {
            // The embedded executable icon is optional at runtime.
        }
    }
}

internal enum LauncherPage
{
    Dashboard,
    Config,
    Logs,
    Settings
}

internal sealed class LauncherForm : Form
{
    private readonly PowerShellBridge bridge = new(AppContext.BaseDirectory);
    private readonly ToolTip toolTip = new();

    private readonly Color windowColor = Color.FromArgb(255, 255, 255);
    private readonly Color surfaceColor = Color.FromArgb(255, 255, 255);
    private readonly Color fieldColor = Color.FromArgb(255, 255, 255);
    private readonly Color borderColor = Color.FromArgb(204, 204, 204);
    private readonly Color textColor = Color.FromArgb(26, 26, 26);
    private readonly Color mutedColor = Color.FromArgb(77, 77, 77);
    private readonly Color primaryColor = Color.FromArgb(26, 26, 26);
    private readonly Color primaryDarkColor = Color.FromArgb(0, 0, 0);
    private readonly Color softColor = Color.FromArgb(245, 245, 245);
    private readonly Color outputBackColor = Color.FromArgb(26, 26, 26);
    private readonly Color outputTextColor = Color.FromArgb(255, 255, 255);

    private ListView profileList = null!;
    private TextBox providerNameBox = null!;
    private TextBox providerIdBox = null!;
    private ComboBox providerModelBox = null!;
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
    private Button fetchModelsButton = null!;
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
    private RichTextBox logsText = null!;
    private Label configSummaryLabel = null!;
    private Label settingsSummaryLabel = null!;
    private Button dashboardNavButton = null!;
    private Button configNavButton = null!;
    private Button logsNavButton = null!;
    private Button settingsNavButton = null!;
    private Panel dashboardPage = null!;
    private Panel configPage = null!;
    private Panel logsPage = null!;
    private Panel settingsPage = null!;

    private bool isBusy;
    private LauncherPage activePage = LauncherPage.Dashboard;
    private ProfileInfo? activeProfile;

    public LauncherForm()
    {
        BuildUi();
        Shown += async (_, _) => await RefreshProfilesAsync();
    }

    private Font UiFont(float size = 9.0f, FontStyle style = FontStyle.Regular)
    {
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fontName = families.Contains("Space Grotesk")
            ? "Space Grotesk"
            : families.Contains("Microsoft YaHei UI")
                ? "Microsoft YaHei UI"
                : "Segoe UI";
        return new Font(fontName, size, style);
    }

    private Font MonoFont(float size = 9.0f)
    {
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fontName = families.Contains("JetBrains Mono")
            ? "JetBrains Mono"
            : families.Contains("Cascadia Mono")
                ? "Cascadia Mono"
                : "Consolas";
        return new Font(fontName, size, FontStyle.Regular);
    }

    private void BuildUi()
    {
        Text = "CodexCLI API 多开启动器";
        Program.ApplyAppIcon(this);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1140, 900);
        MinimumSize = new Size(960, 820);
        BackColor = windowColor;
        Font = UiFont();

        var topBar = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 72),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = surfaceColor
        };
        Controls.Add(topBar);

        topBar.Controls.Add(NewLabel(">_", 24, 22, 36, 26, 11, FontStyle.Bold, primaryColor));
        topBar.Controls.Add(NewLabel("CodexCLI API 多开启动器", 68, 18, 330, 32, 14, FontStyle.Bold, primaryColor));
        dashboardNavButton = NewNavButton("仪表盘", 462, 18, 74, LauncherPage.Dashboard);
        configNavButton = NewNavButton("配置", 546, 18, 58, LauncherPage.Config);
        logsNavButton = NewNavButton("日志", 614, 18, 58, LauncherPage.Logs);
        settingsNavButton = NewNavButton("设置", 682, 18, 58, LauncherPage.Settings);
        topBar.Controls.Add(dashboardNavButton);
        topBar.Controls.Add(configNavButton);
        topBar.Controls.Add(logsNavButton);
        topBar.Controls.Add(settingsNavButton);
        var topBorder = NewSeparator(0, 71, ClientSize.Width);
        topBorder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(topBorder);

        var leftPanel = NewPanel(0, 72, 280, 804);
        leftPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        Controls.Add(leftPanel);

        var sideBorder = NewSeparator(280, 72, 1);
        sideBorder.Size = new Size(1, 804);
        sideBorder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        Controls.Add(sideBorder);

        var rightPanel = NewPanel(304, 96, 804, 780);
        dashboardPage = rightPanel;
        dashboardPage.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(dashboardPage);

        leftPanel.Controls.Add(NewLabel("API 配置文件", 24, 24, 188, 28, 12, FontStyle.Bold));

        var refreshButton = NewIconButton("refresh", 222, 20, 34, 34);
        toolTip.SetToolTip(refreshButton, "刷新配置列表");
        refreshButton.Click += async (_, _) => await RefreshProfilesAsync();
        leftPanel.Controls.Add(refreshButton);

        addProfileButton = NewButton("新增配置", 24, 652, 232, 34);
        addProfileButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        addProfileButton.Click += async (_, _) => await AddProfileAsync();
        leftPanel.Controls.Add(addProfileButton);

        profileList = new ListView
        {
            Location = new Point(16, 76),
            Size = new Size(248, 438),
            Font = UiFont(9.0f),
            BorderStyle = BorderStyle.None,
            BackColor = softColor,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.None,
            HideSelection = false,
            MultiSelect = false,
            OwnerDraw = true
        };
        profileList.SmallImageList = new ImageList { ImageSize = new Size(1, 38) };
        profileList.SelectedIndexChanged += (_, _) => UpdateSelectedProfile();
        profileList.Resize += (_, _) => ResizeProfileColumns();
        profileList.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        profileList.DrawSubItem += DrawProfileSubItem;
        profileList.Columns.Add("配置");
        ResizeProfileColumns();
        leftPanel.Controls.Add(profileList);

        var profileHint = NewLabel("每个配置都保持独立的凭据、CODEX_HOME、会话和日志。", 24, 534, 232, 52, 9, FontStyle.Regular, mutedColor);
        profileHint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        leftPanel.Controls.Add(profileHint);

        var openLaunchersButton = NewButton("打开快捷启动脚本目录", 24, 600, 232, 34);
        openLaunchersButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        openLaunchersButton.Click += (_, _) => OpenFolder(bridge.GetLaunchersDir());
        leftPanel.Controls.Add(openLaunchersButton);

        rightPanel.Controls.Add(NewLabel("当前 API 配置", 16, 16, 170, 28, 12, FontStyle.Bold));

        saveProfileButton = NewButton("保存修改", 580, 14, 96, 32, primary: true);
        saveProfileButton.Click += async (_, _) => await SaveProfileChangesAsync();
        rightPanel.Controls.Add(saveProfileButton);

        homeButton = NewButton("打开目录", 690, 14, 96, 32);
        homeButton.Click += (_, _) =>
        {
            var profile = SelectedProfile();
            if (profile is not null)
            {
                OpenFolder(profile.CodexHome);
            }
        };
        rightPanel.Controls.Add(homeButton);

        providerNameBox = AddEditableField(rightPanel, "显示名称", 16, 58, 520);
        providerIdBox = AddEditableField(rightPanel, "供应商 ID", 16, 92, 520);
        providerModelBox = AddModelField(rightPanel, "模型", 16, 126);
        providerBaseUrlBox = AddEditableField(rightPanel, "中转地址", 16, 160, 520);
        providerApiKeyBox = AddEditableField(rightPanel, "API Key", 16, 194, 520);
        providerApiKeyBox.UseSystemPasswordChar = true;
        providerApiKeyBox.PlaceholderText = "留空则保留现有 API Key";
        providerCodexHomeBox = AddCodexHomeField(rightPanel, "配置目录", 16, 228);

        rightPanel.Controls.Add(NewSeparator(16, 274, 772));
        rightPanel.Controls.Add(NewLabel("项目文件夹", 16, 294, 180, 26, 11, FontStyle.Bold));

        savedProjectLabel = NewLabel("已保存默认项目：无", 16, 320, 772, 24, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(savedProjectLabel);

        workspaceBox = new TextBox
        {
            Location = new Point(16, 348),
            Size = new Size(520, 28),
            Font = UiFont(9.5f)
        };
        workspaceBox.TextChanged += (_, _) => UpdateButtons();
        rightPanel.Controls.Add(workspaceBox);

        var browseButton = NewButton("选择文件夹", 552, 344, 112, 32, primary: true);
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

        saveProjectButton = NewButton("保存默认", 546, 384, 102, 32);
        saveProjectButton.Click += async (_, _) => await SaveWorkspaceAsync();
        rightPanel.Controls.Add(saveProjectButton);

        clearProjectButton = NewButton("清除默认", 660, 384, 108, 32);
        clearProjectButton.Click += async (_, _) => await ClearWorkspaceAsync();
        rightPanel.Controls.Add(clearProjectButton);

        rightPanel.Controls.Add(NewSeparator(16, 434, 772));

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

        outputMetaLabel = NewLabel("空闲", 108, 522, 160, 24, 9, FontStyle.Regular, mutedColor);
        rightPanel.Controls.Add(outputMetaLabel);

        copyOutputButton = NewButton("复制输出", 548, 516, 112, 32);
        copyOutputButton.Click += (_, _) => CopyOutput();
        copyOutputButton.Enabled = false;
        rightPanel.Controls.Add(copyOutputButton);

        clearOutputButton = NewButton("清空", 672, 516, 100, 32);
        clearOutputButton.Click += (_, _) => ClearOutput();
        rightPanel.Controls.Add(clearOutputButton);

        statusText = new RichTextBox
        {
            Location = new Point(16, 556),
            Size = new Size(772, 152),
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

        configPage = BuildConfigPage();
        logsPage = BuildLogsPage();
        settingsPage = BuildSettingsPage();
        Controls.Add(configPage);
        Controls.Add(logsPage);
        Controls.Add(settingsPage);

        ShowPage(LauncherPage.Dashboard);
        UpdateButtons();
    }

    private Button NewNavButton(string text, int x, int y, int width, LauncherPage page)
    {
        var button = NewButton(text, x, y, width, 36);
        if (button is LauncherButton launcherButton)
        {
            launcherButton.IsNavigation = true;
        }
        button.Click += (_, _) => ShowPage(page);
        return button;
    }

    private Button NewIconButton(string iconKind, int x, int y, int width, int height)
    {
        var button = NewButton("", x, y, width, height);
        if (button is LauncherButton launcherButton)
        {
            launcherButton.IconKind = iconKind;
        }
        return button;
    }

    private ComboBox AddModelField(Control parent, string label, int x, int y)
    {
        parent.Controls.Add(NewLabel(label, x, y + 4, 86, 24, 9, FontStyle.Bold, mutedColor));
        var box = new ComboBox
        {
            Location = new Point(x + 100, y),
            Size = new Size(410, 28),
            Font = UiFont(9.5f),
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        parent.Controls.Add(box);

        fetchModelsButton = NewButton("获取模型", x + 550, y - 2, 104, 32);
        fetchModelsButton.Click += async (_, _) => await FetchModelsAsync();
        parent.Controls.Add(fetchModelsButton);
        return box;
    }

    private Panel BuildConfigPage()
    {
        var page = NewPanel(304, 96, 804, 780);
        page.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        page.Visible = false;
        page.Controls.Add(NewLabel("配置", 16, 16, 180, 30, 14, FontStyle.Bold));
        page.Controls.Add(NewLabel("这里集中处理本机配置文件、快捷启动脚本和当前供应商目录。", 16, 52, 760, 24, 9, FontStyle.Regular, mutedColor));

        configSummaryLabel = NewLabel("", 16, 92, 772, 120, 9.5f, FontStyle.Regular, textColor);
        page.Controls.Add(configSummaryLabel);

        var openRootButton = NewButton("打开配置根目录", 16, 236, 150, 34);
        openRootButton.Click += (_, _) => OpenFolder(GetDefaultLauncherHome());
        page.Controls.Add(openRootButton);

        var openLaunchersButton = NewButton("打开快捷脚本", 184, 236, 128, 34);
        openLaunchersButton.Click += (_, _) => OpenFolder(bridge.GetLaunchersDir());
        page.Controls.Add(openLaunchersButton);

        var openCurrentHomeButton = NewButton("打开当前 CODEX_HOME", 16, 286, 174, 34);
        openCurrentHomeButton.Click += (_, _) =>
        {
            var profile = SelectedProfile();
            OpenFolder(profile?.CodexHome);
        };
        page.Controls.Add(openCurrentHomeButton);

        var refreshButton = NewButton("刷新配置", 208, 286, 110, 34);
        refreshButton.Click += async (_, _) => await RefreshProfilesAsync(SelectedProfile()?.Id);
        page.Controls.Add(refreshButton);
        return page;
    }

    private Panel BuildLogsPage()
    {
        var page = NewPanel(304, 96, 804, 780);
        page.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        page.Visible = false;
        page.Controls.Add(NewLabel("日志", 16, 16, 180, 30, 14, FontStyle.Bold));
        page.Controls.Add(NewLabel("显示最近一次启动、检查或配置操作的输出。", 16, 52, 760, 24, 9, FontStyle.Regular, mutedColor));

        var copyButton = NewButton("复制日志", 556, 16, 96, 32);
        copyButton.Click += (_, _) => CopyOutput();
        page.Controls.Add(copyButton);

        var clearButton = NewButton("清空日志", 666, 16, 96, 32);
        clearButton.Click += (_, _) => ClearOutput();
        page.Controls.Add(clearButton);

        logsText = new RichTextBox
        {
            Location = new Point(16, 92),
            Size = new Size(772, 580),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = MonoFont(9.5f),
            BackColor = outputBackColor,
            ForeColor = outputTextColor,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            WordWrap = false
        };
        page.Controls.Add(logsText);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        var page = NewPanel(304, 96, 804, 780);
        page.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        page.Visible = false;
        page.Controls.Add(NewLabel("设置", 16, 16, 180, 30, 14, FontStyle.Bold));
        page.Controls.Add(NewLabel("查看当前应用路径、运行时目录和公开仓库入口。", 16, 52, 760, 24, 9, FontStyle.Regular, mutedColor));

        settingsSummaryLabel = NewLabel("", 16, 92, 772, 150, 9.5f, FontStyle.Regular, textColor);
        page.Controls.Add(settingsSummaryLabel);

        var openAppDirButton = NewButton("打开应用目录", 16, 266, 128, 34);
        openAppDirButton.Click += (_, _) => OpenFolder(AppContext.BaseDirectory);
        page.Controls.Add(openAppDirButton);

        var openRuntimeButton = NewButton("打开运行时目录", 162, 266, 140, 34);
        openRuntimeButton.Click += (_, _) => OpenFolder(GetDefaultLauncherHome());
        page.Controls.Add(openRuntimeButton);

        var openDesktopButton = NewButton("打开桌面", 314, 266, 104, 34);
        openDesktopButton.Click += (_, _) => OpenFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        page.Controls.Add(openDesktopButton);

        var githubButton = NewButton("打开 GitHub", 432, 266, 112, 34);
        githubButton.Click += (_, _) => OpenUrl("https://github.com/Wyy326/codex-api-launcher-windows");
        page.Controls.Add(githubButton);
        return page;
    }

    private void ShowPage(LauncherPage page)
    {
        activePage = page;
        if (dashboardPage is not null) dashboardPage.Visible = page == LauncherPage.Dashboard;
        if (configPage is not null) configPage.Visible = page == LauncherPage.Config;
        if (logsPage is not null) logsPage.Visible = page == LauncherPage.Logs;
        if (settingsPage is not null) settingsPage.Visible = page == LauncherPage.Settings;
        UpdateNavState();
        UpdateInfoPages();
    }

    private void UpdateNavState()
    {
        SetNavActive(dashboardNavButton, activePage == LauncherPage.Dashboard);
        SetNavActive(configNavButton, activePage == LauncherPage.Config);
        SetNavActive(logsNavButton, activePage == LauncherPage.Logs);
        SetNavActive(settingsNavButton, activePage == LauncherPage.Settings);
    }

    private static void SetNavActive(Button? button, bool active)
    {
        if (button is LauncherButton launcherButton)
        {
            launcherButton.IsActive = active;
        }
    }

    private void UpdateInfoPages()
    {
        var profile = SelectedProfile();
        if (configSummaryLabel is not null)
        {
            configSummaryLabel.Text = string.Join(Environment.NewLine, new[]
            {
                $"配置根目录: {GetDefaultLauncherHome()}",
                $"快捷脚本目录: {bridge.GetLaunchersDir()}",
                $"当前供应商: {profile?.Name ?? "未选择"}",
                $"当前 CODEX_HOME: {profile?.CodexHome ?? "未选择"}",
            });
        }

        if (settingsSummaryLabel is not null)
        {
            settingsSummaryLabel.Text = string.Join(Environment.NewLine, new[]
            {
                $"应用目录: {AppContext.BaseDirectory}",
                $"运行时目录: {GetDefaultLauncherHome()}",
                $"桌面目录: {Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}",
                "仓库: https://github.com/Wyy326/codex-api-launcher-windows",
            });
        }
    }

    private async Task AddProfileAsync()
    {
        using var dialog = new AddProfileForm(UiFont, windowColor, surfaceColor, textColor, mutedColor);
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

    private async Task FetchModelsAsync()
    {
        var profile = RequireProfile();
        await RunUiActionAsync("正在从当前供应商的 /models 获取模型列表...", () =>
        {
            var models = bridge.GetModels(profile.Id);
            BeginInvoke((Action)(() =>
            {
                var current = providerModelBox.Text.Trim();
                providerModelBox.BeginUpdate();
                providerModelBox.Items.Clear();
                foreach (var model in models)
                {
                    providerModelBox.Items.Add(model);
                }
                providerModelBox.EndUpdate();

                if (!string.IsNullOrWhiteSpace(current) && models.Contains(current, StringComparer.OrdinalIgnoreCase))
                {
                    providerModelBox.Text = current;
                }
                else if (models.Count > 0 && string.IsNullOrWhiteSpace(current))
                {
                    providerModelBox.Text = models[0];
                }

                SetStatus($"已从 /models 获取 {models.Count} 个模型。可在模型下拉框中选择。");
            }));
        }, timeoutMilliseconds: 60_000);
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
            Size = new Size(410, 28),
            Font = UiFont(9.5f),
            ReadOnly = false,
            BackColor = fieldColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        parent.Controls.Add(box);

        migrateHomeButton = NewButton("迁移目录", x + 550, y - 2, 104, 32);
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
        var button = new LauncherButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = UiFont(9, primary ? FontStyle.Bold : FontStyle.Regular),
            IsPrimary = primary
        };
        return button;
    }

    private void DrawProfileSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null)
        {
            return;
        }

        var item = e.Item;
        var selected = item.Selected;
        using var canvasBrush = new SolidBrush(surfaceColor);
        e.Graphics.FillRectangle(canvasBrush, e.Bounds);

        var rowBounds = new Rectangle(
            e.Bounds.X + 2,
            e.Bounds.Y + 3,
            Math.Max(0, e.Bounds.Width - 4),
            Math.Max(0, e.Bounds.Height - 6));
        var background = selected
            ? primaryColor
            : item.Index % 2 == 0
                ? Color.White
                : softColor;

        using var rowPath = RoundedRect(rowBounds, 10);
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillPath(backgroundBrush, rowPath);

        var color = selected ? Color.White : textColor;
        using var font = UiFont(9.0f, selected ? FontStyle.Bold : FontStyle.Regular);
        var textBounds = new Rectangle(rowBounds.X + 10, rowBounds.Y, Math.Max(0, rowBounds.Width - 18), rowBounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem?.Text ?? "",
            font,
            textBounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (!selected)
        {
            using var linePen = new Pen(Color.FromArgb(238, 238, 238));
            e.Graphics.DrawLine(linePen, rowBounds.Left + 10, rowBounds.Bottom, rowBounds.Right - 10, rowBounds.Bottom);
        }
    }

    private void ResizeProfileColumns()
    {
        if (profileList.Columns.Count == 0)
        {
            return;
        }

        var width = Math.Max(200, profileList.ClientSize.Width);
        profileList.Columns[0].Width = width;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Label NewSeparator(int x, int y, int width)
    {
        return new Label
        {
            BackColor = borderColor,
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
                    var item = new ListViewItem(ProfileDisplayText(profile));
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
                    var profileToSelect = itemToSelect.Tag as ProfileInfo;
                    itemToSelect.Focused = true;
                    itemToSelect.Selected = true;
                    itemToSelect.EnsureVisible();
                    profileList.FocusedItem = itemToSelect;
                    profileList.Select();
                    profileList.Refresh();
                    PopulateSelectedProfile(profileToSelect);
                    SetStatus($"已加载 {profiles.Count} 个供应商配置。选择项目文件夹后即可启动。");
                }
                else
                {
                    activeProfile = null;
                    SetStatus("还没有找到任何供应商配置。请先新增供应商。");
                    PopulateSelectedProfile(null);
                }

            }));
        });
    }

    private void UpdateSelectedProfile()
    {
        var profile = SelectedProfile();
        PopulateSelectedProfile(profile);
    }

    private void PopulateSelectedProfile(ProfileInfo? profile)
    {
        activeProfile = profile;
        if (profile is null)
        {
            providerNameBox.Text = "";
            providerIdBox.Text = "";
            providerModelBox.Items.Clear();
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
        providerModelBox.Items.Clear();
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
        UpdateInfoPages();
    }

    private ProfileInfo? SelectedProfile()
    {
        return profileList.SelectedItems.Count == 0 ? activeProfile : profileList.SelectedItems[0].Tag as ProfileInfo;
    }

    private static string ProfileDisplayText(ProfileInfo profile)
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
        addProfileButton.Enabled = !isBusy;
        saveProfileButton.Enabled = !isBusy && hasProfile;
        migrateHomeButton.Enabled = !isBusy && hasProfile;
        fetchModelsButton.Enabled = !isBusy && hasProfile;
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
        if (logsText is not null)
        {
            logsText.Clear();
            logsText.SelectionColor = outputTextColor;
            logsText.AppendText(text);
            logsText.SelectionStart = 0;
            logsText.ScrollToCaret();
        }
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
        logsText?.Clear();
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

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}

internal sealed class AddProfileForm : Form
{
    private readonly Func<float, FontStyle, Font> uiFont;
    private readonly Color surfaceColor;
    private readonly Color textColor;
    private readonly Color mutedColor;

    private TextBox nameBox = null!;
    private TextBox idBox = null!;
    private TextBox baseUrlBox = null!;
    private ComboBox modelBox = null!;
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
        Color textColor,
        Color mutedColor)
    {
        this.uiFont = uiFont;
        this.surfaceColor = surfaceColor;
        this.textColor = textColor;
        this.mutedColor = mutedColor;

        Text = "新增供应商";
        Program.ApplyAppIcon(this);
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
        modelBox = AddModelRow(panel, 186);
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

        validationLabel = NewLabel("", 28, 574, 540, 26, 9, FontStyle.Regular, Color.FromArgb(51, 51, 51));
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
            PlaceholderText = placeholder,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
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
            PlaceholderText = "手动指定，例如：welfare-0xpsyche",
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        parent.Controls.Add(box);

        var button = NewButton("从标题生成", 590, y - 2, 102, 32);
        button.Click += async (_, _) => await FillSupplierIdFromSiteTitleAsync();
        parent.Controls.Add(button);
        return box;
    }

    private ComboBox AddModelRow(Control parent, int y)
    {
        parent.Controls.Add(NewLabel("模型", 20, y + 4, 132, 24, 9, FontStyle.Bold));
        var box = new ComboBox
        {
            Location = new Point(166, y),
            Size = new Size(398, 28),
            Font = uiFont(9.5f, FontStyle.Regular),
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        parent.Controls.Add(box);

        var button = NewButton("获取模型", 590, y - 2, 102, 32);
        button.Click += async (_, _) => await FetchDraftModelsAsync();
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
            PlaceholderText = placeholder,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
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

    private async Task FetchDraftModelsAsync()
    {
        var baseUrl = baseUrlBox.Text.Trim();
        var apiKey = apiKeyBox.Text;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            validationLabel.Text = "请先填写完整的 http 或 https 中转地址。";
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            validationLabel.Text = "请先填写 API Key，再获取模型列表。";
            return;
        }

        try
        {
            UseWaitCursor = true;
            validationLabel.Text = "正在请求 /models...";
            var models = await FetchModelIdsAsync(baseUrl, apiKey);
            var current = modelBox.Text.Trim();

            modelBox.BeginUpdate();
            try
            {
                modelBox.Items.Clear();
                foreach (var model in models)
                {
                    modelBox.Items.Add(model);
                }
            }
            finally
            {
                modelBox.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(current) && models.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                modelBox.Text = current;
            }
            else if (models.Count > 0 && string.IsNullOrWhiteSpace(current))
            {
                modelBox.Text = models[0];
            }

            validationLabel.Text = models.Count == 0
                ? "/models 返回成功，但没有识别到模型 ID。"
                : $"已获取 {models.Count} 个模型，可在下拉框中选择。";
        }
        catch (Exception ex)
        {
            validationLabel.Text = $"获取模型失败：{ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static async Task<List<string>> FetchModelIdsAsync(string baseUrl, string apiKey)
    {
        var modelsUrl = JoinProviderEndpoint(baseUrl, "/models");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey.Trim());
        request.Headers.TryAddWithoutValidation("User-Agent", "CodexApiLauncher/0.3");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"/models 请求失败，HTTP {(int)response.StatusCode}: {TrimForValidation(body, 260)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        using var document = JsonDocument.Parse(body);
        var modelIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        ExtractModelIds(document.RootElement, modelIds);
        return modelIds.ToList();
    }

    private static string JoinProviderEndpoint(string baseUrl, string suffix)
    {
        return baseUrl.TrimEnd('/') + suffix;
    }

    private static void ExtractModelIds(JsonElement root, ISet<string> modelIds)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            AddModelItems(root, modelIds);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("data", out var data))
        {
            AddModelItems(data, modelIds);
        }

        if (root.TryGetProperty("models", out var models))
        {
            AddModelItems(models, modelIds);
        }
    }

    private static void AddModelItems(JsonElement value, ISet<string> modelIds)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                AddModelItem(item, modelIds);
            }
            return;
        }

        AddModelItem(value, modelIds);
    }

    private static void AddModelItem(JsonElement item, ISet<string> modelIds)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            AddModelId(item.GetString(), modelIds);
            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var propertyName in new[] { "id", "model", "name" })
        {
            if (item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                AddModelId(value.GetString(), modelIds);
                return;
            }
        }
    }

    private static void AddModelId(string? modelId, ISet<string> modelIds)
    {
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            modelIds.Add(modelId.Trim());
        }
    }

    private static string TrimForValidation(string value, int maxLength)
    {
        var clean = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength] + "...";
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
            ForeColor = color ?? textColor,
            BackColor = Color.Transparent
        };
    }

    private Button NewButton(string text, int x, int y, int width, int height, bool primary = false)
    {
        var button = new LauncherButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = uiFont(9, primary ? FontStyle.Bold : FontStyle.Regular),
            IsPrimary = primary
        };
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

internal sealed class LauncherButton : Button
{
    private bool hovering;
    private bool pressing;
    private bool isActive;

    public bool IsPrimary { get; set; }
    public bool IsNavigation { get; set; }
    public string IconKind { get; set; } = "";

    public bool IsActive
    {
        get => isActive;
        set
        {
            isActive = value;
            Invalidate();
        }
    }

    public LauncherButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovering = false;
        pressing = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressing = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressing = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Math.Min(12, Math.Max(6, Height / 3));

        var colors = ResolveColors();
        using var path = RoundedRect(rect, radius);
        using var background = new SolidBrush(colors.Background);
        e.Graphics.FillPath(background, path);
        using var border = new Pen(colors.Border);
        e.Graphics.DrawPath(border, path);

        if (IsActive && IsNavigation)
        {
            using var activePen = new Pen(Color.FromArgb(26, 26, 26), 2);
            e.Graphics.DrawLine(activePen, 12, Height - 4, Width - 12, Height - 4);
        }

        if (IconKind.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            DrawRefreshIcon(e.Graphics, colors.Foreground);
        }
        else
        {
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                colors.Foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private (Color Background, Color Foreground, Color Border) ResolveColors()
    {
        var black = Color.FromArgb(26, 26, 26);
        var dark = Color.Black;
        var white = Color.White;
        var soft = Color.FromArgb(245, 245, 245);
        var border = Color.FromArgb(204, 204, 204);
        var muted = Color.FromArgb(102, 102, 102);
        var disabledBack = Color.FromArgb(229, 229, 229);
        var disabledText = Color.FromArgb(77, 77, 77);

        if (!string.IsNullOrWhiteSpace(IconKind))
        {
            var iconBack = pressing
                ? Color.FromArgb(238, 238, 238)
                : hovering
                    ? soft
                    : white;
            return (iconBack, black, Color.FromArgb(153, 153, 153));
        }

        if (!Enabled)
        {
            return (disabledBack, disabledText, Color.FromArgb(210, 210, 210));
        }

        if (IsPrimary)
        {
            return (pressing ? dark : hovering ? Color.FromArgb(51, 51, 51) : black, white, dark);
        }

        if (IsNavigation)
        {
            if (IsActive)
            {
                return (soft, black, soft);
            }
            return (hovering ? soft : white, hovering ? black : muted, hovering ? soft : white);
        }

        return (hovering ? soft : white, black, border);
    }

    private void DrawRefreshIcon(Graphics graphics, Color color)
    {
        var size = Math.Min(Width, Height) - 14;
        var x = (Width - size) / 2;
        var y = (Height - size) / 2;
        using var pen = new Pen(color, 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(pen, x, y, size, size, 35, 280);
        var tip = new PointF(x + size - 1, y + size * 0.36f);
        graphics.DrawLine(pen, tip, new PointF(tip.X - 6, tip.Y - 1));
        graphics.DrawLine(pen, tip, new PointF(tip.X - 1, tip.Y + 6));
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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

    public List<string> GetModels(string id)
    {
        var output = RunModule($"$result = @(Get-CodexApiProfileModels -Id {Quote(id)}); ConvertTo-Json -InputObject $result -Depth 8 -Compress", timeoutMilliseconds: 60_000);
        var json = output.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        if (json.StartsWith("\"", StringComparison.Ordinal))
        {
            var single = JsonSerializer.Deserialize<string>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single };
        }

        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
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
