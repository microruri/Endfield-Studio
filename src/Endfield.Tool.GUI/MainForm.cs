using System.Globalization;
using System.Text;
using System.Text.Json;
using Endfield.BlcTool.Core.Blc;
using Endfield.JsonTool.Core.Json;
using Endfield.Tool.GUI.Logging;
using Endfield.Tool.GUI.Services;
using Endfield.Tool.GUI.UI;

namespace Endfield.Tool.GUI;

public sealed class MainForm : Form
{
    private const string ApplicationTitle = "Endfield.Tool.GUI";

    private readonly GuiLogger _logger;
    private readonly SplitContainer _mainSplitContainer;

    private readonly ComboBox _leftTypeSelector;
    private readonly ListBox _leftFileList;

    private readonly TabControl _rightTabControl;
    private readonly TabPage _detailsTab;
    private readonly TabPage _rawTab;
    private readonly TabPage _decodedTab;
    private readonly TabPage _previewTab;
    private readonly TextBox _detailsTextBox;
    private readonly TextBox _rawTextBox;
    private readonly TextBox _decodedTextBox;
    private readonly TextBox _previewTextBox;
    private readonly Label _emptyRightLabel;

    private ToolStripMenuItem _themeSystemMenuItem = null!;
    private ToolStripMenuItem _themeDarkMenuItem = null!;
    private ToolStripMenuItem _themeLightMenuItem = null!;

    private readonly Dictionary<int, ResourceCatalogEntry> _preferredBlcEntriesByType = new();
    private readonly Dictionary<int, string> _blcRawCache = new();
    private readonly Dictionary<int, string> _blcDecodedTextCache = new();
    private readonly Dictionary<int, string> _blcPreviewTextCache = new();
    private readonly Dictionary<string, string> _jsonDataRawCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _jsonDataDecodedTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _jsonDataPreviewTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<ResourceCatalogEntry>> _entriesByType = new();
    private readonly List<Control> _surfacePanels = new();

    private string? _gameRoot;
    private bool _isSelectingGameRoot;
    private bool _isApplyingTheme;
    // Used as a generation token to drop stale async UI load results.
    private int _rightLoadVersion;

    private static readonly (int Type, string Name)[] TypeItems =
    {
        (-1, "JSONData"),
        (0, "Manifest Blobs"),
        (1, "InitAudio"),
        (2, "InitBundle"),
        (3, "InitialExtendData"),
        (4, "BundleManifest"),
        (5, "IFixPatchOut"),
        (6, "AuditStreaming"),
        (7, "AuditDynamicStreaming"),
        (8, "AuditIV"),
        (9, "AuditAudio"),
        (10, "AuditVideo"),
        (11, "Bundle"),
        (12, "Audio"),
        (13, "Video"),
        (14, "IV"),
        (15, "Streaming"),
        (16, "DynamicStreaming"),
        (17, "Lua"),
        (18, "Table"),
        (19, "JsonData"),
        (20, "ExtendData"),
        (101, "AudioChinese"),
        (102, "AudioEnglish"),
        (103, "AudioJapanese"),
        (104, "AudioKorean")
    };

    public MainForm()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

        Text = ApplicationTitle;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1582, 853);
        MinimumSize = new Size(620, 372);

        _logger = new GuiLogger();

        var menuStrip = BuildMenuStrip();

        _mainSplitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Orientation = Orientation.Vertical
        };
        Controls.Add(_mainSplitContainer);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        var leftSurface = CreateSurfacePanel();
        leftSurface.Dock = DockStyle.Fill;
        leftSurface.Padding = new Padding(8);
        _mainSplitContainer.Panel1.Padding = new Padding(10, 10, 5, 10);
        _mainSplitContainer.Panel1.Controls.Add(leftSurface);

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftSurface.Controls.Add(leftLayout);

        var selectorHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 6)
        };
        leftLayout.Controls.Add(selectorHeader, 0, 0);

        var selectorLabel = new Label
        {
            AutoSize = true,
            Text = "Type:",
            Margin = new Padding(0, 7, 8, 0),
            Font = new Font("Segoe UI", 10, FontStyle.Regular)
        };
        selectorHeader.Controls.Add(selectorLabel);

        _leftTypeSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Font = new Font("Segoe UI", 10, FontStyle.Regular)
        };
        _leftTypeSelector.SelectedIndexChanged += LeftTypeSelector_SelectedIndexChanged;
        selectorHeader.Controls.Add(_leftTypeSelector);

        _leftFileList = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        _leftFileList.SelectedIndexChanged += LeftFileList_SelectedIndexChanged;
        leftLayout.Controls.Add(_leftFileList, 0, 1);

        var rightSurface = CreateSurfacePanel();
        rightSurface.Dock = DockStyle.Fill;
        rightSurface.Padding = new Padding(8);
        _mainSplitContainer.Panel2.Padding = new Padding(5, 10, 10, 10);
        _mainSplitContainer.Panel2.Controls.Add(rightSurface);

        _rightTabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 4),
            SizeMode = TabSizeMode.Fixed,
            Visible = false
        };
        rightSurface.Controls.Add(_rightTabControl);

        _detailsTab = new TabPage("Details");
        _rawTab = new TabPage("Raw");
        _decodedTab = new TabPage("Decoded");
        _previewTab = new TabPage("Preview");

        _detailsTextBox = CreateViewerTextBox();
        _rawTextBox = CreateViewerTextBox();
        _decodedTextBox = CreateViewerTextBox();
        _previewTextBox = CreateViewerTextBox();

        _detailsTab.Controls.Add(_detailsTextBox);
        _rawTab.Controls.Add(_rawTextBox);
        _decodedTab.Controls.Add(_decodedTextBox);
        _previewTab.Controls.Add(_previewTextBox);

        _emptyRightLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Select a file to view details.",
            Font = new Font("Segoe UI", 10, FontStyle.Regular)
        };
        rightSurface.Controls.Add(_emptyRightLabel);

        InitializeLeftTypeSelector();

        ThemeManager.ApplyAppTheme(GuiTheme.System);
        ApplyThemeToControls(ThemeManager.CurrentTheme);
        UpdateThemeMenuChecks(ThemeManager.CurrentTheme);

        Shown += (_, _) => BeginInvoke(() =>
        {
            ApplyInitialSplitterDistance();
            _ = SelectAndValidateGameRootAsync(exitOnCancel: true);
        });
    }

    private static TextBox CreateViewerTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10)
        };
    }

    private void InitializeLeftTypeSelector()
    {
        _leftTypeSelector.BeginUpdate();
        _leftTypeSelector.Items.Clear();
        foreach (var item in TypeItems)
            _leftTypeSelector.Items.Add(new TypeSelectorItem(item.Type, item.Name));
        _leftTypeSelector.EndUpdate();

        if (_leftTypeSelector.Items.Count > 0)
            _leftTypeSelector.SelectedIndex = 0;
    }

    private void ApplyInitialSplitterDistance()
    {
        const int preferredLeftMin = 200;
        const int preferredRightMin = 400;
        const int preferredDistance = 603;

        var width = _mainSplitContainer.ClientSize.Width;
        if (width <= 0)
            return;

        var rightMin = Math.Min(preferredRightMin, Math.Max(25, width - preferredLeftMin));
        var leftMin = Math.Min(preferredLeftMin, Math.Max(25, width - rightMin));

        _mainSplitContainer.Panel1MinSize = leftMin;
        _mainSplitContainer.Panel2MinSize = rightMin;

        var minDistance = leftMin;
        var maxDistance = width - rightMin;
        if (maxDistance < minDistance)
            return;

        _mainSplitContainer.SplitterDistance = Math.Max(minDistance, Math.Min(preferredDistance, maxDistance));
    }

    private MenuStrip BuildMenuStrip()
    {
        var menuStrip = new MenuStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(20, 20)
        };

        var fileMenu = new ToolStripMenuItem("File");
        var gameDirectoryMenuItem = new ToolStripMenuItem("Game Directory...");
        gameDirectoryMenuItem.Click += async (_, _) => await SelectAndValidateGameRootAsync(exitOnCancel: false);
        fileMenu.DropDownItems.Add(gameDirectoryMenuItem);

        var optionsMenu = new ToolStripMenuItem("Options");
        var themeMenu = new ToolStripMenuItem("Theme");

        _themeSystemMenuItem = new ToolStripMenuItem("System") { Tag = GuiTheme.System };
        _themeDarkMenuItem = new ToolStripMenuItem("Dark") { Tag = GuiTheme.Dark };
        _themeLightMenuItem = new ToolStripMenuItem("Light") { Tag = GuiTheme.Light };

        _themeSystemMenuItem.Click += ThemeMenuItem_Click;
        _themeDarkMenuItem.Click += ThemeMenuItem_Click;
        _themeLightMenuItem.Click += ThemeMenuItem_Click;

        themeMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _themeSystemMenuItem,
            _themeDarkMenuItem,
            _themeLightMenuItem
        });
        optionsMenu.DropDownItems.Add(themeMenu);

        var exportMenu = new ToolStripMenuItem("Export");
        var exportPlaceholder = new ToolStripMenuItem("Coming soon") { Enabled = false };
        exportMenu.DropDownItems.Add(exportPlaceholder);

        var aboutMenu = new ToolStripMenuItem("About");
        aboutMenu.Click += (_, _) => ShowAboutDialog();

        menuStrip.Items.AddRange(new ToolStripItem[]
        {
            fileMenu,
            optionsMenu,
            exportMenu,
            aboutMenu
        });

        return menuStrip;
    }

    private async Task SelectAndValidateGameRootAsync(bool exitOnCancel)
    {
        if (_isSelectingGameRoot)
            return;

        _isSelectingGameRoot = true;
        _logger.Info("Opening game root folder picker.");

        try
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select Endfield game root directory",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = string.IsNullOrWhiteSpace(_gameRoot) ? string.Empty : _gameRoot
            };

            var dialogResult = folderDialog.ShowDialog(this);
            if (dialogResult != DialogResult.OK)
            {
                _logger.Warn("Folder selection canceled.");
                if (exitOnCancel)
                {
                    _logger.Warn("Application will exit because initial setup was canceled.");
                    Close();
                }

                return;
            }

            var selectedPath = folderDialog.SelectedPath;
            _logger.Info($"Selected path: {selectedPath}");
            UseWaitCursor = true;

            var validation = await Task.Run(() =>
            {
                var isValid = GameRootValidator.TryValidate(selectedPath, out var validationResult, out var error);
                return (isValid, validationResult, error);
            });

            if (!validation.isValid)
            {
                _logger.Error("Unable to read game resources.");
                _logger.Error(validation.error);
                MessageBox.Show(this, "Unable to read game resources", ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _gameRoot = validation.validationResult!.GameRoot;

            var catalogLoad = await Task.Run(() =>
            {
                var loaded = GameResourceCatalogLoader.TryLoad(_gameRoot, out var entries, out var loadError);
                return (loaded, entries, loadError);
            });

            if (!catalogLoad.loaded)
            {
                _logger.Error("Failed to load resource catalog.");
                _logger.Error(catalogLoad.loadError);
                MessageBox.Show(this, "Unable to load resource catalog", ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PopulateEntries(catalogLoad.entries);
            RebuildLeftFileList();
            HideRightViews();

            _logger.Info("Game root configured.");
            _logger.Info($"Validation succeeded with index: {validation.validationResult.ValidatedIndexPath}");
            _logger.Info($"Loaded resources: {catalogLoad.entries.Count}");
        }
        finally
        {
            UseWaitCursor = false;
            _isSelectingGameRoot = false;
        }
    }

    private void PopulateEntries(List<ResourceCatalogEntry> entries)
    {
        _entriesByType.Clear();
        _preferredBlcEntriesByType.Clear();
        _blcRawCache.Clear();
        _blcDecodedTextCache.Clear();
        _blcPreviewTextCache.Clear();
        _jsonDataRawCache.Clear();
        _jsonDataDecodedTextCache.Clear();
        _jsonDataPreviewTextCache.Clear();

        foreach (var typeItem in TypeItems.Where(x => x.Type > 0))
        {
            var list = entries
                .Where(x => x.Type == typeItem.Type)
                .OrderBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _entriesByType[typeItem.Type] = list;
        }

        foreach (var pair in BuildPreferredBlcEntriesByType(entries))
            _preferredBlcEntriesByType[pair.Key] = pair.Value;
    }

    private static Dictionary<int, ResourceCatalogEntry> BuildPreferredBlcEntriesByType(List<ResourceCatalogEntry> entries)
    {
        var result = new Dictionary<int, ResourceCatalogEntry>();

        foreach (var group in entries
                     .Where(x => x.VirtualPath.EndsWith(".blc", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(x => x.Type))
        {
            var preferred = group
                .OrderBy(x => GetIndexPriority(x.IndexFile))
                .ThenBy(x => GetSourcePriority(x.SourceFolder))
                .ThenBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .First();

            result[group.Key] = preferred;
        }

        return result;
    }

    private static int GetIndexPriority(string indexFile)
    {
        return indexFile.Equals(GameCatalogLayout.InitialIndexFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static int GetSourcePriority(string sourceFolder)
    {
        return sourceFolder.Equals(GameCatalogLayout.PersistentFolderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private void LeftTypeSelector_SelectedIndexChanged(object? sender, EventArgs e)
    {
        RebuildLeftFileList();
        HideRightViews();
    }

    private void RebuildLeftFileList()
    {
        _leftFileList.BeginUpdate();
        _leftFileList.Items.Clear();

        if (_leftTypeSelector.SelectedItem is not TypeSelectorItem selectedType)
        {
            _leftFileList.EndUpdate();
            return;
        }

        if (selectedType.TypeId == -1)
        {
            var existingJsonDataPaths = GameDataPathResolver.GetExistingJsonDataRelativePaths(_gameRoot);
            if (existingJsonDataPaths.Count == 0)
            {
                _leftFileList.Items.Add(new EmptyListItem("No files."));
            }
            else
            {
                foreach (var relPath in existingJsonDataPaths)
                    _leftFileList.Items.Add(new JsonDataFileListItem(relPath));
            }

            _leftFileList.EndUpdate();
            return;
        }

        if (selectedType.TypeId == 0)
        {
            foreach (var item in TypeItems.Where(x => x.Type > 0))
                _leftFileList.Items.Add(new BlcJsonListItem(item.Type, item.Name));

            _leftFileList.EndUpdate();
            return;
        }

        if (!_entriesByType.TryGetValue(selectedType.TypeId, out var files) || files.Count == 0)
        {
            _leftFileList.Items.Add(new EmptyListItem("No files."));
            _leftFileList.EndUpdate();
            return;
        }

        foreach (var file in files)
            _leftFileList.Items.Add(new ResourceListItem(file));

        _leftFileList.EndUpdate();
    }

    private void LeftFileList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_leftFileList.SelectedItem is JsonDataFileListItem jsonDataItem)
        {
            _ = ShowJsonDataViewsAsync(jsonDataItem.RelativePath);
            return;
        }

        if (_leftFileList.SelectedItem is BlcJsonListItem blcItem)
        {
            _ = ShowBlcViewsAsync(blcItem.TypeId, blcItem.TypeName);
            return;
        }

        if (_leftFileList.SelectedItem is ResourceListItem resourceItem)
        {
            ShowMetadataOnly(resourceItem.Entry);
            return;
        }

        HideRightViews();
    }

    private async Task ShowBlcViewsAsync(int typeId, string typeName)
    {
        var requestVersion = ++_rightLoadVersion;

        ShowRightTabs(showRaw: true, showDecoded: true, showPreview: true);
        _detailsTextBox.Text = "Loading details...";
        _rawTextBox.Text = "Loading raw bytes...";
        _decodedTextBox.Text = "Loading decoded text...";
        _previewTextBox.Text = "Loading preview text...";

        var filename = $"{typeName}.json";
        var result = await Task.Run(() =>
        {
            var details = BuildBlcDetails(typeId, filename, out var entry, out var absolutePath);
            var rawText = BuildBlcRawText(typeId, entry, absolutePath);
            var decodedText = BuildBlcDecodedText(typeId, entry, absolutePath);
            var previewText = BuildBlcPreviewText(typeId, entry, absolutePath);
            return (details, rawText, decodedText, previewText);
        });

        if (requestVersion != _rightLoadVersion)
            return;

        _detailsTextBox.Text = result.details;
        _rawTextBox.Text = result.rawText;
        _decodedTextBox.Text = result.decodedText;
        _previewTextBox.Text = result.previewText;
    }

    private async Task ShowJsonDataViewsAsync(string relativePath)
    {
        var requestVersion = ++_rightLoadVersion;

        ShowRightTabs(showRaw: true, showDecoded: true, showPreview: true);
        _detailsTextBox.Text = "Loading details...";
        _rawTextBox.Text = "Loading raw bytes...";
        _decodedTextBox.Text = "Loading decoded text...";
        _previewTextBox.Text = "Loading preview text...";

        var result = await Task.Run(() =>
        {
            var details = BuildJsonDataDetails(relativePath, out var absolutePath);
            var rawText = BuildJsonDataRawText(absolutePath);
            var decodedText = BuildJsonDataDecodedText(relativePath, absolutePath);
            var previewText = BuildJsonDataPreviewText(relativePath, absolutePath);
            return (details, rawText, decodedText, previewText);
        });

        if (requestVersion != _rightLoadVersion)
            return;

        _detailsTextBox.Text = result.details;
        _rawTextBox.Text = result.rawText;
        _decodedTextBox.Text = result.decodedText;
        _previewTextBox.Text = result.previewText;
    }

    private void ShowMetadataOnly(ResourceCatalogEntry entry)
    {
        ShowRightTabs(showRaw: false, showDecoded: false, showPreview: false);

        var abs = GameDataPathResolver.TryResolveResourceAbsolutePath(_gameRoot, entry, out var resolvedPath)
            ? resolvedPath
            : GameDataPathResolver.NotFoundPath;
        _detailsTextBox.Text =
            $"Type: {entry.Type}{Environment.NewLine}" +
            $"Index: {entry.IndexFile}{Environment.NewLine}" +
            $"Source: {entry.SourceFolder}{Environment.NewLine}" +
            $"Virtual Path: {entry.VirtualPath}{Environment.NewLine}" +
            $"Size: {entry.Size:N0}{Environment.NewLine}" +
            $"Absolute Path: {abs}";
    }

    private void ShowRightTabs(bool showRaw, bool showDecoded, bool showPreview)
    {
        _rightTabControl.TabPages.Clear();

        _rightTabControl.TabPages.Add(_detailsTab);
        if (showRaw)
            _rightTabControl.TabPages.Add(_rawTab);
        if (showDecoded)
            _rightTabControl.TabPages.Add(_decodedTab);
        if (showPreview)
            _rightTabControl.TabPages.Add(_previewTab);

        _rightTabControl.SelectedTab = _detailsTab;
        _rightTabControl.Visible = true;
        _emptyRightLabel.Visible = false;

        ApplyThemeToControls(ThemeManager.CurrentTheme);
    }

    private void HideRightViews()
    {
        _rightLoadVersion++;
        _rightTabControl.TabPages.Clear();
        _rightTabControl.Visible = false;
        _emptyRightLabel.Visible = true;
    }

    private string BuildBlcDetails(int typeId, string filename, out ResourceCatalogEntry? entry, out string absolutePath)
    {
        entry = null;
        absolutePath = GameDataPathResolver.NotFoundPath;

        if (!_preferredBlcEntriesByType.TryGetValue(typeId, out var blcEntry))
        {
            return
                $"File: {filename}{Environment.NewLine}" +
                "Type: N/A" + Environment.NewLine +
                "Index: N/A" + Environment.NewLine +
                "Source: N/A" + Environment.NewLine +
                "Virtual Path: N/A" + Environment.NewLine +
                $"Absolute Path: {GameDataPathResolver.NotFoundPath}" + Environment.NewLine +
                "Status: No .blc entry is available for this type.";
        }

        entry = blcEntry;
        if (!GameDataPathResolver.TryResolveResourceAbsolutePath(_gameRoot, blcEntry, out absolutePath))
            absolutePath = GameDataPathResolver.NotFoundPath;

        return
            $"File: {filename}{Environment.NewLine}" +
            $"Type: {blcEntry.Type}{Environment.NewLine}" +
            $"Index: {blcEntry.IndexFile}{Environment.NewLine}" +
            $"Source: {blcEntry.SourceFolder}{Environment.NewLine}" +
            $"Virtual Path: {blcEntry.VirtualPath}{Environment.NewLine}" +
            $"Size: {blcEntry.Size:N0}{Environment.NewLine}" +
            $"Absolute Path: {absolutePath}";
    }

    private string BuildJsonDataDetails(string relativePath, out string absolutePath)
    {
        if (!GameDataPathResolver.TryResolveJsonDataAbsolutePath(_gameRoot, relativePath, out absolutePath))
            absolutePath = GameDataPathResolver.NotFoundPath;

        var sourceInfo = GameDataPathResolver.DetectJsonDataSource(absolutePath);

        return
            $"File: {Path.GetFileName(relativePath)}{Environment.NewLine}" +
            "Type: JSONData" + Environment.NewLine +
            $"Source: {sourceInfo}{Environment.NewLine}" +
            $"Relative Path: {relativePath}{Environment.NewLine}" +
            $"Absolute Path: {absolutePath}";
    }

    private string BuildJsonDataRawText(string absolutePath)
    {
        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced file is missing.";

        if (_jsonDataRawCache.TryGetValue(absolutePath, out var cached))
            return cached;

        try
        {
            var text = HexDumpFormatter.Format(File.ReadAllBytes(absolutePath));
            _jsonDataRawCache[absolutePath] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Failed to read raw bytes: {ex.Message}";
        }
    }

    private string BuildJsonDataDecodedText(string relativePath, string absolutePath)
    {
        var cacheKey = $"{relativePath}::{absolutePath}";
        if (_jsonDataDecodedTextCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced file is missing.";

        try
        {
            var encrypted = File.ReadAllBytes(absolutePath);

            var useDecoded = TryDecryptJsonData(encrypted, out var decodedBytes);
            var text = HexDumpFormatter.Format(decodedBytes);
            if (!useDecoded)
                text = "First-stage decrypt failed. Showing raw bytes.\r\n\r\n" + text;

            _jsonDataDecodedTextCache[cacheKey] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Failed to decode file: {ex.Message}";
        }
    }

    private string BuildJsonDataPreviewText(string relativePath, string absolutePath)
    {
        var cacheKey = $"{relativePath}::{absolutePath}";
        if (_jsonDataPreviewTextCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced file is missing.";

        try
        {
            var encrypted = File.ReadAllBytes(absolutePath);

            var useDecoded = TryDecryptJsonData(encrypted, out var decodedBytes);
            if (useDecoded && JsonDecryptor.TryDecodeUtf8Json(decodedBytes, out var jsonText))
            {
                _jsonDataPreviewTextCache[cacheKey] = jsonText;
                return jsonText;
            }

            var text = BuildBestEffortText(decodedBytes);
            if (!useDecoded)
                text = "First-stage decrypt failed. Showing best-effort preview from raw bytes.\r\n\r\n" + text;

            _jsonDataPreviewTextCache[cacheKey] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Failed to build preview text: {ex.Message}";
        }
    }

    private static bool TryDecryptJsonData(byte[] encrypted, out byte[] decoded)
    {
        try
        {
            decoded = JsonDecryptor.DecryptFirstStage(encrypted);
            return true;
        }
        catch
        {
            decoded = encrypted;
            return false;
        }
    }

    private string BuildBlcRawText(int typeId, ResourceCatalogEntry? entry, string absolutePath)
    {
        if (entry == null)
            return "No .blc entry is available for this type.";

        if (_blcRawCache.TryGetValue(typeId, out var cached))
            return cached;

        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced .blc file is missing.";

        try
        {
            var bytes = File.ReadAllBytes(absolutePath);
            var text = HexDumpFormatter.Format(bytes);
            _blcRawCache[typeId] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Failed to read raw bytes: {ex.Message}";
        }
    }

    private string BuildBlcDecodedText(int typeId, ResourceCatalogEntry? entry, string absolutePath)
    {
        if (entry == null)
            return "No .blc entry is available for this type.";

        if (_blcDecodedTextCache.TryGetValue(typeId, out var cached))
            return cached;

        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced .blc file is missing.";

        try
        {
            var encrypted = File.ReadAllBytes(absolutePath);
            var decrypted = BlcDecryptor.Decrypt(encrypted);
            var text = HexDumpFormatter.Format(decrypted);
            _blcDecodedTextCache[typeId] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Failed to decode .blc: {ex.Message}";
        }
    }

    private string BuildBlcPreviewText(int typeId, ResourceCatalogEntry? entry, string absolutePath)
    {
        if (entry == null)
            return "No .blc entry is available for this type.";

        if (_blcPreviewTextCache.TryGetValue(typeId, out var cached))
            return cached;

        if (absolutePath == GameDataPathResolver.NotFoundPath)
            return "Referenced .blc file is missing.";

        try
        {
            var encrypted = File.ReadAllBytes(absolutePath);

            try
            {
                var parsed = BlcDecoder.Decode(encrypted);
                var text = JsonSerializer.Serialize(parsed, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                _blcPreviewTextCache[typeId] = text;
                return text;
            }
            catch
            {
                var decrypted = BlcDecryptor.Decrypt(encrypted);
                var text = BuildBestEffortText(decrypted);
                _blcPreviewTextCache[typeId] = text;
                return text;
            }
        }
        catch (Exception ex)
        {
            return $"Failed to build preview text: {ex.Message}";
        }
    }

    private static string BuildBestEffortText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var controlCount = text.Count(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t');
        if (text.Length == 0)
            return "<empty>";

        var ratio = controlCount / (double)text.Length;
        if (ratio > 0.1)
            return HexDumpFormatter.Format(bytes);

        return text;
    }

    private Panel CreateSurfacePanel()
    {
        var panel = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(8)
        };

        _surfacePanels.Add(panel);
        return panel;
    }

    private void ThemeMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not GuiTheme theme)
            return;

        if (_isApplyingTheme)
            return;

        _isApplyingTheme = true;
        try
        {
            ThemeManager.ApplyAppTheme(theme);
            ApplyThemeToControls(theme);
            UpdateThemeMenuChecks(theme);
            _logger.Info($"Theme changed to: {theme}");
        }
        finally
        {
            _isApplyingTheme = false;
        }
    }

    private void UpdateThemeMenuChecks(GuiTheme selectedTheme)
    {
        _themeSystemMenuItem.Checked = selectedTheme == GuiTheme.System;
        _themeDarkMenuItem.Checked = selectedTheme == GuiTheme.Dark;
        _themeLightMenuItem.Checked = selectedTheme == GuiTheme.Light;
    }

    private void ApplyThemeToControls(GuiTheme theme)
    {
        var palette = ThemeManager.GetPalette(theme);

        BackColor = palette.FormBack;
        ForeColor = palette.PrimaryText;

        foreach (var panel in _surfacePanels)
        {
            panel.BackColor = palette.Surface;
            panel.ForeColor = palette.PrimaryText;
        }

        _leftTypeSelector.BackColor = palette.SurfaceAlt;
        _leftTypeSelector.ForeColor = palette.PrimaryText;
        _leftFileList.BackColor = palette.SurfaceAlt;
        _leftFileList.ForeColor = palette.PrimaryText;

        _rightTabControl.BackColor = palette.Surface;
        _rightTabControl.ForeColor = palette.PrimaryText;
        foreach (TabPage tab in _rightTabControl.TabPages)
        {
            tab.BackColor = palette.Surface;
            tab.ForeColor = palette.PrimaryText;
        }

        _detailsTextBox.BackColor = palette.SurfaceAlt;
        _detailsTextBox.ForeColor = palette.PrimaryText;
        _rawTextBox.BackColor = palette.SurfaceAlt;
        _rawTextBox.ForeColor = palette.PrimaryText;
        _decodedTextBox.BackColor = palette.SurfaceAlt;
        _decodedTextBox.ForeColor = palette.PrimaryText;
        _previewTextBox.BackColor = palette.SurfaceAlt;
        _previewTextBox.ForeColor = palette.PrimaryText;

        _emptyRightLabel.BackColor = palette.Surface;
        _emptyRightLabel.ForeColor = palette.SecondaryText;
    }

    private void ShowAboutDialog()
    {
        var version = Application.ProductVersion;
        var message =
            $"Endfield.Tool.GUI{Environment.NewLine}" +
            $"Version: {version}{Environment.NewLine}{Environment.NewLine}" +
            "A desktop tool for opening, browsing, previewing, and exporting Endfield game resources.";

        MessageBox.Show(this, message, $"About {ApplicationTitle}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record TypeSelectorItem(int TypeId, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record BlcJsonListItem(int TypeId, string TypeName)
    {
        public override string ToString()
        {
            return $"{TypeName}.json";
        }
    }

    private sealed record ResourceListItem(ResourceCatalogEntry Entry)
    {
        public override string ToString()
        {
            return Entry.VirtualPath;
        }
    }

    private sealed record EmptyListItem(string Text)
    {
        public override string ToString()
        {
            return Text;
        }
    }

    private sealed record JsonDataFileListItem(string RelativePath)
    {
        public override string ToString()
        {
            return RelativePath;
        }
    }
}
