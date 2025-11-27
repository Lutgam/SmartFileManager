using Avalonia;
using System;

namespace App;

class Program
{
    // 程式進入點
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 設定
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(); // 移除了 UseDesktopWebView()，紅字就會消失
}