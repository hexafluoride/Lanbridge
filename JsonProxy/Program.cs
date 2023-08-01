using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JsonProxy;

public class Program
{
    public static ConcurrentDictionary<int, JsonElement> Outbound = new();
    public static ConcurrentQueue<Message?> Inbound = new();

    public static bool Dead;
    public static CancellationTokenSource CancellationTokenSource = new();

    public static JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task Main(string[] args)
    {
        var scriptPath = args[0];
        if (!File.Exists(scriptPath))
        {
            return;
        }

        var psi = new ProcessStartInfo(scriptPath);

        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        
        var process = Process.Start(psi) ?? throw new Exception("Could not start process");
        Console.WriteLine($"Started process, waiting for readiness");
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.Trim() == "ready")
            {
                Console.WriteLine($"Process marked ready");
                break;
            }
            else
            {
                Console.WriteLine($"Process wrote \"{line}\" ({Convert.ToBase64String(Encoding.UTF8.GetBytes(line))})");
            }
        }
#pragma warning disable CS4014
        HandleProcessCommunications(process).ContinueWith(_ =>
        {
            Console.WriteLine($"Process died, broadcasting dead");
            Dead = true;
            Environment.Exit(1);
        });
#pragma warning restore CS4014

        var host = "0.0.0.0";
        var port = 8952;
        var listener = new TcpListener(IPAddress.Parse(host), port);
        
        listener.Start();
        Console.WriteLine($"Started listening on {host}:{port}");

        TcpClient? client;
        while ((client = await listener.AcceptTcpClientAsync()) is not null)
        {
            Console.WriteLine($"Accepted client");
            var connection = new Connection(client);

#pragma warning disable CS4014
            connection.HandleAsync().ContinueWith(t => {
#pragma warning restore CS4014
                try
                {
                    connection.Client.Close();
                }
                catch
                {
                }
            });
        }
    }

    public static async Task HandleProcessCommunications(Process process)
    {
        var stdin = process.StandardInput;
        var stdout = process.StandardOutput;
        stdin.AutoFlush = true;
        int pending = 0;
        Dictionary<int, Message> localRequests = new(); 

        while (!process.HasExited)
        {
            if (pending > 0)
            {
                var line = await stdout.ReadLineAsync() ?? throw new Exception();
                var message = JsonDocument.Parse(line);
                var id = message.RootElement.GetProperty("id").GetInt32();
                var innerRequest = localRequests[id];
                localRequests.Remove(id);

                Outbound.GetOrAdd(id, message.RootElement);
                if (innerRequest.WakeOnCompletion)
                {
                    innerRequest.Source.CancellationTokenSource.Cancel();
                    await Task.Yield();
                }
                pending--;
                await Task.Yield();
            }
            else if (pending == 0)
            {
                var token = CancellationTokenSource.Token;
                await Task.Delay(5000, token).ContinueWith(t => t);

                if (token.IsCancellationRequested)
                {
                    Console.WriteLine($"Cancellation was requested");
                    CancellationTokenSource = new();
                    Console.WriteLine($"Cancellation was reset");
                }
            }

            if (Inbound.TryDequeue(out Message? request) && request is not null)
            {
                localRequests[request.Id] = request;
                var text = request!.Body!.Value.GetRawText().ReplaceLineEndings("");
                await stdin.WriteLineAsync(text);
                pending++;
            }
        }
    }
}