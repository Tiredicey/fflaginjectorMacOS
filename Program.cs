using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace FlagInjector;

sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Th.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var win = new MainWindow(desktop.Args ?? Array.Empty<string>());
            desktop.MainWindow = win;
            desktop.ShutdownRequested += (_, _) => win.PrepareExit();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("This build targets macOS only.");
            return 1;
        }

        if (Mach.getuid() != 0)
        {
            try
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe)) { Console.Error.WriteLine("Cannot determine executable path."); return 1; }
                var quotedArgs = string.Join(" ", args.Select(a => a.Contains(' ') ? $"'{a}'" : a));
                var cmd = string.IsNullOrEmpty(quotedArgs) ? $"'{exe}'" : $"'{exe}' {quotedArgs}";
                var script = Path.Combine(Path.GetTempPath(), $"fi_elevate_{Environment.ProcessId}.sh");
                File.WriteAllText(script, $"#!/bin/bash\nexec {cmd}\n");
                Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = $"-e 'do shell script \"{script}\" with administrator privileges'",
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                try { File.Delete(script); } catch { }
                return proc?.ExitCode ?? 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Elevation failed: {ex.Message}");
                Console.Error.WriteLine("Run with: sudo dotnet run");
                return 1;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
