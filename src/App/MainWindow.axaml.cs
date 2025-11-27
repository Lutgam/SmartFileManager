using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace App;

// 將 FileItem 移出來，變成獨立的類別，結構更清晰
public class FileItem 
{ 
    public string Name { get; set; } = ""; 
    public string FullPath { get; set; } = ""; 
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadFiles();
    }

    // 1. 讀取桌面檔案
    private void LoadFiles()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dir = new DirectoryInfo(desktopPath);
            
            // 抓取非隱藏檔案
            var files = dir.GetFiles()
                           .Where(f => !f.Name.StartsWith("."))
                           .Select(f => new FileItem { Name = f.Name, FullPath = f.FullName })
                           .ToList();

            // 綁定到左側列表
            var listBox = this.FindControl<ListBox>("FileListBox");
            if(listBox != null) listBox.ItemsSource = files;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // 2. 當使用者點擊檔案時
    private void OnFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is FileItem selectedFile)
        {
            var infoText = this.FindControl<TextBlock>("InfoText");
            var imageControl = this.FindControl<Image>("PreviewImage");

            if(infoText != null) infoText.Text = $"選取檔案: {selectedFile.Name}";

            // 嘗試顯示圖片
            if (IsImage(selectedFile.FullPath) && imageControl != null)
            {
                try
                {
                    // 載入圖片到記憶體
                    imageControl.Source = new Bitmap(selectedFile.FullPath);
                    if(infoText != null) infoText.Text += "\n(預覽載入成功)";
                }
                catch
                {
                    imageControl.Source = null;
                    if(infoText != null) infoText.Text += "\n(無法預覽此格式)";
                }
            }
            else if (imageControl != null)
            {
                imageControl.Source = null; // 非圖片則清空
                if(infoText != null) infoText.Text += "\n(非圖片檔案)";
            }
        }
    }

    private bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp";
    }
}