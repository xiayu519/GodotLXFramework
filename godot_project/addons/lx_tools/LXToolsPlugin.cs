#if TOOLS
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace LX.Editor;

/// <summary>复用 lx.ps1 事实源的 Godot 编辑器入口，不维护第二套生成或验证逻辑。</summary>
[Tool]
public partial class LXToolsPlugin : EditorPlugin
{
    private VBoxContainer? _panel;
    private EditorDock? _dock;
    private Label? _status;
    private RichTextLabel? _output;
    private Tree? _problems;
    private TextureRect? _visualPreview;
    private ConfirmationDialog? _createDialog;
    private OptionButton? _createKind;
    private LineEdit? _createArguments;
    private bool _running;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        _dock = new EditorDock
        {
            Title = "LX Tools",
            LayoutKey = "lx_tools",
            DefaultSlot = EditorDock.DockSlot.Bottom,
        };
        _panel = new VBoxContainer { Name = "LXToolsPanel" };
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _dock.AddChild(_panel);
        var toolbar = new HFlowContainer();
        _panel.AddChild(toolbar);
        AddButton(toolbar, "Validate", () => StartCommand("validate"));
        AddButton(toolbar, "Generate Bindings", () => StartCommand("generate"));
        AddButton(toolbar, "Luban Data", () => StartCommand("data"));
        AddButton(toolbar, "Create…", ShowCreateDialog);
        AddButton(toolbar, "Dependencies", InspectDependencies);
        AddButton(toolbar, "Visual Compare", () => StartCommand("visual", "compare", "ui_components"));
        AddButton(toolbar, "Visual Approve", () => StartCommand("visual", "approve", "ui_components"));
        AddButton(toolbar, "Open game_design", OpenGameDesign);

        _status = new Label { Text = "LX Tools ready" };
        _panel.AddChild(_status);
        var split = new HSplitContainer { CustomMinimumSize = new Vector2(0, 320) };
        _panel.AddChild(split);
        _problems = new Tree { Columns = 3, HideRoot = true, CustomMinimumSize = new Vector2(520, 300) };
        _problems.SetColumnTitle(0, "Severity");
        _problems.SetColumnTitle(1, "Code");
        _problems.SetColumnTitle(2, "Message");
        _problems.ItemActivated += OpenSelectedProblem;
        split.AddChild(_problems);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(420, 300) };
        split.AddChild(right);
        _visualPreview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(400, 200),
        };
        right.AddChild(_visualPreview);
        _output = new RichTextLabel { FitContent = false, ScrollActive = true };
        right.AddChild(_output);
        AddDock(_dock);

        AddToolMenuItem("LXFramework/Validate", Callable.From(() => StartCommand("validate")));
        AddToolMenuItem("LXFramework/Generate Bindings", Callable.From(() => StartCommand("generate")));
        AddToolMenuItem("LXFramework/Luban Data", Callable.From(() => StartCommand("data")));
        AddToolMenuItem("LXFramework/Create…", Callable.From(ShowCreateDialog));
        RefreshVisualPreview();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        RemoveToolMenuItem("LXFramework/Validate");
        RemoveToolMenuItem("LXFramework/Generate Bindings");
        RemoveToolMenuItem("LXFramework/Luban Data");
        RemoveToolMenuItem("LXFramework/Create…");
        if (_dock is not null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
        }
        _createDialog?.QueueFree();
        _panel = null;
        _dock = null;
    }

    private static void AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void StartCommand(params string[] arguments)
    {
        if (_running)
        {
            SetStatus("A LX command is already running.");
            return;
        }
        _ = RunCommandAsync(arguments);
    }

    private async Task RunCommandAsync(IReadOnlyList<string> arguments)
    {
        _running = true;
        SetStatus($"Running lx {string.Join(' ', arguments)} …");
        try
        {
            var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('\\', '/');
            var workspaceRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
            var script = Path.Combine(workspaceRoot, "lx.ps1");
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            start.ArgumentList.Add("--json");

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("Failed to start lx.ps1.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Callable.From(() => ApplyCommandResult(
                arguments,
                process.ExitCode,
                stdout,
                stderr)).CallDeferred();
        }
        catch (Exception exception)
        {
            Callable.From(() =>
            {
                SetStatus($"LX command failed: {exception.Message}");
                if (_output is not null)
                {
                    _output.Text = exception.ToString();
                }
            }).CallDeferred();
        }
        finally
        {
            _running = false;
        }
    }

    private void ApplyCommandResult(
        IReadOnlyList<string> arguments,
        int exitCode,
        string stdout,
        string stderr)
    {
        SetStatus($"lx {string.Join(' ', arguments)} exited with {exitCode}.");
        if (_output is not null)
        {
            _output.Text = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + "\n" + stderr;
        }
        PopulateProblems(stdout);
        if (arguments.Contains("visual", StringComparer.Ordinal))
        {
            RefreshVisualPreview();
        }
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }

    private void PopulateProblems(string json)
    {
        if (_problems is null)
        {
            return;
        }
        _problems.Clear();
        var root = _problems.CreateItem();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("diagnostics", out var diagnostics))
            {
                return;
            }
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                var item = _problems.CreateItem(root);
                var severity = diagnostic.GetProperty("severity").GetString() ?? "info";
                var code = diagnostic.GetProperty("code").GetString() ?? "LX_OUTPUT";
                var message = diagnostic.GetProperty("message").GetString() ?? string.Empty;
                item.SetText(0, severity);
                item.SetText(1, code);
                item.SetText(2, message.Replace('\n', ' '));
                var location = ExtractResourcePath(message);
                if (location is not null)
                {
                    item.SetMetadata(0, location);
                }
            }
        }
        catch (JsonException exception)
        {
            var item = _problems.CreateItem(root);
            item.SetText(0, "error");
            item.SetText(1, "LX_EDITOR_JSON");
            item.SetText(2, exception.Message);
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
            SetStatus("Open a saved scene before inspecting dependencies.");
            return;
        }
        var dependencies = ResourceLoader.GetDependencies(path);
        _output!.Text = $"{path}\n\n" + string.Join('\n', dependencies);
        _problems!.Clear();
        var root = _problems.CreateItem();
        foreach (var dependency in dependencies)
        {
            var item = _problems.CreateItem(root);
            item.SetText(0, ResourceLoader.Exists(dependency) ? "info" : "error");
            item.SetText(1, "LX_RESOURCE_DEPENDENCY");
            item.SetText(2, dependency);
            item.SetMetadata(0, dependency);
        }
        SetStatus($"Found {dependencies.Length} dependencies for {path}.");
    }

    private void ShowCreateDialog()
    {
        if (_createDialog is null)
        {
            _createDialog = new ConfirmationDialog { Title = "LX Create" };
            var layout = new VBoxContainer();
            _createDialog.AddChild(layout);
            _createKind = new OptionButton();
            foreach (var kind in new[] { "game", "world", "feature", "screen", "node", "content", "input", "res" })
            {
                _createKind.AddItem(kind);
            }
            _createArguments = new LineEdit { PlaceholderText = "Arguments, for example: MainMenu main_menu" };
            layout.AddChild(new Label { Text = "Kind" });
            layout.AddChild(_createKind);
            layout.AddChild(new Label { Text = "Arguments" });
            layout.AddChild(_createArguments);
            _createDialog.Confirmed += CreateFromDialog;
            EditorInterface.Singleton.GetBaseControl().AddChild(_createDialog);
        }
        _createDialog.PopupCentered(new Vector2I(520, 220));
    }

    private void CreateFromDialog()
    {
        var kind = _createKind?.GetItemText(_createKind.Selected) ?? string.Empty;
        var raw = _createArguments?.Text ?? string.Empty;
        var arguments = Regex.Matches(raw, "(?:[^\\s\"]|\"[^\"]*\")+")
            .Select(match => match.Value.Trim('"'))
            .ToArray();
        StartCommand(new[] { "create", kind }.Concat(arguments).ToArray());
    }

    private void OpenGameDesign()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('\\', '/');
        var workspaceRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
        OS.ShellOpen(Path.Combine(workspaceRoot, "game_design"));
    }

    private void RefreshVisualPreview()
    {
        if (_visualPreview is null)
        {
            return;
        }
        var path = ProjectSettings.GlobalizePath("res://tests/Visual/Baselines/ui_components.png");
        if (!File.Exists(path))
        {
            return;
        }
        _visualPreview.Texture = ImageTexture.CreateFromImage(Image.LoadFromFile(path));
    }

    private void SetStatus(string message)
    {
        if (_status is not null)
        {
            _status.Text = message;
        }
    }

    private static string? ExtractResourcePath(string message)
    {
        var match = Regex.Match(message, "res://[^\\s:'\"]+", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }
}
#endif
