using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;

namespace FSB_helper_C__;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static bool IsDebugMode = false;
    private static Mutex _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        const string mutexName = "DuranHelperSingleInstanceMutex";
        bool createdNew;
        _mutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            var w = new AlreadyRunningWindow();
            w.ShowDialog();
            Environment.Exit(0);
            return;
        }

        DispatcherUnhandledException += (s, ex) =>
        {
            string fullMsg = $"ОШИБКА:\n{ex.Exception.GetType().Name}\n\n{ex.Exception.Message}";
            var inner = ex.Exception.InnerException;
            while (inner != null) {
                fullMsg += $"\n\n--- Inner: {inner.GetType().Name} ---\n{inner.Message}";
                inner = inner.InnerException;
            }
            fullMsg += $"\n\nStackTrace:\n{ex.Exception.StackTrace}";
            try { System.IO.File.WriteAllText("crash.txt", fullMsg); } catch { }
            System.Windows.MessageBox.Show(fullMsg, "Crash Diagnostic", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show(
                $"ФАТАЛЬНАЯ ОШИБКА:\n{((Exception)ex.ExceptionObject).Message}",
                "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }
}
