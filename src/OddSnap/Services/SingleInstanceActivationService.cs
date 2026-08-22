using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace OddSnap.Services;

public enum SingleInstanceActivationRequest
{
    OpenSettings,
    OpenHistory,
}

public static class SingleInstanceActivationService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static CancellationTokenSource? _cts;
    private static Task? _serverTask;

    private static string PipeName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var safeSid = new string(sid.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            return $"OddSnapScreenshotTool_Activation_{safeSid}";
        }
    }

    public static void Start(Action<SingleInstanceActivationRequest> onActivated)
    {
        lock (Gate)
        {
            if (_serverTask is { IsCompleted: false })
                return;

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerAsync(onActivated, _cts.Token));
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _serverTask = null;
        }
    }

    public static bool TryActivateExisting(SingleInstanceActivationRequest request, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            client.ConnectAsync((int)Math.Max(1, timeout.TotalMilliseconds)).GetAwaiter().GetResult();
            using var writer = new StreamWriter(client, Utf8NoBom, bufferSize: 128, leaveOpen: false)
            {
                AutoFlush = true
            };
            writer.WriteLine(ToMessage(request));
            return true;
        }
        catch (TimeoutException ex)
        {
            AppDiagnostics.LogWarning("single-instance.activate-timeout", ex.Message, ex);
            return false;
        }
        catch (IOException ex)
        {
            AppDiagnostics.LogWarning("single-instance.activate-io", ex.Message, ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppDiagnostics.LogWarning("single-instance.activate-access", ex.Message, ex);
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("single-instance.activate", ex);
            return false;
        }
    }

    private static async Task RunServerAsync(Action<SingleInstanceActivationRequest> onActivated, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 128, leaveOpen: false);
                var message = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (TryParse(message, out var request))
                    onActivated(request);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("single-instance.server", ex);
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
    }

    private static string ToMessage(SingleInstanceActivationRequest request) => request switch
    {
        SingleInstanceActivationRequest.OpenSettings => "open-settings",
        SingleInstanceActivationRequest.OpenHistory => "open-history",
        _ => "open-settings",
    };

    private static bool TryParse(string? message, out SingleInstanceActivationRequest request)
    {
        if (string.Equals(message, "open-history", StringComparison.OrdinalIgnoreCase))
        {
            request = SingleInstanceActivationRequest.OpenHistory;
            return true;
        }

        request = SingleInstanceActivationRequest.OpenSettings;
        return string.Equals(message, "open-settings", StringComparison.OrdinalIgnoreCase);
    }
}
