using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Tally.Services;
using Tally.ViewModels;
using Tally.Views;

namespace Tally;

/// <summary>
/// Program startup: load settings, choose the data folder, load data, show the main window.
/// Saving after that is fully automatic (see DataStore) – nothing more to do here.
/// </summary>
public partial class App : Application
{
    private const string AppTitle = "Tally";
    private const string MutexName = @"Local\Tally.SingleInstance";

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // German number and date format everywhere – regardless of the Windows language
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;
        Thread.CurrentThread.CurrentCulture = german;
        Thread.CurrentThread.CurrentUICulture = german;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(german.IetfLanguageTag)));

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("Unbehandelter Fehler", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unbeobachteter Fehler in einer Hintergrundaufgabe", args.Exception);
            args.SetObserved();
        };

        // Only one instance: two program instances writing the same file would overwrite each other.
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!ActivateRunningInstance())
            {
                MessageBox.Show("Das Programm läuft bereits.", AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        // The program used to be called "PartyRechnungen" - take over its folders once.
        LegacyMigration.Run();

        var settings = AppSettings.Load();
        ThemeManager.Apply(settings.Theme);
        LocalizationManager.Apply(settings.Language);

        var folder = ChooseDataFolder(settings, out var folderNotice);
        if (folder == null)
        {
            Shutdown();
            return;
        }

        var store = new DataStore(folder, action => Dispatcher.InvokeAsync(action));

        LoadResult load;
        try
        {
            load = store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file exists but isn't readable right now (locked, no permission ...).
            // Then do NOT continue: otherwise the first save would overwrite it with an empty file.
            Log.Write("Data file not readable – program is shutting down", ex);
            store.Dispose();
            MessageBox.Show(
                "Die Datendatei kann gerade nicht gelesen werden:\n\n" + ex.Message +
                "\n\nOrdner: " + folder +
                "\n\nDamit nichts überschrieben wird, beendet sich das Programm. " +
                "Bitte prüfe, ob die Datei woanders geöffnet ist oder der Ordner erreichbar ist, und starte neu.",
                AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var dialogs = new WpfDialogService();
        var viewModel = new MainViewModel(dialogs, settings, store, load, action => Dispatcher.InvokeAsync(action));
        dialogs.ViewModel = viewModel;

        var window = new MainWindow(viewModel, settings);
        MainWindow = window;
        window.Show();

        // only show notices once the window is visible
        Dispatcher.InvokeAsync(() =>
        {
            if (folderNotice != null) dialogs.Info("Speicherordner", folderNotice);
            if (load.Notice != null) dialogs.Info("Hinweis", load.Notice);
        }, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_ownsMutex) _mutex?.ReleaseMutex();
        }
        catch
        {
            // doesn't matter – the program is exiting anyway
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ------------------------------------------------------------------ Storage folder

    /// <summary>
    /// Uses the saved folder (or Documents\Tally). If it's not writable
    /// (e.g. a USB stick isn't plugged in), a fallback folder is used temporarily – and the user is told.
    /// The setting itself stays unchanged; the desired folder is tried again on the next launch.
    /// </summary>
    private static string? ChooseDataFolder(AppSettings settings, out string? notice)
    {
        notice = null;

        var wanted = string.IsNullOrWhiteSpace(settings.DataFolder)
            ? AppSettings.DefaultDataFolder()
            : settings.DataFolder;

        var problem = DataStore.TestWritable(wanted);
        if (problem == null) return wanted;

        var fallback = AppSettings.FallbackDataFolder();
        var fallbackProblem = DataStore.TestWritable(fallback);
        if (fallbackProblem == null)
        {
            Log.Write($"Storage folder '{wanted}' not usable ({problem}) – using fallback folder '{fallback}'");
            notice =
                $"In den Speicherordner\n{wanted}\nkann gerade nicht geschrieben werden:\n{problem}\n\n" +
                $"Deshalb speichert das Programm vorübergehend hier:\n{fallback}\n\n" +
                "Deine Daten aus dem eigentlichen Ordner werden in dieser Sitzung NICHT angezeigt. " +
                "Wenn der Ordner wieder erreichbar ist, starte das Programm einfach neu.";
            return fallback;
        }

        Log.Write($"No usable storage folder: '{wanted}' ({problem}), '{fallback}' ({fallbackProblem})");
        MessageBox.Show(
            $"Es gibt keinen Ordner, in dem gespeichert werden kann.\n\n{wanted}\n{problem}\n\n{fallback}\n{fallbackProblem}",
            AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        return null;
    }

    // ------------------------------------------------------------------ Error handling

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unexpected error in the UI", e.Exception);

        try
        {
            MessageBox.Show(
                "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + e.Exception.Message +
                "\n\nDeine bisherigen Änderungen werden laufend automatisch gespeichert. " +
                "Tritt der Fehler öfter auf, starte das Programm neu.\n\n" +
                "Details stehen in der Datei log.txt im Ordner\n" + AppSettings.SettingsDirectory,
                AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // if even showing the message fails, at least keep running
        }

        e.Handled = true;

        // If something already went wrong at startup (window never became visible), an invisible
        // program would otherwise keep running and block the file – better to shut down cleanly instead.
        if (MainWindow is not { IsVisible: true }) Shutdown(1);
    }

    // ------------------------------------------------------------------ Second instance -> bring the first one forward

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SwRestore = 9;

    private static bool ActivateRunningInstance()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var others = Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id).ToArray();
            try
            {
                var handle = others.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
                if (handle == IntPtr.Zero) return false;

                if (IsIconic(handle)) ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
                return true;
            }
            finally
            {
                foreach (var p in others) p.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }
}
