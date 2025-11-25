using System;
using System.IO;

namespace SmartFileManager
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   Smart File Manager (Core Service)    ");
            Console.WriteLine("   Version: 0.1.0 (Prototype)           ");
            Console.WriteLine("========================================");
            Console.WriteLine("[System] 初始化檔案監控服務...");

            // 設定監控路徑 (這裡先預設監控你的桌面，方便測試)
            // Environment.SpecialFolder.Desktop 會自動抓到你電腦的桌面路徑
            string watchPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            
            Console.WriteLine($"[Monitor] 正在監控: {watchPath}");
            Console.WriteLine("[Monitor] 請試著在桌面新增、刪除或重新命名檔案...");
            Console.WriteLine("[Monitor] 按下 'q' 鍵退出程式");

            // 建立檔案監控器
            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                watcher.Path = watchPath;
                
                // 設定要監控哪些變化 (檔名改變、大小改變)
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
                watcher.Filter = "*.*"; // 監控所有類型的檔案

                // 綁定事件處理函式
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                
                // 開始監控
                watcher.EnableRaisingEvents = true;

                // 讓程式停在這裡，直到按下 q
                while (Console.Read() != 'q') ;
            }
        }

        // 當檔案被新增或刪除時執行
        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"[偵測] 檔案狀態改變 ({e.ChangeType}): {e.Name}");
            // TODO: 未來這裡會呼叫 ML.NET 來辨識這個新檔案
        }

        // 當檔案被改名時執行
        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Console.WriteLine($"[偵測] 檔案更名: {e.OldName} -> {e.Name}");
        }
    }
}