using System.Windows;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace FocusDeck;

public partial class App : System.Windows.Application
{
    private static Mutex? singleInstanceMutex;
    private MainWindow? mainWindow;
    private CancellationTokenSource? pipeCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstanceMutex = new Mutex(true, "FocusDeck.Native.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            SendShowRequest();
            Shutdown();
            return;
        }

        mainWindow = new MainWindow();
        mainWindow.Loaded += async (_, _) => await UpdateChecker.CheckAsync(mainWindow);
        StartPipeServer();
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            mainWindow.ShowAndActivate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        pipeCancellation?.Cancel();
        singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void StartPipeServer()
    {
        pipeCancellation = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!pipeCancellation.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream("FocusDeck.Native.Pipe", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(pipeCancellation.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var message = await reader.ReadLineAsync(pipeCancellation.Token);
                    if (message == "show")
                    {
                        Dispatcher.Invoke(() => mainWindow?.ShowAndActivate());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        });
    }

    private static void SendShowRequest()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "FocusDeck.Native.Pipe", PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("show");
        }
        catch
        {
        }
    }
}
