﻿using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using KustoLoco.Core;
using KustoLoco.Rendering;
using Lokql.Engine;
using Microsoft.Web.WebView2.Core.Raw;
using Microsoft.Win32;
using NotNullStrings;

namespace lokqlDx;

public partial class MainWindow : Window
{
    private readonly string[] _args;
    private readonly WpfConsole _console;
    private readonly Size _minWindowSize = new(600, 400);
    private readonly PreferencesManager _preferenceManager = new();
    private readonly WorkspaceManager _workspaceManager;

    private Copilot _copilot = new(string.Empty);
    private InteractiveTableExplorer _explorer;

    private MruList _mruList = MruList.LoadFromArray([]);
    private bool isBusy;

    public MainWindow(
        string[] args
    )
    {
        _args = args.ToArray();
        InitializeComponent();
        _console = new WpfConsole(OutputText);

        _workspaceManager = new WorkspaceManager();
        var settings = _workspaceManager.Settings;
        var loader = new StandardFormatAdaptor(settings, _console);
        var cp = CommandProcessorProvider.GetCommandProcessor();
        _explorer = new InteractiveTableExplorer(_console, loader, settings, cp);
    }

    private async Task RunQuery(string query)
    {
        if (isBusy)
            return;
        isBusy = true;
        Editor.SetBusy(true);
        //start capturing console output from the engine
        _console.PrepareForOutput();
        //run the supplied lines of kusto/commands
        //Note that we need the extra Task.Run here to ensure
        //that the UI thread is not blocked for reports generated by
        //the engine

        await Task.Run(async () => await _explorer.RunInput(query));
        var result = _explorer._prevResult;

        var renderer = new KustoResultRenderer(_explorer.Settings);
        var html = renderer.RenderToHtml(result);

        //annoying we have to do this, but it's the only way to get the webview to render
        await webview.EnsureCoreWebView2Async();
        //generate the html and display it
        try
        {
            webview.NavigateToString(html);
        }
        catch
        {
            _explorer.Warn("Unable to render results in webview");
        }


        FillInDataGrid(result);

        Editor.SetBusy(false);
        isBusy = false;
    }

    private void FillInDataGrid(KustoQueryResult result)
    {
        //ensure that if have not results we clear the data grid
        if (result.RowCount == 0)
        {
            dataGrid.ItemsSource = null;
            return;
        }

        var maxDataGridRows = int.TryParse(VisibleDataGridRows.Text, out var parsed)
            ? parsed
            : 10000;
        var dt = result.ToDataTable(maxDataGridRows);
        dataGrid.ItemsSource = dt.DefaultView;
    }

    /// <summary>
    ///     Called when user presses SHIFT-ENTER in the query editor
    /// </summary>
    private async void OnQueryEditorRunTextBlock(object? sender, QueryEditorRunEventArgs eventArgs)
    {
        await RunQuery(eventArgs.Query);
    }

    private void UpdateUIFromWorkspace()
    {
        Editor.SetText(currentWorkspace.Text);
        var settings = _workspaceManager.Settings;
        var loader = new StandardFormatAdaptor(settings, _console);
        _explorer = new InteractiveTableExplorer(_console, loader, settings,
            CommandProcessorProvider.GetCommandProcessor());
        UpdateFontSize();
        Title = $"LokqlDX - {_workspaceManager.Path.OrWhenBlank("new workspace")}";
    }

    private void RebuildRecentFilesList()
    {
        RecentlyUsed.Items.Clear();


        foreach (var mruItem in _mruList.GetItems())
        {
            var menuitem = new MenuItem
            {
                Header = mruItem.Description,
                DataContext = mruItem
            };
            menuitem.Click += RecentlyUsedFileClicked;
            RecentlyUsed.Items.Add(menuitem);
        }
    }

    private void UpdateMostRecentlyUsed(string path)
    {
        if (path.IsBlank())
            return;
        _mruList.BringToTop(path);
        JumpList.AddToRecentCategory(path);

        RebuildRecentFilesList();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        RegistryOperations.AssociateFileType(true);
        _preferenceManager.Load();
        _mruList = MruList.LoadFromArray(_preferenceManager.Preferences.RecentProjects);
        RebuildRecentFilesList();
        var pathToLoad = _args.Any()
            ? _args[0]
            : string.Empty;
      
        if (Width > 100 && Height > 100 && Left > 0 && Top > 0)
        {
            Width = _preferenceManager.Preferences.WindowWidth < _minWindowSize.Width
                ? _minWindowSize.Width
                : _preferenceManager.Preferences.WindowWidth;
            Height = _preferenceManager.Preferences.WindowHeight < _minWindowSize.Height
                ? _minWindowSize.Height
                : _preferenceManager.Preferences.WindowHeight;
            Left = _preferenceManager.Preferences.WindowLeft;
            Top = _preferenceManager.Preferences.WindowTop;
        }
        await LoadWorkspace(pathToLoad);
        await Navigate("https://github.com/NeilMacMullen/kusto-loco/wiki/LokqlDX");
    }

    private async void RecentlyUsedFileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MruList.MruItem mruItem })
            await LoadWorkspace(mruItem.Path);
    }

    private Workspace currentWorkspace = new Workspace();

    private void SaveWorkspace(string path)
    {
        PreferencesManager.EnsureDefaultFolderExists();
        UpdateCurrentWorkspaceFromUI();
        _workspaceManager.Save(path,currentWorkspace);
        _preferenceManager.Preferences.LastWorkspacePath = _workspaceManager.Path;
        _preferenceManager.Preferences.WindowLeft = Left;
        _preferenceManager.Preferences.WindowTop = Top;
        _preferenceManager.Preferences.WindowWidth = Width;
        _preferenceManager.Preferences.WindowHeight = Height;

        _preferenceManager.Preferences.RecentProjects = _mruList.GetItems().Select(i => i.Path).ToArray();
        _preferenceManager.Save();
        UpdateMostRecentlyUsed(_workspaceManager.Path);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        Save();
    }

    private async Task LoadWorkspace(string path)
    {
        _workspaceManager.Load(path);
        currentWorkspace = _workspaceManager.Workspace;
      
        var startupscript = _preferenceManager.Preferences.StartupScript;
        if (!startupscript.IsBlank())
            await RunQuery(startupscript);
        var wkspcStartup = _workspaceManager.Workspace.StartupScript;
        if (!wkspcStartup.IsBlank())
            await RunQuery(wkspcStartup);
        UpdateMostRecentlyUsed(path);
        UpdateUIFromWorkspace();
    }

    private async void OpenWorkSpace(object sender, RoutedEventArgs e)
    {
        var folder = _workspaceManager.ContainingFolder();
        var dialog = new OpenFileDialog
        {
            InitialDirectory = folder,
            Filter = $"Lokql Workspace ({WorkspaceManager.GlobPattern})|{WorkspaceManager.GlobPattern}",
            FileName = Path.GetFileName(_workspaceManager.Path)
        };

        if (dialog.ShowDialog() == true)
            await LoadWorkspace(dialog.FileName);
    }


    private void SaveWorkspaceEvent(object sender, RoutedEventArgs e)
    {
        Save();
    }

    void UpdateCurrentWorkspaceFromUI()
    {
        currentWorkspace = currentWorkspace with { Text = Editor.GetText() };
    }
    private void Save()
    {
        UpdateCurrentWorkspaceFromUI();
        if (!_workspaceManager.IsDirty(currentWorkspace))
            return;
        if (_workspaceManager.Path.IsBlank())
        {
            SaveAs();
            return;
        }

        SaveWorkspace(_workspaceManager.Path);
    }

    private bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = $"Lokql Workspace ({WorkspaceManager.GlobPattern})|{WorkspaceManager.GlobPattern}",
            FileName = Path.GetFileName(_workspaceManager.Path)
        };
        if (dialog.ShowDialog() == true)
        {
            SaveWorkspace(dialog.FileName);
            UpdateUIFromWorkspace();
            return true;
        }

        return false;
    }

    private void SaveWorkspaceAsEvent(object sender, RoutedEventArgs e)
    {
        SaveAs();
    }

    private async void NewWorkspace(object sender, RoutedEventArgs e)
    {
        //save current
        Save();
      await LoadWorkspace(string.Empty);
    }

    private void IncreaseFontSize(object sender, RoutedEventArgs e)
    {
        _preferenceManager.Preferences.FontSize = Math.Min(40, _preferenceManager.Preferences.FontSize + 1);
        UpdateFontSize();
    }

    private void DecreaseFontSize(object sender, RoutedEventArgs e)
    {
        _preferenceManager.Preferences.FontSize = Math.Max(6, _preferenceManager.Preferences.FontSize - 1);
        UpdateFontSize();
    }

    private void UpdateFontSize()
    {
        Editor.SetFontSize(_preferenceManager.Preferences.FontSize);
        OutputText.FontSize = _preferenceManager.Preferences.FontSize;
        dataGrid.FontSize = _preferenceManager.Preferences.FontSize;
        UserChat.FontSize = _preferenceManager.Preferences.FontSize;
        ChatHistory.FontSize = _preferenceManager.Preferences.FontSize;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // here I suppose the window's menu is named "MainMenu"
        MainMenu.RaiseMenuItemClickOnKeyGesture(e);
    }

    private async Task Navigate(string url)
    {
        await webview.EnsureCoreWebView2Async();
        webview.Source = new Uri(url);
    }

    private async void NavigateToGettingStarted(object sender, RoutedEventArgs e)
    {
        await Navigate("https://github.com/NeilMacMullen/kusto-loco/wiki/LokqlDX");
    }

    private async void NavigateToProjectPage(object sender, RoutedEventArgs e)
    {
        await Navigate("https://github.com/NeilMacMullen/kusto-loco");
    }

    private async void NavigateToKqlIntroductionPage(object sender, RoutedEventArgs e)
    {
        await Navigate(
            "https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/tutorials/learn-common-operators");
    }

    private void EnableJumpList(object sender, RoutedEventArgs e)
    {
        RegistryOperations.AssociateFileType(false);
    }

    private async void SubmitToCopilot(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        if (!_copilot.Initialised)
        {
            _copilot = new Copilot(_explorer.Settings.GetOr("copilot", string.Empty));
            foreach (var table in _explorer.GetCurrentContext().Tables())
            {
                var sb = new StringBuilder();
                sb.AppendLine($"The table named '{table.Name}' has the following columns");
                var cols = table.ColumnNames.Zip(table.Type.Columns)
                    .Select(z => $"  {z.First} is of type {z.Second.Type.Name}")
                    .ToArray();
                foreach (var column in cols) sb.AppendLine(column);
                _copilot.AddSystemInstructions(sb.ToString());
            }
        }

        var userchat = UserChat.Text;
        UserChat.Text = string.Empty;
        const int maxResubmissions = 3;
        for (var i = 0; i < maxResubmissions; i++)
        {
            var response = await _copilot.Issue(userchat);


            var console = new WpfConsole(ChatHistory);

            //now try to extract kql...
            var lines = response.Split('\n');
            var kql = new StringBuilder();
            var getting = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("```kql") || line.StartsWith("```kusto"))
                {
                    kql.Clear();
                    getting = true;
                    continue;
                }

                if (line.StartsWith("```"))
                {
                    getting = false;
                    continue;
                }

                if (getting)
                    kql.AppendLine(line.Trim());
            }

            _copilot.AddResponse(kql.ToString());
            console.PrepareForOutput();
            var options = new List<string> { Copilot.Roles.System, Copilot.Roles.User, Copilot.Roles.Kql };
            if (TerseMode.IsChecked != true)
                options.Add(Copilot.Roles.Assistant);
            _copilot.RenderResponses(console, options.ToArray());

            if (kql.ToString().IsBlank())
                break;
            await RunQuery(kql.ToString());
            var lastResult = _explorer._prevResultIncludingError;

            if (lastResult.Error.IsBlank())
                break;
            userchat = $"That query gave an error: {lastResult.Error}";
        }


        SubmitButton.IsEnabled = true;
    }

    private void ResetCopilot(object sender, RoutedEventArgs e)
    {
        _copilot = new Copilot(string.Empty);
        ChatHistory.Document.Blocks.Clear();
    }

    private void OpenOptionsDialog(object sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesWindow(_preferenceManager.Preferences)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true) _preferenceManager.Save();
    }

    private void OpenWorkspaceOptions(object sender, RoutedEventArgs e)
    {
       UpdateCurrentWorkspaceFromUI();
        var dialog = new WorkspaceOptions(currentWorkspace)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            currentWorkspace=dialog._workspace ;
            Save();
        }
    }
}
