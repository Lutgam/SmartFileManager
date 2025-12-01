using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Input; 
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json; 

namespace App;

// --- 資料模型 ---

public class FileTag : IEquatable<FileTag>
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#888888";
    public string Emoji { get; set; } = "🏷️";

    public bool Equals(FileTag? other)
    {
        if (other == null) return false;
        return this.Name == other.Name && this.Color == other.Color;
    }
}

public class FileMetadata
{
    public string FullPath { get; set; } = "";
    public List<FileTag> Tags { get; set; } = new List<FileTag>();
}

public class FileItem 
{ 
    public string Name { get; set; } = ""; 
    public string FullPath { get; set; } = ""; 
    public string Icon { get; set; } = "📄";
    public string DisplaySize { get; set; } = "";
    public string DisplayDate { get; set; } = "";
    
    public ObservableCollection<FileTag> Tags { get; set; } = new ObservableCollection<FileTag>();
    public Line? ConnectionLine { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public partial class MainWindow : Window
{
    private ObservableCollection<FileItem> _currentFiles = new ObservableCollection<FileItem>();
    private ObservableCollection<FileTag> _availableTags = new ObservableCollection<FileTag>();
    
    private string _currentPath = "";
    private string _selectedColor = "#3498db"; 
    private string _dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartFileManager_Tags.json");

    private bool _isDraggingNode = false;
    private bool _isPanning = false;
    private Panel? _draggedNode = null;
    private object? _draggedItem = null;
    private Point _lastMousePos;
    private Point _dragStartPoint; 
    private Panel? _centerNodePanel = null;
    
    private Panel? _selectedNode = null;
    private object? _selectedItem = null;
    private bool _isLinkMode = false;

    private Dictionary<string, Panel> _activeTagNodes = new Dictionary<string, Panel>();

    // 畫布常數
    private const double CANVAS_CENTER_X = 2500;
    private const double CANVAS_CENTER_Y = 2500;
    private const double CANVAS_WIDTH = 5000;
    private const double CANVAS_HEIGHT = 5000;
    
    private const double NODE_PANEL_WIDTH = 160;
    private const double CENTER_NODE_SIZE = 60;
    private const double FILE_NODE_SIZE = 40;   
    private const double TAG_NODE_SIZE = 50;    

    private ScaleTransform _graphScale = new ScaleTransform(1, 1);
    private TranslateTransform _graphTranslate = new TranslateTransform(0, 0);

    public MainWindow()
    {
        InitializeComponent();
        
        var container = this.FindControl<Panel>("GraphContainer");
        if (container != null)
        {
            container.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            var group = new TransformGroup();
            group.Children.Add(_graphScale);
            group.Children.Add(_graphTranslate);
            container.RenderTransform = group;
        }

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            _dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "SmartFileManager_Tags.json");
        }

        _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        Opened += (s, e) => {
            InitDefaultTags();
            LoadFiles(_currentPath);
            InitColorPicker();
            
            // [修復] 使用 DispatcherPriority.Loaded 確保 Layout 完成
            Dispatcher.UIThread.Post(async () => {
                await Task.Delay(500); 
                ForceResetView(); 
            }, DispatcherPriority.Loaded);
        };
        
        // 視窗大小改變時自動重置，確保不會偏移
        SizeChanged += (s, e) => { 
             // 如果畫布偏移量還是 0 (代表還沒操作過)，才自動重置
            if(_currentFiles.Count > 0 && _graphTranslate.X == 0 && _graphTranslate.Y == 0) 
            {
                ForceResetView();
            }
        };
    }

    private void SaveTagsToDb()
    {
        try
        {
            var dataToSave = _currentFiles.Where(f => f.Tags.Count > 0)
                                          .Select(f => new FileMetadata { FullPath = f.FullPath, Tags = f.Tags.ToList() })
                                          .ToList();
            string json = JsonSerializer.Serialize(dataToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dbPath, json);
        }
        catch (Exception ex) { Console.WriteLine($"[DB Error] {ex.Message}"); }
    }

    private void LoadTagsFromDb()
    {
        try
        {
            if (!File.Exists(_dbPath)) return;
            string json = File.ReadAllText(_dbPath);
            var savedData = JsonSerializer.Deserialize<List<FileMetadata>>(json);
            if (savedData != null)
            {
                foreach (var data in savedData)
                {
                    var targetFile = _currentFiles.FirstOrDefault(f => f.FullPath == data.FullPath);
                    if (targetFile != null)
                    {
                        targetFile.Tags.Clear();
                        foreach (var t in data.Tags) targetFile.Tags.Add(t);
                    }
                }
                SyncTagsFromFiles();
            }
        }
        catch { }
    }

    private void SyncTagsFromFiles()
    {
        var usedTags = _currentFiles.SelectMany(f => f.Tags).Distinct().ToList();
        foreach(var tag in usedTags)
        {
            if (!_availableTags.Any(t => t.Name == tag.Name))
            {
                _availableTags.Add(tag);
            }
        }
        RefreshTagLists();
    }

    private void InitDefaultTags()
    {
        if (_availableTags.Count == 0)
        {
            _availableTags.Add(new FileTag { Name = "待辦", Color = "#e74c3c", Emoji = "🔴" });
            _availableTags.Add(new FileTag { Name = "學校", Color = "#3498db", Emoji = "🔵" });
            _availableTags.Add(new FileTag { Name = "完成", Color = "#2ecc71", Emoji = "🟢" });
        }
        RefreshTagLists();
    }

    private void RefreshTagLists()
    {
        var quickTagList = this.FindControl<ItemsControl>("QuickTagList");
        var miniTagList = this.FindControl<ItemsControl>("MiniTagList");
        
        if (quickTagList != null) { quickTagList.ItemsSource = null; quickTagList.ItemsSource = _availableTags; }
        if (miniTagList != null) { miniTagList.ItemsSource = null; miniTagList.ItemsSource = _availableTags; }
    }

    private void LoadFiles(string path)
    {
        try
        {
            _currentFiles.Clear();
            var dir = new DirectoryInfo(path);
            if (dir.Exists)
            {
                var pathText = this.FindControl<TextBlock>("CurrentPathText");
                if (pathText != null) pathText.Text = dir.Name;

                var files = dir.GetFiles()
                               .Where(f => !f.Name.StartsWith(".")) 
                               .OrderByDescending(f => f.LastWriteTime) 
                               .Take(40) 
                               .Select(f => new FileItem 
                               { 
                                   Name = f.Name, 
                                   FullPath = f.FullName,
                                   Icon = GetIcon(System.IO.Path.GetExtension(f.FullName)),
                                   DisplaySize = FormatSize(f.Length),
                                   DisplayDate = f.LastWriteTime.ToString("MM/dd")
                               });

                foreach(var f in files) _currentFiles.Add(f);
            }

            if (_currentFiles.Count == 0) GenerateFakeData();

            var listBox = this.FindControl<ListBox>("FileListBox");
            if(listBox != null) listBox.ItemsSource = _currentFiles;
            
            LoadTagsFromDb();
            _activeTagNodes.Clear();
            DrawGraph(); 
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    private void GenerateFakeData()
    {
        _currentFiles.Add(new FileItem { Name = "專題報告.pdf", Icon = "📕", DisplaySize="2.5 MB", DisplayDate="12/01", FullPath="/fake/path/doc.pdf" });
        _currentFiles.Add(new FileItem { Name = "Demo截圖.png", Icon = "🖼️", DisplaySize="1.2 MB", DisplayDate="12/01", FullPath="/fake/path/img.png" });
        _currentFiles.Add(new FileItem { Name = "程式碼.cs", Icon = "📝", DisplaySize="4 KB", DisplayDate="11/30", FullPath="/fake/path/code.cs" });
        _currentFiles.Add(new FileItem { Name = "預算表.xlsx", Icon = "📄", DisplaySize="15 KB", DisplayDate="11/28", FullPath="/fake/path/sheet.xlsx" });
        _currentFiles.Add(new FileItem { Name = "貓咪.jpg", Icon = "🖼️", DisplaySize="3.8 MB", DisplayDate="11/25", FullPath="/fake/path/cat.jpg" });
    }

    private void QuickTag_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        string? tagName = btn?.Tag?.ToString();
        var tag = _availableTags.FirstOrDefault(t => t.Name == tagName);
        if (tag != null) ApplyTagToSelected(tag);
    }

    private void ApplyTagToSelected(FileTag tagTemplate)
    {
        var listBox = this.FindControl<ListBox>("FileListBox");
        if (listBox?.SelectedItem is FileItem selectedFile)
        {
            AddTagToFile(selectedFile, tagTemplate);
        }
    }

    private void AddTagToFile(FileItem file, FileTag tagTemplate)
    {
        var newTag = new FileTag { Name = tagTemplate.Name, Color = tagTemplate.Color, Emoji = tagTemplate.Emoji };
        var existing = file.Tags.FirstOrDefault(t => t.Name == newTag.Name);
        if (existing != null) file.Tags.Remove(existing); 
        else file.Tags.Add(newTag);
        
        SaveTagsToDb(); 
        RefreshTagDashboard(); 
        
        var canvas = this.FindControl<Canvas>("GraphCanvas"); 
        if(canvas != null && _activeTagNodes.ContainsKey(newTag.Name)) RefreshLines(canvas);
    }

    private void InitColorPicker()
    {
        var panel = this.FindControl<WrapPanel>("ColorPickerPanel");
        if (panel == null) return;
        panel.Children.Clear();
        string[] colors = { "#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#1abc9c", "#3498db", "#9b59b6", "#34495e", "#95a5a6", "#ecf0f1" };
        foreach (var color in colors)
        {
            var btn = new Button
            {
                Width = 30, Height = 30, Background = Brush.Parse(color), Margin = new Thickness(4), CornerRadius = new CornerRadius(15)
            };
            btn.Click += (s, e) => {
                _selectedColor = color;
                foreach(var b in panel.Children.OfType<Button>()) b.BorderThickness = new Thickness(0);
                btn.BorderThickness = new Thickness(2);
                btn.BorderBrush = Brushes.White;
            };
            panel.Children.Add(btn);
        }
    }

    private void ConfirmAddTag(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("TagNameInput");
        if (input != null)
        {
            string tagName = input.Text ?? "";
            if (!string.IsNullOrWhiteSpace(tagName))
            {
                var newGlobalTag = new FileTag { Name = tagName, Color = _selectedColor, Emoji = "🔖" };
                
                if (!_availableTags.Any(t => t.Name == tagName))
                {
                    _availableTags.Add(newGlobalTag);
                    RefreshTagLists(); // [修復] 強制刷新列表，確保新標籤出現
                }
                
                ApplyTagToSelected(newGlobalTag);
                input.Text = "";
                CloseTagDialog(null, null);
            }
        }
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        var tabControl = sender as TabControl;
        if (e.Source != tabControl) return;
        
        if (tabControl?.SelectedIndex == 1) ShowTagDashboard();
        if (tabControl?.SelectedIndex == 2) 
        {
            DrawGraph();
            if (_graphTranslate.X == 0 && _graphTranslate.Y == 0) ForceResetView();
        }
    }

    private void ShowTagDashboard()
    {
        var dashboard = this.FindControl<Grid>("TagDashboardView");
        var detail = this.FindControl<Grid>("TagDetailView");
        if(dashboard != null) dashboard.IsVisible = true;
        if(detail != null) detail.IsVisible = false;
        ClearDetailPreview();
        RefreshTagDashboard();
    }

    private void RefreshTagDashboard()
    {
        var panel = this.FindControl<WrapPanel>("TagDashboardPanel");
        if (panel == null) return;
        panel.Children.Clear();

        var allTags = _currentFiles.SelectMany(f => f.Tags)
                                   .GroupBy(t => t.Name)
                                   .Select(g => new { Name = g.Key, Count = g.Count(), Color = g.First().Color })
                                   .ToList();

        if (allTags.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "目前沒有標籤", Foreground = Brushes.Gray, Margin = new Thickness(10) });
            return;
        }

        foreach (var tag in allTags)
        {
            var btn = new Button
            {
                Background = Brush.Parse("#252526"), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(10), Width = 200, Height = 100, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = tag.Name, FontWeight = FontWeight.Bold, Foreground = Brush.Parse(tag.Color), FontSize = 18 });
            stack.Children.Add(new TextBlock { Text = $"{tag.Count} 個檔案", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
            
            btn.Content = stack;
            btn.Click += (s, e) => ShowTagDetail(tag.Name);
            panel.Children.Add(btn);
        }
    }

    private void ShowTagDetail(string tagName)
    {
        var dashboard = this.FindControl<Grid>("TagDashboardView");
        var detail = this.FindControl<Grid>("TagDetailView");
        var title = this.FindControl<TextBlock>("TagDetailTitle");
        var list = this.FindControl<ListBox>("TagDetailListBox");

        if(dashboard != null) dashboard.IsVisible = false;
        if(detail != null) detail.IsVisible = true;
        if(title != null) title.Text = $"標籤：{tagName}";

        ClearDetailPreview();

        var filteredFiles = _currentFiles.Where(f => f.Tags.Any(t => t.Name == tagName)).ToList();
        if(list != null) list.ItemsSource = filteredFiles;
    }

    private void ClearDetailPreview()
    {
        var img = this.FindControl<Image>("DetailPreviewImage");
        var txt = this.FindControl<TextBlock>("DetailPreviewText");
        var scroll = this.FindControl<ScrollViewer>("DetailPreviewTextScroll");
        var info = this.FindControl<StackPanel>("DetailInfoPanel");
        var btn = this.FindControl<Button>("DetailOpenExternalBtn");

        if(img!=null) img.IsVisible=false; 
        if(scroll!=null) scroll.IsVisible=false; 
        if(info!=null) info.IsVisible=true; 
        if(btn!=null) btn.IsVisible=false;
    }

    private async void OnDetailFileSelected(object? sender, SelectionChangedEventArgs e) => await LoadPreview(sender as ListBox, true);
    private async void OnFileSelected(object? sender, SelectionChangedEventArgs e) => await LoadPreview(sender as ListBox, false);

    private async Task LoadPreview(ListBox? listBox, bool isDetail)
    {
        if (listBox?.SelectedItem is FileItem selectedFile)
        {
            string prefix = isDetail ? "Detail" : "";
            var img = this.FindControl<Image>($"{prefix}PreviewImage");
            var txt = this.FindControl<TextBlock>($"{prefix}PreviewText");
            var scroll = this.FindControl<ScrollViewer>($"{prefix}PreviewTextScroll");
            var info = this.FindControl<StackPanel>($"{prefix}InfoPanel");
            var btn = this.FindControl<Button>($"{prefix}OpenExternalBtn");

            if(img!=null) img.IsVisible=false; 
            if(scroll!=null) scroll.IsVisible=false; 
            if(info!=null) info.IsVisible=true; 
            if(btn!=null) { btn.IsVisible=true; btn.Tag=selectedFile.FullPath; }

            string ext = System.IO.Path.GetExtension(selectedFile.FullPath).ToLower();

            if (IsImage(ext) && img != null)
            {
                try { img.Source = new Bitmap(selectedFile.FullPath); img.IsVisible=true; if(info!=null) info.IsVisible=false; } catch {}
            }
            else if (IsText(ext) && txt != null && scroll != null)
            {
                try 
                { 
                    string c = await File.ReadAllTextAsync(selectedFile.FullPath); 
                    if(ext==".rtf") c=StripRtf(c); 
                    if(c.Length>2000) c=c.Substring(0,2000)+"..."; 
                    txt.Text=c; scroll.IsVisible=true; if(info!=null) info.IsVisible=false; 
                } catch {}
            }
        }
    }

    private void DetailOpenExternal_Click(object? sender, RoutedEventArgs e) 
    { 
        var btn = sender as Button; 
        if(btn?.Tag is string p) OpenWithDefaultProgram(p); 
    }

    private void BackToTagDashboard(object? sender, RoutedEventArgs e) => ShowTagDashboard();

    // ========================
    // 關聯圖核心 (繪製)
    // ========================
    private void DrawGraph()
    {
        var canvas = this.FindControl<Canvas>("GraphCanvas");
        if (canvas == null || _currentFiles.Count == 0) return;

        if (canvas.Children.Count == 0 || _activeTagNodes.Count == 0)
        {
            canvas.Children.Clear();
            _activeTagNodes.Clear();

            DrawGrid(canvas);

            // 中心點 (Desktop)
            _centerNodePanel = CreateNodePanel(CANVAS_CENTER_X, CANVAS_CENTER_Y, CENTER_NODE_SIZE, Brushes.DodgerBlue, "Desktop", null);
            canvas.Children.Add(_centerNodePanel);

            Random rnd = new Random();
            foreach (var file in _currentFiles)
            {
                if (file.X == 0)
                {
                    double angle = rnd.NextDouble() * Math.PI * 2;
                    double distance = 250 + rnd.NextDouble() * 300;
                    file.X = CANVAS_CENTER_X + Math.Cos(angle) * distance;
                    file.Y = CANVAS_CENTER_Y + Math.Sin(angle) * distance;
                }

                var node = CreateNodePanel(file.X, file.Y, FILE_NODE_SIZE, Brushes.Orange, file.Name, file);
                canvas.Children.Add(node);

                foreach(var tag in file.Tags)
                {
                    EnsureTagNodeExists(tag, CANVAS_CENTER_X, CANVAS_CENTER_Y, canvas);
                }
            }
            
            RefreshLines(canvas);
        }
    }

    private void DrawGrid(Canvas canvas)
    {
        var borderRect = new Rectangle { Width = CANVAS_WIDTH, Height = CANVAS_HEIGHT, Stroke = Brushes.Red, StrokeThickness = 2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>{ 4, 4 }, Opacity = 0.5 };
        Canvas.SetLeft(borderRect, 0);
        Canvas.SetTop(borderRect, 0);
        canvas.Children.Add(borderRect);

        for(int i = 0; i <= 5000; i += 500)
        {
            var lineV = new Line { StartPoint=new Point(i,0), EndPoint=new Point(i,5000), Stroke=Brushes.Gray, Opacity=0.1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>{ 2, 2 } };
            var lineH = new Line { StartPoint=new Point(0,i), EndPoint=new Point(5000,i), Stroke=Brushes.Gray, Opacity=0.1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>{ 2, 2 } };
            canvas.Children.Add(lineV);
            canvas.Children.Add(lineH);
        }
        canvas.Children.Add(new Line { StartPoint=new Point(CANVAS_CENTER_X, CANVAS_CENTER_Y-100), EndPoint=new Point(CANVAS_CENTER_X, CANVAS_CENTER_Y+100), Stroke=Brushes.Gray, Opacity=0.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>{ 4, 4 } });
        canvas.Children.Add(new Line { StartPoint=new Point(CANVAS_CENTER_X-100, CANVAS_CENTER_Y), EndPoint=new Point(CANVAS_CENTER_X+100, CANVAS_CENTER_Y), Stroke=Brushes.Gray, Opacity=0.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>{ 4, 4 } });
    }
    
    private void EnsureTagNodeExists(FileTag tag, double cx, double cy, Canvas canvas)
    {
        if (!_activeTagNodes.ContainsKey(tag.Name))
        {
            Random rnd = new Random();
            double angle = rnd.NextDouble() * Math.PI * 2;
            double distance = 150;
            var node = CreateNodePanel(cx + Math.Cos(angle)*distance, cy + Math.Sin(angle)*distance, TAG_NODE_SIZE, Brush.Parse(tag.Color), tag.Name, tag);
            canvas.Children.Add(node);
            _activeTagNodes.Add(tag.Name, node);
        }
    }
    
    private void RefreshLines(Canvas canvas)
    {
        var lines = canvas.Children.OfType<Line>().Where(l => l.Opacity > 0.2 && l.Opacity != 0.5).ToList(); 
        foreach(var l in lines) canvas.Children.Remove(l);

        if (_centerNodePanel != null)
        {
            double cx = Canvas.GetLeft(_centerNodePanel) + (NODE_PANEL_WIDTH/2); 
            double cy = Canvas.GetTop(_centerNodePanel) + (CENTER_NODE_SIZE/2); 
            
            foreach(var file in _currentFiles)
            {
                var line = new Line { StartPoint = new Point(cx, cy), EndPoint = new Point(file.X + (NODE_PANEL_WIDTH/2), file.Y + (FILE_NODE_SIZE/2)), Stroke = Brushes.Gray, StrokeThickness = 1, Opacity = 0.3 };
                canvas.Children.Insert(2, line); 
                file.ConnectionLine = line; 
            }
        }

        foreach(var file in _currentFiles)
        {
            foreach(var tag in file.Tags)
            {
                if (_activeTagNodes.ContainsKey(tag.Name))
                {
                    var tagNode = _activeTagNodes[tag.Name];
                    var line = new Line
                    {
                        StartPoint = new Point(file.X + (NODE_PANEL_WIDTH/2), file.Y + (FILE_NODE_SIZE/2)),
                        EndPoint = new Point(Canvas.GetLeft(tagNode) + (NODE_PANEL_WIDTH/2), Canvas.GetTop(tagNode) + (TAG_NODE_SIZE/2)),
                        Stroke = Brush.Parse(tag.Color),
                        StrokeThickness = 2,
                        Opacity = 0.6
                    };
                    canvas.Children.Insert(2, line);
                }
            }
        }
    }

    // --- 導航至標籤 ---
    private void NavigateToTag_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        string? tagName = btn?.Tag?.ToString();
        if (tagName == null) return;
        
        var canvas = this.FindControl<Canvas>("GraphCanvas");
        if (canvas == null) return;

        // 如果不在畫布上，先生成
        if (!_activeTagNodes.ContainsKey(tagName))
        {
             var tag = _availableTags.FirstOrDefault(t => t.Name == tagName);
             if (tag != null)
             {
                 Random rnd = new Random();
                 double cx = CANVAS_CENTER_X + rnd.Next(-250, 250);
                 double cy = CANVAS_CENTER_Y + rnd.Next(-250, 250);
                 EnsureTagNodeExists(tag, cx, cy, canvas);
                 RefreshLines(canvas);
             }
        }

        // [修復] 導航計算
        if (_activeTagNodes.ContainsKey(tagName))
        {
            var node = _activeTagNodes[tagName];
            double nx = Canvas.GetLeft(node) + NODE_PANEL_WIDTH/2;
            double ny = Canvas.GetTop(node) + TAG_NODE_SIZE/2;
            ForceResetView(new Point(nx, ny));
        }
    }

    // --- Toggle 標籤節點 ---
    private void SpawnTagNode_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        string? tagName = btn?.Tag?.ToString();
        var tag = _availableTags.FirstOrDefault(t => t.Name == tagName);
        var canvas = this.FindControl<Canvas>("GraphCanvas");
        
        if (canvas == null || tag == null || tagName == null) return;

        if (_activeTagNodes.ContainsKey(tagName)) 
        {
            var nodeToRemove = _activeTagNodes[tagName];
            canvas.Children.Remove(nodeToRemove);
            _activeTagNodes.Remove(tagName);
            RefreshLines(canvas); 
        }
        else
        {
            Random rnd = new Random();
            double cx = CANVAS_CENTER_X + rnd.Next(-250, 250);
            double cy = CANVAS_CENTER_Y + rnd.Next(-250, 250);
             
            EnsureTagNodeExists(tag, cx, cy, canvas);
            RefreshLines(canvas);
        }
    }

    private Panel CreateNodePanel(double x, double y, double size, IBrush color, string text, object? dataItem)
    {
        bool isDraggable = (dataItem != null); 

        var panel = new Panel { Width = NODE_PANEL_WIDTH, Height = size + 20, Background = Brushes.Transparent };
        Canvas.SetLeft(panel, x);
        Canvas.SetTop(panel, y);

        if (dataItem is FileItem f) {
            string ext = System.IO.Path.GetExtension(f.FullPath).ToLower();
            if (!IsImage(ext) && !IsText(ext)) color = Brushes.MediumSeaGreen;
        }

        var border = new Border 
        { 
            Width = size + 6, Height = size + 6, CornerRadius = new CornerRadius((size+6)/2),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(0, -3, 0, 0)
        };
        border.Child = new Ellipse { Width = size, Height = size, Fill = color };
        var label = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, size + 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };

        panel.Children.Add(border);
        panel.Children.Add(label);

        if (isDraggable)
        {
            panel.PointerPressed += (s, e) => {
                var props = e.GetCurrentPoint(panel).Properties;
                if (props.IsLeftButtonPressed) {
                    if (_isLinkMode && _selectedItem != null && _selectedItem != dataItem) { HandleLinkAction(dataItem); e.Handled = true; return; }
                    SelectNode(panel, dataItem);
                    _isDraggingNode = true; _draggedNode = panel; _draggedItem = dataItem;
                    if (panel.Parent is Visual parent) _dragStartPoint = e.GetPosition(parent);
                    e.Handled = true;
                }
            };

            panel.PointerMoved += (s, e) => {
                if (!_isDraggingNode || _draggedNode == null) return;
                if (_draggedNode.Parent is Visual parent) {
                    var currentPoint = e.GetPosition(parent);
                    var offsetX = currentPoint.X - _dragStartPoint.X;
                    var offsetY = currentPoint.Y - _dragStartPoint.Y;
                    double newX = Canvas.GetLeft(_draggedNode) + offsetX;
                    double newY = Canvas.GetTop(_draggedNode) + offsetY;
                    Canvas.SetLeft(_draggedNode, newX);
                    Canvas.SetTop(_draggedNode, newY);
                    if (_draggedItem is FileItem fileItem) { fileItem.X = newX; fileItem.Y = newY; }
                    _dragStartPoint = currentPoint;
                }
            };

            panel.PointerReleased += (s, e) => {
                if (_isDraggingNode) {
                    _isDraggingNode = false; _draggedNode = null; _draggedItem = null;
                    var canvas = this.FindControl<Canvas>("GraphCanvas");
                    if(canvas != null) RefreshLines(canvas);
                }
            };
        }
        else 
        {
            panel.PointerPressed += (s, e) => {
               if (e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed) {
                   if (_isLinkMode && _selectedItem != null && _selectedItem != dataItem) { HandleLinkAction(dataItem); e.Handled = true; return; }
                   SelectNode(panel, dataItem);
                   e.Handled = true;
               }
            };
        }
        return panel;
    }

    private void SelectNode(Panel node, object? dataItem)
    {
        if (_selectedNode != null) { var oldBorder = _selectedNode.Children.OfType<Border>().FirstOrDefault(); if (oldBorder != null) oldBorder.BorderThickness = new Thickness(0); }
        _selectedNode = node; _selectedItem = dataItem;
        var newBorder = node.Children.OfType<Border>().FirstOrDefault(); if (newBorder != null) newBorder.BorderThickness = new Thickness(3);
        var selectionPanel = this.FindControl<Border>("SelectionPanel"); var text = this.FindControl<TextBlock>("SelectedNodeText"); var linkModeHint = this.FindControl<Border>("LinkModeHint");
        if (selectionPanel != null) selectionPanel.IsVisible = true; if (linkModeHint != null) linkModeHint.IsVisible = false; _isLinkMode = false;
        string name = (dataItem as FileItem)?.Name ?? (dataItem as FileTag)?.Name ?? "Desktop"; if (text != null) text.Text = $"已選取: {name}";
    }

    private void ToggleLinkMode_Click(object? sender, RoutedEventArgs e) { _isLinkMode = !_isLinkMode; var hint = this.FindControl<Border>("LinkModeHint"); if (hint != null) hint.IsVisible = _isLinkMode; }

    private void HandleLinkAction(object? targetItem)
    {
        FileItem? file = null; FileTag? tag = null;
        if (_selectedItem is FileItem f && targetItem is FileTag t) { file = f; tag = t; }
        else if (_selectedItem is FileTag t2 && targetItem is FileItem f2) { file = f2; tag = t2; }
        if (file != null && tag != null) { AddTagToFile(file, tag); _isLinkMode = false; var hint = this.FindControl<Border>("LinkModeHint"); if (hint != null) hint.IsVisible = false; }
    }

    // --- 互動控制 ---
    private void OnGraphContainerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
        {
            _isPanning = true; _lastMousePos = e.GetPosition(this);
            if (_selectedNode != null)
            {
                var oldBorder = _selectedNode.Children.OfType<Border>().FirstOrDefault();
                if (oldBorder != null) oldBorder.BorderThickness = new Thickness(0);
                _selectedNode = null; _selectedItem = null;
                var p = this.FindControl<Border>("SelectionPanel"); if (p != null) p.IsVisible = false;
                _isLinkMode = false;
                var hint = this.FindControl<Border>("LinkModeHint"); if (hint != null) hint.IsVisible = false;
            }
        }
    }

    private void OnGraphContainerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
        {
            var currentPos = e.GetPosition(this);
            var delta = currentPos - _lastMousePos;
            _lastMousePos = currentPos;
            _graphTranslate.X += delta.X;
            _graphTranslate.Y += delta.Y;
        }
    }

    private void OnGraphContainerReleased(object? sender, PointerReleasedEventArgs e) { _isPanning = false; }

    private void OnGraphScroll(object? sender, PointerWheelEventArgs e)
    {
        var container = this.FindControl<Panel>("GraphContainer");
        if (container == null) return;
        double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        if (container.Parent is Control parent)
        {
            Point center = new Point(parent.Bounds.Width / 2, parent.Bounds.Height / 2);
            ApplyZoom(zoomFactor, center);
        }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        var container = this.FindControl<Panel>("GraphContainer");
        if (container?.Parent is Control parent)
            ApplyZoom(1.2, new Point(parent.Bounds.Width / 2, parent.Bounds.Height / 2));
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        var container = this.FindControl<Panel>("GraphContainer");
        if (container?.Parent is Control parent)
            ApplyZoom(1 / 1.2, new Point(parent.Bounds.Width / 2, parent.Bounds.Height / 2));
    }

    private void ApplyZoom(double zoomFactor, Point focusPoint)
    {
        double oldScale = _graphScale.ScaleX;
        double newScale = oldScale * zoomFactor;
        if (newScale < 0.1 || newScale > 5) return;

        double worldX = (focusPoint.X - _graphTranslate.X) / oldScale;
        double worldY = (focusPoint.Y - _graphTranslate.Y) / oldScale;

        _graphScale.ScaleX = newScale;
        _graphScale.ScaleY = newScale;
        _graphTranslate.X = focusPoint.X - (worldX * newScale);
        _graphTranslate.Y = focusPoint.Y - (worldY * newScale);
    }

    private void ResetGraphView(object? sender, RoutedEventArgs? e) => ForceResetView();

    // [修正] 精準重置，對齊 Desktop 圓心 (2500 + 80, 2500 + 30)
    private void ForceResetView(Point? targetCenter = null)
    {
        var container = this.FindControl<Panel>("GraphContainer");
        if (container == null || container.Parent is not Control parent) return;

        double viewWidth = parent.Bounds.Width > 0 ? parent.Bounds.Width : 1100;
        double viewHeight = parent.Bounds.Height > 0 ? parent.Bounds.Height : 700;

        double targetX = targetCenter?.X ?? (CANVAS_CENTER_X + NODE_PANEL_WIDTH / 2);
        double targetY = targetCenter?.Y ?? (CANVAS_CENTER_Y + CENTER_NODE_SIZE / 2);

        double finalTransX = (viewWidth / 2) - targetX;
        double finalTransY = (viewHeight / 2) - targetY;

        _graphScale.ScaleX = 1;
        _graphScale.ScaleY = 1;
        _graphTranslate.X = finalTransX;
        _graphTranslate.Y = finalTransY;
    }

    // --- 輔助函式 ---
    private void ShowCustomTagDialog(object? sender, RoutedEventArgs e) { var o = this.FindControl<Grid>("CustomTagOverlay"); if(o != null) o.IsVisible = true; }
    private void CloseTagDialog(object? sender, RoutedEventArgs? e) { var o = this.FindControl<Grid>("CustomTagOverlay"); if(o != null) o.IsVisible = false; }
    private async void SelectFolder_Click(object? sender, RoutedEventArgs e) { if (!StorageProvider.CanPickFolder) return; var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "選擇資料夾" }); if (folders.Count > 0) { _currentPath = folders[0].Path.LocalPath; LoadFiles(_currentPath); } }
    private void OpenExternal_Click(object? sender, RoutedEventArgs e) { if ((this.FindControl<ListBox>("FileListBox")?.SelectedItem as FileItem) is FileItem f) OpenWithDefaultProgram(f.FullPath); }
    private void OpenWithDefaultProgram(string path) { try { if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else Process.Start("open", path); } catch {} }
    private void AiTrash_Click(object? sender, RoutedEventArgs e) { }
    private async void AiOrganize_Click(object? sender, RoutedEventArgs e) { await Task.Delay(1000); foreach(var f in _currentFiles.Take(5)) AddTagToFile(f, new FileTag { Name="AI", Color="#f39c12", Emoji="🤖" }); }
    private string StripRtf(string rtf) { try { return Regex.Replace(rtf, @"\{\*?\\[^{}]+}|[{}]|\\\n?[A-Za-z]+\n?(?:-?\d+)?[ ]?", "").Trim(); } catch { return rtf; } }
    private bool IsImage(string ext) => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" }.Contains(ext);
    private bool IsText(string ext) => new[] { ".txt", ".md", ".cs", ".json", ".xml", ".html", ".css", ".js", ".rtf" }.Contains(ext);
    private string GetIcon(string ext) { if (IsImage(ext)) return "🖼️"; if (IsText(ext)) return "📝"; if (ext == ".pdf") return "📕"; if (ext == ".zip") return "📦"; return "📄"; }
    private string FormatSize(long bytes) { string[] sizes = { "B", "KB", "MB", "GB" }; double len = bytes; int order = 0; while (len >= 1024 && order < sizes.Length - 1) { order++; len = len / 1024; } return $"{len:0.#} {sizes[order]}"; }
}