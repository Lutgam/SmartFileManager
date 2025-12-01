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
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace App;

// --- 資料模型 ---
public class FileTag : IEquatable<FileTag>
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#888888";
    public string Emoji { get; set; } = "🏷️";
    public bool Equals(FileTag? other) => other != null && Name == other.Name && Color == other.Color;
}

public class FileItem 
{ 
    public string Name { get; set; } = ""; 
    public string FullPath { get; set; } = ""; 
    public string Icon { get; set; } = "📄";
    public string DisplaySize { get; set; } = "";
    public string DisplayDate { get; set; } = "";
    public ObservableCollection<FileTag> Tags { get; set; } = new ObservableCollection<FileTag>();
    
    // 關聯圖物件參考
    public Line? ConnectionLine { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public partial class MainWindow : Window
{
    private ObservableCollection<FileItem> _currentFiles = new ObservableCollection<FileItem>();
    
    // 全域標籤庫
    private ObservableCollection<FileTag> _availableTags = new ObservableCollection<FileTag>();

    private string _currentPath = "";
    private string _selectedColor = "#3498db"; 

    // --- 關聯圖變數 ---
    private bool _isDraggingNode = false;
    private bool _isPanning = false;
    private Panel? _draggedNode = null;
    private object? _draggedItem = null; // 可能是 FileItem 或 TagNode
    private Point _lastMousePos;
    private Point _dragStartPoint; 

    // 關聯圖中心點物件
    private Panel? _centerNodePanel = null;
    
    // 管理畫布上的標籤節點，避免重複生成
    private Dictionary<string, Panel> _activeTagNodes = new Dictionary<string, Panel>();

    public MainWindow()
    {
        InitializeComponent();
        _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        Opened += (s, e) => {
            InitDefaultTags(); 
            LoadFiles(_currentPath);
            InitColorPicker();
        };
        
        SizeChanged += (s, e) => DrawGraph();
    }

    private void InitDefaultTags()
    {
        _availableTags.Add(new FileTag { Name = "待辦", Color = "#e74c3c", Emoji = "🔴" });
        _availableTags.Add(new FileTag { Name = "學校", Color = "#3498db", Emoji = "🔵" });
        _availableTags.Add(new FileTag { Name = "完成", Color = "#2ecc71", Emoji = "🟢" });
        
        var quickTagList = this.FindControl<ItemsControl>("QuickTagList");
        var miniTagList = this.FindControl<ItemsControl>("MiniTagList");
        
        if (quickTagList != null) quickTagList.ItemsSource = _availableTags;
        if (miniTagList != null) miniTagList.ItemsSource = _availableTags;
    }

    // ========================
    // 1. 檔案讀取
    // ========================
    private void LoadFiles(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;

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

            _currentFiles.Clear();
            foreach(var f in files) _currentFiles.Add(f);

            var listBox = this.FindControl<ListBox>("FileListBox");
            if(listBox != null) listBox.ItemsSource = _currentFiles;
            
            // 換資料夾時，清空標籤節點快取
            _activeTagNodes.Clear();
            DrawGraph(); 
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    // ========================
    // 2. 標籤系統 (動態化)
    // ========================
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
            var newTag = new FileTag { Name = tagTemplate.Name, Color = tagTemplate.Color, Emoji = tagTemplate.Emoji };
            var existing = selectedFile.Tags.FirstOrDefault(t => t.Name == newTag.Name);
            if (existing != null) selectedFile.Tags.Remove(existing); // Toggle
            else selectedFile.Tags.Add(newTag);
        }
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
                Width = 30, Height = 30,
                Background = Brush.Parse(color),
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(15)
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
        string tagName = input?.Text ?? "";
        
        if (!string.IsNullOrWhiteSpace(tagName))
        {
            var newGlobalTag = new FileTag { Name = tagName, Color = _selectedColor, Emoji = "🔖" };
            if (!_availableTags.Any(t => t.Name == tagName))
            {
                _availableTags.Add(newGlobalTag);
            }
            ApplyTagToSelected(newGlobalTag);
            input.Text = "";
            CloseTagDialog(null, null);
        }
    }

    // ========================
    // 3. 標籤管理導航
    // ========================
    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        var tabControl = sender as TabControl;
        if (e.Source != tabControl) return;

        if (tabControl?.SelectedIndex == 1) ShowTagDashboard();
        if (tabControl?.SelectedIndex == 2) DrawGraph();
    }

    private void ShowTagDashboard()
    {
        var dashboard = this.FindControl<Grid>("TagDashboardView");
        var detail = this.FindControl<Grid>("TagDetailView");
        if(dashboard != null) dashboard.IsVisible = true;
        if(detail != null) detail.IsVisible = false;
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
                Background = Brush.Parse("#252526"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(10),
                Width = 200, Height = 100,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
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

        var filteredFiles = _currentFiles.Where(f => f.Tags.Any(t => t.Name == tagName)).ToList();
        if(list != null) list.ItemsSource = filteredFiles;
    }

    private async void OnDetailFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is FileItem selectedFile)
        {
            var imgControl = this.FindControl<Image>("DetailPreviewImage");
            var textControl = this.FindControl<TextBlock>("DetailPreviewText");
            var textScroll = this.FindControl<ScrollViewer>("DetailPreviewTextScroll");
            var infoPanel = this.FindControl<StackPanel>("DetailInfoPanel");

            if(imgControl != null) imgControl.IsVisible = false;
            if(textScroll != null) textScroll.IsVisible = false;
            if(infoPanel != null) infoPanel.IsVisible = true;

            string ext = System.IO.Path.GetExtension(selectedFile.FullPath).ToLower();

            if (IsImage(ext) && imgControl != null) {
                try { imgControl.Source = new Bitmap(selectedFile.FullPath); imgControl.IsVisible = true; if(infoPanel != null) infoPanel.IsVisible = false; } catch {}
            } else if (IsText(ext) && textControl != null && textScroll != null) {
                try { 
                    string content = await File.ReadAllTextAsync(selectedFile.FullPath); 
                    if (ext == ".rtf") content = StripRtf(content);
                    if(content.Length > 2000) content = content.Substring(0, 2000) + "..."; 
                    textControl.Text = content; textScroll.IsVisible = true; if(infoPanel != null) infoPanel.IsVisible = false; 
                } catch {}
            }
        }
    }

    private void BackToTagDashboard(object? sender, RoutedEventArgs e)
    {
        ShowTagDashboard();
    }

    // ========================
    // 4. 關聯圖 (Graph View 3.0)
    // ========================
    private void DrawGraph()
    {
        var canvas = this.FindControl<Canvas>("GraphCanvas");
        if (canvas == null || _currentFiles.Count == 0) return;

        // 如果是初始狀態，才清空重畫
        if (canvas.Children.Count == 0 || _currentFiles.Any(f => f.X == 0))
        {
            canvas.Children.Clear();
            _activeTagNodes.Clear(); 

            double centerX = 2500; 
            double centerY = 2500;
            
            _centerNodePanel = CreateNodePanel(centerX, centerY, 60, Brushes.DodgerBlue, "Desktop", null);
            canvas.Children.Add(_centerNodePanel);

            Random rnd = new Random();
            foreach (var file in _currentFiles)
            {
                if (file.X == 0) {
                    double angle = rnd.NextDouble() * Math.PI * 2;
                    double distance = 250 + rnd.NextDouble() * 300;
                    file.X = centerX + Math.Cos(angle) * distance;
                    file.Y = centerY + Math.Sin(angle) * distance;
                }

                var line = new Line
                {
                    StartPoint = new Point(centerX + 80, centerY + 30), 
                    EndPoint = new Point(file.X + 80, file.Y + 20),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    Opacity = 0.3
                };
                canvas.Children.Insert(0, line);
                file.ConnectionLine = line;

                var node = CreateNodePanel(file.X, file.Y, 40, Brushes.Orange, file.Name, file);
                canvas.Children.Add(node);
            }
        }
    }

    private void SpawnTagNode_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        string? tagName = btn?.Tag?.ToString();
        var tag = _availableTags.FirstOrDefault(t => t.Name == tagName);
        
        var canvas = this.FindControl<Canvas>("GraphCanvas");
        if (canvas == null || tag == null || tagName == null) return;

        if (_activeTagNodes.ContainsKey(tagName)) return;

        double cx = 2500; 
        double cy = 2500;
        
        var node = CreateNodePanel(cx, cy - 150, 50, Brush.Parse(tag.Color), tag.Name, tag); 
        canvas.Children.Add(node);
        _activeTagNodes.Add(tag.Name, node);
    }

    private Panel CreateNodePanel(double x, double y, double size, IBrush color, string text, object? dataItem)
    {
        var panel = new Panel { Width = 160, Height = size + 20, Background = Brushes.Transparent };
        Canvas.SetLeft(panel, x);
        Canvas.SetTop(panel, y);

        if (dataItem is FileItem f) {
            string ext = System.IO.Path.GetExtension(f.FullPath).ToLower();
            if (!IsImage(ext) && !IsText(ext)) color = Brushes.MediumSeaGreen;
        }

        var ellipse = new Ellipse { Width = size, Height = size, Fill = color, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var label = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, size + 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };

        panel.Children.Add(ellipse);
        panel.Children.Add(label);

        panel.PointerPressed += (s, e) => {
            _isDraggingNode = true;
            _draggedNode = panel;
            _draggedItem = dataItem; 
            if (panel.Parent is Visual parent) _dragStartPoint = e.GetPosition(parent);
            e.Handled = true; 
        };

        panel.PointerMoved += (s, e) => {
            if (!_isDraggingNode || _draggedNode == null) return;
            if (_draggedNode.Parent is Visual parent)
            {
                var currentPoint = e.GetPosition(parent);
                var offsetX = currentPoint.X - _dragStartPoint.X;
                var offsetY = currentPoint.Y - _dragStartPoint.Y;

                double newX = Canvas.GetLeft(_draggedNode) + offsetX;
                double newY = Canvas.GetTop(_draggedNode) + offsetY;

                Canvas.SetLeft(_draggedNode, newX);
                Canvas.SetTop(_draggedNode, newY);

                if (_draggedItem is FileItem fileItem) {
                    fileItem.X = newX; fileItem.Y = newY;
                    if (fileItem.ConnectionLine != null) fileItem.ConnectionLine.EndPoint = new Point(newX + 80, newY + 20);
                }
                
                if (_draggedNode == _centerNodePanel) {
                    foreach(var f in _currentFiles) {
                        if (f.ConnectionLine != null) f.ConnectionLine.StartPoint = new Point(newX + 80, newY + 30);
                    }
                }

                _dragStartPoint = currentPoint;
            }
        };

        panel.PointerReleased += (s, e) => {
            _isDraggingNode = false;
            _draggedNode = null;
            _draggedItem = null;
        };

        return panel;
    }

    // --- 互動控制 ---
    private (ScaleTransform? scale, TranslateTransform? translate) GetTransforms()
    {
        var container = this.FindControl<Panel>("GraphContainer");
        if (container?.RenderTransform is TransformGroup group)
        {
            var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            return (scale, translate);
        }
        return (null, null);
    }

    private void OnGraphContainerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed) { _isPanning = true; _lastMousePos = e.GetPosition(this); }
    }

    private void OnGraphContainerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
        {
            var currentPos = e.GetPosition(this);
            var delta = currentPos - _lastMousePos;
            _lastMousePos = currentPos;
            var (_, translate) = GetTransforms();
            if (translate != null) { translate.X += delta.X; translate.Y += delta.Y; }
        }
    }

    private void OnGraphContainerReleased(object? sender, PointerReleasedEventArgs e) { _isPanning = false; }

    private void OnGraphScroll(object? sender, PointerWheelEventArgs e)
    {
        var (scale, _) = GetTransforms();
        if (scale != null)
        {
            double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            scale.ScaleX *= zoomFactor;
            scale.ScaleY *= zoomFactor;
        }
    }

    private void ResetGraphView(object? sender, RoutedEventArgs e)
    {
        var (scale, translate) = GetTransforms();
        if(scale!=null){scale.ScaleX=1;scale.ScaleY=1;}
        if(translate!=null){translate.X=0;translate.Y=0;}
    }

    // --- 輔助函式 ---
    private void ShowCustomTagDialog(object? sender, RoutedEventArgs e) { var o = this.FindControl<Grid>("CustomTagOverlay"); if(o != null) o.IsVisible = true; }
    private void CloseTagDialog(object? sender, RoutedEventArgs e) { var o = this.FindControl<Grid>("CustomTagOverlay"); if(o != null) o.IsVisible = false; }
    private async void SelectFolder_Click(object? sender, RoutedEventArgs e) {
        if (!StorageProvider.CanPickFolder) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "選擇資料夾" });
        if (folders.Count > 0) { _currentPath = folders[0].Path.LocalPath; LoadFiles(_currentPath); DrawGraph(); }
    }
    private void OpenExternal_Click(object? sender, RoutedEventArgs e) { if ((this.FindControl<ListBox>("FileListBox")?.SelectedItem as FileItem) is FileItem f) OpenWithDefaultProgram(f.FullPath); }
    private void OpenWithDefaultProgram(string path) { try { if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else Process.Start("open", path); } catch {} }
    private void AiTrash_Click(object? sender, RoutedEventArgs e) { }
    private async void AiOrganize_Click(object? sender, RoutedEventArgs e) { await Task.Delay(1000); }
    private async void OnFileSelected(object? sender, SelectionChangedEventArgs e) {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is FileItem selectedFile) {
            var imgControl = this.FindControl<Image>("PreviewImage"); var textControl = this.FindControl<TextBlock>("PreviewText"); var textScroll = this.FindControl<ScrollViewer>("PreviewTextScroll"); var infoPanel = this.FindControl<StackPanel>("InfoPanel"); var openBtn = this.FindControl<Button>("OpenExternalBtn");
            if(imgControl!=null)imgControl.IsVisible=false; if(textScroll!=null)textScroll.IsVisible=false; if(infoPanel!=null)infoPanel.IsVisible=true; if(openBtn!=null)openBtn.IsVisible=true;
            string ext = System.IO.Path.GetExtension(selectedFile.FullPath).ToLower();
            if (IsImage(ext) && imgControl != null) { try { imgControl.Source = new Bitmap(selectedFile.FullPath); imgControl.IsVisible = true; if(infoPanel!=null)infoPanel.IsVisible=false; } catch {} }
            else if (IsText(ext) && textControl != null && textScroll != null) { try { string content = await File.ReadAllTextAsync(selectedFile.FullPath); if (ext == ".rtf") content = StripRtf(content); if(content.Length > 2000) content = content.Substring(0, 2000) + "..."; textControl.Text = content; textScroll.IsVisible = true; if(infoPanel!=null)infoPanel.IsVisible=false; } catch {} }
        }
    }
    private string StripRtf(string rtf) { try { return Regex.Replace(rtf, @"\{\*?\\[^{}]+}|[{}]|\\\n?[A-Za-z]+\n?(?:-?\d+)?[ ]?", "").Trim(); } catch { return rtf; } }
    private bool IsImage(string ext) => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" }.Contains(ext);
    private bool IsText(string ext) => new[] { ".txt", ".md", ".cs", ".json", ".xml", ".html", ".css", ".js", ".rtf" }.Contains(ext);
    private string GetIcon(string ext) { if (IsImage(ext)) return "🖼️"; if (IsText(ext)) return "📝"; if (ext == ".pdf") return "📕"; if (ext == ".zip") return "📦"; return "📄"; }
    private string FormatSize(long bytes) { string[] sizes = { "B", "KB", "MB", "GB" }; double len = bytes; int order = 0; while (len >= 1024 && order < sizes.Length - 1) { order++; len = len / 1024; } return $"{len:0.#} {sizes[order]}"; }
}