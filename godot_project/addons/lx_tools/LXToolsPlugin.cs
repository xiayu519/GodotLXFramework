#if TOOLS
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace LX.Editor;

/// <summary>为中文开发者提供 LXFramework 创建、策划数据和场景资源工具。</summary>
[Tool]
public partial class LXToolsPlugin : EditorPlugin
{
    private const double ReportPollIntervalSeconds = 0.25;

    private static readonly (string Id, string Label, string Description)[] CreateKinds =
    [
        ("game", "游戏产品层", "为当前分支创建一次游戏代码、初始世界和产品清单。"),
        ("world", "世界", "创建并注册可以由 LX.Scenes 切换的世界场景。"),
        ("feature", "功能模块", "创建可以独立生成和释放的 Feature 场景。"),
        ("screen", "UI 页面", "创建 UIScreen、场景并注册类型化 UI 目录。"),
        ("node", "Godot 节点", "创建继承 Godot 原生类型并接收 LX 上下文的节点。"),
        ("content", "JSON 内容表", "创建适合少量简单数据的类型化 JSON 内容表。"),
        ("input", "输入动作", "注册 Godot Input Map 动作并生成类型化输入目录。"),
        ("res", "资源引用", "把已有 res:// 资源注册为类型化资源引用。"),
    ];

    private EditorDock? _dock;
    private Label? _status;
    private RichTextLabel? _output;
    private Tree? _problems;
    private readonly List<Button> _commandButtons = [];
    private bool _running;
    private double _reportPollElapsed;
    private string? _lastObservedCommandId;
    private string? _lastObservedState;

    private ConfirmationDialog? _createDialog;
    private OptionButton? _createKind;
    private Label? _createDescription;
    private GridContainer? _createFields;
    private Label? _createValidation;
    private readonly Dictionary<string, LineEdit> _createInputs = new(StringComparer.Ordinal);
    private OptionButton? _resourcePolicy;
    private EditorFileDialog? _resourceFileDialog;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        _dock = new EditorDock
        {
            Title = "LX 开发工具",
            LayoutKey = "lx_tools",
            DefaultSlot = EditorDock.DockSlot.Bottom,
        };
        var panel = new VBoxContainer { Name = "LXToolsPanel" };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _dock.AddChild(panel);

        var toolbar = new HFlowContainer();
        panel.AddChild(toolbar);
        AddCommandButton(toolbar, "创建内容…", ShowCreateDialog);
        AddCommandButton(toolbar, "生成策划数据", StartDataCommand);
        AddButton(toolbar, "场景依赖", InspectDependencies);
        AddButton(toolbar, "打开策划数据目录", OpenGameDesign);

        _status = new Label { Text = "LX 工具已就绪" };
        _status.AddThemeColorOverride("font_color", new Color("b8c0cc"));
        panel.AddChild(_status);

        var split = new HSplitContainer { CustomMinimumSize = new Vector2(0, 320) };
        panel.AddChild(split);

        var problemPanel = new VBoxContainer { CustomMinimumSize = new Vector2(620, 300) };
        problemPanel.AddChild(new Label { Text = "问题与结果" });
        _problems = new Tree
        {
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(600, 270),
        };
        _problems.SetColumnTitle(0, "级别");
        _problems.SetColumnTitle(1, "代码");
        _problems.SetColumnTitle(2, "说明");
        _problems.SetColumnExpand(0, false);
        _problems.SetColumnCustomMinimumWidth(0, 72);
        _problems.SetColumnExpand(1, false);
        _problems.SetColumnCustomMinimumWidth(1, 160);
        _problems.ItemActivated += OpenSelectedProblem;
        problemPanel.AddChild(_problems);
        split.AddChild(problemPanel);

        var outputPanel = new VBoxContainer { CustomMinimumSize = new Vector2(520, 300) };
        outputPanel.AddChild(new Label { Text = "执行详情（仅在排错时查看）" });
        _output = new RichTextLabel
        {
            FitContent = false,
            ScrollActive = true,
            SelectionEnabled = true,
            CustomMinimumSize = new Vector2(500, 270),
        };
        outputPanel.AddChild(_output);
        split.AddChild(outputPanel);

        AddDock(_dock);
        AddToolMenuItem("LXFramework/创建内容…", Callable.From(ShowCreateDialog));
        AddToolMenuItem("LXFramework/生成策划数据", Callable.From(StartDataCommand));

        SetProcess(true);
        PollCommandReport(force: true);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        SetProcess(false);
        RemoveToolMenuItem("LXFramework/创建内容…");
        RemoveToolMenuItem("LXFramework/生成策划数据");
        if (_dock is not null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
        }
        _createDialog?.QueueFree();
        _resourceFileDialog?.QueueFree();
        _dock = null;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _reportPollElapsed += delta;
        if (_reportPollElapsed < ReportPollIntervalSeconds)
        {
            return;
        }

        _reportPollElapsed = 0;
        PollCommandReport(force: false);
    }

    private static Button AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void AddCommandButton(Container parent, string text, Action action)
    {
        _commandButtons.Add(AddButton(parent, text, action));
    }

    private void StartDataCommand() =>
        StartCommand(["data"], "生成策划数据");

    private void StartCommand(IReadOnlyList<string> arguments, string displayName)
    {
        PollCommandReport(force: true);
        if (_running)
        {
            SetStatus("已有 LX 操作正在执行，请等待完成。", StatusKind.Warning);
            return;
        }

        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('\\', '/');
        var workspaceRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
        var helper = Path.Combine(projectRoot, "addons", "lx_tools", "run-command.ps1");
        var commandId = Guid.NewGuid().ToString("N");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(helper);
            start.ArgumentList.Add("-WorkspaceRoot");
            start.ArgumentList.Add(workspaceRoot);
            start.ArgumentList.Add("-CommandId");
            start.ArgumentList.Add(commandId);
            start.ArgumentList.Add("-DisplayName");
            start.ArgumentList.Add(displayName);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("无法启动 LX 后台命令。");
            _running = true;
            _lastObservedCommandId = commandId;
            _lastObservedState = "starting";
            SetCommandButtonsDisabled(true);
            ClearResults();
            SetStatus($"正在执行：{displayName}…", StatusKind.Running);
        }
        catch (Exception exception)
        {
            _running = false;
            SetCommandButtonsDisabled(false);
            SetStatus($"启动失败：{displayName}", StatusKind.Error);
            ShowStandaloneProblem("错误", "LX_EDITOR_START", exception.Message, exception.ToString());
        }
    }

    private void PollCommandReport(bool force)
    {
        var report = TryReadCommandReport();
        if (report is null)
        {
            return;
        }
        if (!force &&
            string.Equals(report.CommandId, _lastObservedCommandId, StringComparison.Ordinal) &&
            string.Equals(report.State, _lastObservedState, StringComparison.Ordinal))
        {
            return;
        }

        _lastObservedCommandId = report.CommandId;
        _lastObservedState = report.State;
        if (string.Equals(report.State, "running", StringComparison.Ordinal))
        {
            if (!IsProcessRunning(report.ProcessId))
            {
                _running = false;
                SetCommandButtonsDisabled(false);
                SetStatus($"执行中断：{report.DisplayName}", StatusKind.Error);
                ShowStandaloneProblem(
                    "错误",
                    "LX_EDITOR_PROCESS_EXITED",
                    "后台进程已结束，但没有写入完成报告。请查看执行详情后重试。",
                    report.StandardError);
                return;
            }

            _running = true;
            SetCommandButtonsDisabled(true);
            SetStatus($"正在执行：{report.DisplayName}…", StatusKind.Running);
            return;
        }

        _running = false;
        SetCommandButtonsDisabled(false);
        ApplyCompletedReport(report, refreshResources: !force);
    }

    private EditorCommandReport? TryReadCommandReport()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('\\', '/');
        var workspaceRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
        var path = Path.Combine(workspaceRoot, ".lx", "editor-command.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var exitCode = root.TryGetProperty("exitCode", out var exitCodeElement) &&
                           exitCodeElement.ValueKind == JsonValueKind.Number
                ? exitCodeElement.GetInt32()
                : (int?)null;
            return new EditorCommandReport(
                root.GetProperty("commandId").GetString() ?? string.Empty,
                root.GetProperty("displayName").GetString() ?? "LX 操作",
                root.GetProperty("state").GetString() ?? "failed",
                root.TryGetProperty("processId", out var processIdElement)
                    ? processIdElement.GetInt32()
                    : 0,
                exitCode,
                root.TryGetProperty("stdout", out var stdoutElement)
                    ? stdoutElement.GetString() ?? string.Empty
                    : string.Empty,
                root.TryGetProperty("stderr", out var stderrElement)
                    ? stderrElement.GetString() ?? string.Empty
                    : string.Empty);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyCompletedReport(EditorCommandReport report, bool refreshResources)
    {
        var succeeded = string.Equals(report.State, "succeeded", StringComparison.Ordinal) &&
                        report.ExitCode == 0;
        SetStatus(
            succeeded
                ? $"成功：{report.DisplayName}"
                : $"失败：{report.DisplayName}（退出码 {report.ExitCode ?? 1}）",
            succeeded ? StatusKind.Success : StatusKind.Error);

        var diagnosticCount = PopulateProblems(report.StandardOutput);
        if (!succeeded && diagnosticCount == 0)
        {
            var message = string.IsNullOrWhiteSpace(report.StandardError)
                ? "命令执行失败，但没有返回诊断信息。"
                : FirstLine(report.StandardError);
            ShowStandaloneProblem(
                "错误",
                "LX_COMMAND_FAILED",
                message,
                BuildExecutionDetails(report));
        }
        else if (_output is not null)
        {
            _output.Text = BuildExecutionDetails(report);
        }

        var resourceFilesystem = EditorInterface.Singleton.GetResourceFilesystem();
        if (refreshResources && !resourceFilesystem.IsScanning())
        {
            resourceFilesystem.Scan();
        }
    }

    private int PopulateProblems(string json)
    {
        if (_problems is null)
        {
            return 0;
        }

        _problems.Clear();
        var root = _problems.CreateItem();
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("diagnostics", out var diagnostics))
            {
                return 0;
            }

            var count = 0;
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                var severity = diagnostic.GetProperty("severity").GetString() ?? "info";
                var code = diagnostic.GetProperty("code").GetString() ?? "LX_OUTPUT";
                var message = diagnostic.GetProperty("message").GetString() ?? string.Empty;
                var item = _problems.CreateItem(root);
                item.SetText(0, TranslateSeverity(severity));
                item.SetText(1, code);
                item.SetText(2, message.Replace('\n', ' '));
                item.SetCustomColor(0, SeverityColor(severity));
                var location = ExtractResourcePath(message);
                if (location is not null)
                {
                    item.SetMetadata(0, location);
                }
                count++;
            }
            return count;
        }
        catch (JsonException exception)
        {
            var item = _problems.CreateItem(root);
            item.SetText(0, "错误");
            item.SetText(1, "LX_EDITOR_JSON");
            item.SetText(2, $"无法解析命令报告：{exception.Message}");
            item.SetCustomColor(0, SeverityColor("error"));
            return 1;
        }
    }

    private void ShowStandaloneProblem(
        string severity,
        string code,
        string message,
        string details)
    {
        if (_problems is not null)
        {
            _problems.Clear();
            var root = _problems.CreateItem();
            var item = _problems.CreateItem(root);
            item.SetText(0, severity);
            item.SetText(1, code);
            item.SetText(2, message.Replace('\n', ' '));
            item.SetCustomColor(0, SeverityColor("error"));
        }
        if (_output is not null)
        {
            _output.Text = details;
        }
    }

    private void ClearResults()
    {
        _problems?.Clear();
        if (_output is not null)
        {
            _output.Text = string.Empty;
        }
    }

    private void SetCommandButtonsDisabled(bool disabled)
    {
        foreach (var button in _commandButtons)
        {
            button.Disabled = disabled;
        }
    }

    private void OpenSelectedProblem()
    {
        var item = _problems?.GetSelected();
        var path = item?.GetMetadata(0).AsString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        if (path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
        {
            EditorInterface.Singleton.OpenSceneFromPath(path);
            return;
        }

        var script = ResourceLoader.Load<Script>(path);
        if (script is not null)
        {
            EditorInterface.Singleton.EditScript(script);
        }
    }

    private void InspectDependencies()
    {
        var scene = EditorInterface.Singleton.GetEditedSceneRoot();
        var path = scene?.SceneFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("请先打开并保存一个场景。", StatusKind.Warning);
            return;
        }

        var dependencies = ResourceLoader.GetDependencies(path);
        if (_output is not null)
        {
            _output.Text = $"当前场景：{path}\n依赖数量：{dependencies.Length}\n\n" +
                           string.Join('\n', dependencies);
        }
        if (_problems is not null)
        {
            _problems.Clear();
            var root = _problems.CreateItem();
            foreach (var dependency in dependencies)
            {
                var exists = ResourceLoader.Exists(dependency);
                var item = _problems.CreateItem(root);
                item.SetText(0, exists ? "正常" : "缺失");
                item.SetText(1, "LX_RESOURCE_DEPENDENCY");
                item.SetText(2, dependency);
                item.SetCustomColor(0, exists ? SeverityColor("info") : SeverityColor("error"));
                item.SetMetadata(0, dependency);
            }
        }
        SetStatus($"场景依赖检查完成：共 {dependencies.Length} 项。", StatusKind.Success);
    }

    private void ShowCreateDialog()
    {
        if (_running)
        {
            SetStatus("已有 LX 操作正在执行，请等待完成。", StatusKind.Warning);
            return;
        }
        if (_createDialog is null)
        {
            BuildCreateDialog();
        }

        BuildCreateFields();
        _createDialog!.PopupCentered(new Vector2I(680, 520));
    }

    private void BuildCreateDialog()
    {
        _createDialog = new ConfirmationDialog { Title = "创建 LX 内容" };
        _createDialog.GetOkButton().Text = "创建";
        _createDialog.GetCancelButton().Text = "取消";
        var layout = new VBoxContainer();
        _createDialog.AddChild(layout);

        layout.AddChild(new Label { Text = "创建类型" });
        _createKind = new OptionButton();
        foreach (var kind in CreateKinds)
        {
            var index = _createKind.ItemCount;
            _createKind.AddItem(kind.Label);
            _createKind.SetItemMetadata(index, kind.Id);
        }
        _createKind.ItemSelected += OnCreateKindSelected;
        layout.AddChild(_createKind);

        _createDescription = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(620, 44),
        };
        layout.AddChild(_createDescription);

        _createFields = new GridContainer { Columns = 2 };
        layout.AddChild(_createFields);
        _createValidation = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _createValidation.AddThemeColorOverride("font_color", SeverityColor("error"));
        layout.AddChild(_createValidation);

        _createDialog.Confirmed += CreateFromDialog;
        EditorInterface.Singleton.GetBaseControl().AddChild(_createDialog);
        BuildCreateFields();
    }

    private void OnCreateKindSelected(long index)
    {
        BuildCreateFields();
    }

    private void BuildCreateFields()
    {
        if (_createFields is null || _createKind is null)
        {
            return;
        }

        foreach (var child in _createFields.GetChildren())
        {
            _createFields.RemoveChild(child);
            child.QueueFree();
        }
        _createInputs.Clear();
        _resourcePolicy = null;
        if (_createValidation is not null)
        {
            _createValidation.Text = string.Empty;
        }

        var kind = SelectedCreateKind();
        var metadata = CreateKinds.First(item => string.Equals(item.Id, kind, StringComparison.Ordinal));
        if (_createDescription is not null)
        {
            _createDescription.Text = metadata.Description;
        }

        switch (kind)
        {
            case "game":
                AddTextField("name", "游戏名称 *", "例如：MyGame");
                break;
            case "world":
                AddTextField("name", "世界类名 *", "例如：Dungeon");
                AddTextField("id", "世界 ID", "可选，例如：dungeon");
                break;
            case "feature":
                AddTextField("name", "功能类名 *", "例如：Player");
                AddTextField("id", "功能 ID", "可选，例如：player");
                break;
            case "screen":
                AddTextField("name", "页面类名 *", "例如：MainMenu");
                AddTextField("id", "页面 ID", "可选，例如：main_menu");
                break;
            case "node":
                AddTextField("name", "节点类名 *", "例如：PlayerBody");
                AddTextField("base", "Godot 基类 *", "例如：CharacterBody2D");
                AddTextField("id", "节点 ID", "可选，例如：player_body");
                break;
            case "content":
                AddTextField("name", "内容类型名 *", "例如：Item");
                AddTextField("id", "数据表 ID", "可选，例如：items");
                break;
            case "input":
                AddTextField("name", "代码名称 *", "例如：Jump");
                AddTextField("action", "Godot 动作名 *", "例如：game_jump");
                AddTextField("key", "默认物理按键", "可选，例如：Space");
                break;
            case "res":
                AddTextField("id", "资源 ID *", "例如：player_sprite");
                AddTextField("type", "Godot 资源类型 *", "例如：Texture2D");
                AddResourcePathField();
                AddResourcePolicyField();
                AddTextField("group", "资源分组", "可选，例如：characters");
                break;
        }
    }

    private void AddTextField(string key, string label, string placeholder)
    {
        var input = new LineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(420, 0),
        };
        _createInputs[key] = input;
        _createFields!.AddChild(new Label { Text = label });
        _createFields.AddChild(input);
    }

    private void AddResourcePathField()
    {
        var input = new LineEdit
        {
            PlaceholderText = "例如：res://content/art/player.png",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _createInputs["path"] = input;
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        row.AddChild(input);
        AddButton(row, "选择…", ShowResourceFileDialog);
        _createFields!.AddChild(new Label { Text = "资源文件 *" });
        _createFields.AddChild(row);
    }

    private void AddResourcePolicyField()
    {
        _resourcePolicy = new OptionButton { CustomMinimumSize = new Vector2(420, 0) };
        AddPolicyOption("缓存（推荐）", "Cached");
        AddPolicyOption("临时", "Transient");
        AddPolicyOption("常驻", "Resident");
        _createFields!.AddChild(new Label { Text = "缓存策略" });
        _createFields.AddChild(_resourcePolicy);
    }

    private void AddPolicyOption(string label, string value)
    {
        var index = _resourcePolicy!.ItemCount;
        _resourcePolicy.AddItem(label);
        _resourcePolicy.SetItemMetadata(index, value);
    }

    private void ShowResourceFileDialog()
    {
        if (_resourceFileDialog is null)
        {
            _resourceFileDialog = new EditorFileDialog
            {
                Title = "选择要注册的资源",
                Access = EditorFileDialog.AccessEnum.Resources,
                FileMode = EditorFileDialog.FileModeEnum.OpenFile,
            };
            _resourceFileDialog.FileSelected += OnResourceFileSelected;
            EditorInterface.Singleton.GetBaseControl().AddChild(_resourceFileDialog);
        }
        _resourceFileDialog.PopupFileDialog();
    }

    private void OnResourceFileSelected(string path)
    {
        if (_createInputs.TryGetValue("path", out var input))
        {
            input.Text = path;
        }
    }

    private void CreateFromDialog()
    {
        var kind = SelectedCreateKind();
        var arguments = new List<string> { "create", kind };
        var required = kind switch
        {
            "game" => new[] { "name" },
            "world" or "feature" or "screen" or "content" => ["name"],
            "node" => ["name", "base"],
            "input" => ["name", "action"],
            "res" => ["id", "type", "path"],
            _ => [],
        };
        foreach (var key in required)
        {
            if (string.IsNullOrWhiteSpace(InputValue(key)))
            {
                if (_createValidation is not null)
                {
                    _createValidation.Text = "请填写所有带 * 的必填项。";
                }
                _createDialog?.PopupCentered(new Vector2I(680, 520));
                return;
            }
        }

        switch (kind)
        {
            case "game":
                arguments.Add(InputValue("name"));
                break;
            case "world":
            case "feature":
            case "screen":
            case "content":
                arguments.Add(InputValue("name"));
                AddOptionalArgument(arguments, InputValue("id"));
                break;
            case "node":
                arguments.Add(InputValue("name"));
                arguments.Add(InputValue("base"));
                AddOptionalArgument(arguments, InputValue("id"));
                break;
            case "input":
                arguments.Add(InputValue("name"));
                arguments.Add(InputValue("action"));
                AddOptionalArgument(arguments, InputValue("key"));
                break;
            case "res":
                arguments.Add(InputValue("id"));
                arguments.Add(InputValue("type"));
                arguments.Add(InputValue("path"));
                arguments.Add(_resourcePolicy?.GetItemMetadata(_resourcePolicy.Selected).AsString() ?? "Cached");
                AddOptionalArgument(arguments, InputValue("group"));
                break;
        }

        var label = CreateKinds.First(item => string.Equals(item.Id, kind, StringComparison.Ordinal)).Label;
        StartCommand(arguments, $"创建{label} {arguments[2]}");
    }

    private string SelectedCreateKind() =>
        _createKind?.GetItemMetadata(_createKind.Selected).AsString() ?? "game";

    private string InputValue(string key) =>
        _createInputs.TryGetValue(key, out var input) ? input.Text.Trim() : string.Empty;

    private static void AddOptionalArgument(ICollection<string> arguments, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(value);
        }
    }

    private void OpenGameDesign()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('\\', '/');
        var workspaceRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
        OS.ShellOpen(Path.Combine(workspaceRoot, "game_design"));
        SetStatus("已打开策划数据目录。", StatusKind.Success);
    }

    private void SetStatus(string message, StatusKind kind)
    {
        if (_status is null)
        {
            return;
        }

        _status.Text = message;
        _status.AddThemeColorOverride("font_color", kind switch
        {
            StatusKind.Success => new Color("67d391"),
            StatusKind.Warning => new Color("e6b566"),
            StatusKind.Error => new Color("ef6b73"),
            StatusKind.Running => new Color("6aa9ff"),
            _ => new Color("b8c0cc"),
        });
    }

    private static string BuildExecutionDetails(EditorCommandReport report)
    {
        var status = string.Equals(report.State, "succeeded", StringComparison.Ordinal)
            ? "成功"
            : "失败";
        var details = $"操作：{report.DisplayName}\n状态：{status}\n退出码：{report.ExitCode ?? 1}";
        if (!string.IsNullOrWhiteSpace(report.StandardOutput))
        {
            details += $"\n\n标准输出\n{report.StandardOutput.Trim()}";
        }
        if (!string.IsNullOrWhiteSpace(report.StandardError))
        {
            details += $"\n\n错误输出\n{report.StandardError.Trim()}";
        }
        return details;
    }

    private static string FirstLine(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        var newline = normalized.IndexOf('\n');
        return newline >= 0 ? normalized[..newline] : normalized;
    }

    private static string TranslateSeverity(string severity) => severity switch
    {
        "error" => "错误",
        "warning" => "警告",
        _ => "信息",
    };

    private static Color SeverityColor(string severity) => severity switch
    {
        "error" => new Color("ef6b73"),
        "warning" => new Color("e6b566"),
        _ => new Color("67d391"),
    };

    private static string? ExtractResourcePath(string message)
    {
        var match = Regex.Match(message, "res://[^\\s:'\"]+", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }

    private enum StatusKind
    {
        Ready,
        Running,
        Success,
        Warning,
        Error,
    }

    private sealed record EditorCommandReport(
        string CommandId,
        string DisplayName,
        string State,
        int ProcessId,
        int? ExitCode,
        string StandardOutput,
        string StandardError);
}
#endif
