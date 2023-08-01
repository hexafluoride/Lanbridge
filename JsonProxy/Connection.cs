using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace JsonProxy;

public class Connection
{
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public TcpClient Client { get; set; }
    public NetworkStream NetworkStream { get; set; }
    public bool Dead { get; set; }
    public StreamReader StreamReader { get; set; }
    public StreamWriter StreamWriter { get; set; }

    public string RemoteEndPoint { get; set; }
    
    public Connection(TcpClient client)
    {
        client.NoDelay = true;
        RemoteEndPoint = (client.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? "(null)";
        Console.WriteLine($"New connection from {RemoteEndPoint}");
        Client = client;
        NetworkStream = client.GetStream();
        StreamReader = new StreamReader(NetworkStream);
        StreamWriter = new StreamWriter(NetworkStream);
        StreamWriter.AutoFlush = true;
    }

    public async Task<Message?> ConsumeRequest()
    {
        var line = await StreamReader.ReadLineAsync();
        if (line is null)
        {
            return null;
        }
        
        var deserialized = JsonSerializer.Deserialize<Message>(line, Program.JsonSerializerOptions);
        if (deserialized is not null)
        {
            deserialized.Source = this;
        }

        return deserialized;
    }

    public async Task SendResponse(Message message)
    {
        var serialized = JsonSerializer.Serialize(message, Program.JsonSerializerOptions);
        await StreamWriter.WriteLineAsync(serialized);
    }

    public async Task HandleAsync()
    {
        Message? request;
        while (!Program.Dead && (request = await ConsumeRequest()) is not null)
        {
            try
            {
                Console.WriteLine($"Serving request {request.Id} from {RemoteEndPoint} with method {request.Method}");
                switch (request.Method)
                {
                    case "submit":
                    {
                        Program.Inbound.Enqueue(request);
                        Program.CancellationTokenSource.Cancel();
                        await SendResponse(new Message()
                        {
                            Id = request.Id,
                            Method = "ok"
                        });
                        break;
                    }
                    case "submit_poll":
                    {
                        request.WakeOnCompletion = true;
                        Program.Inbound.Enqueue(request);
                        Program.CancellationTokenSource.Cancel();
                        await Task.Delay(-1, CancellationTokenSource.Token).ContinueWith(_ => { });
                        if (CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            CancellationTokenSource = new();
                        }
                        if (Program.Outbound.TryGetValue(request.Id, out JsonElement responseValue))
                        {
                            await SendResponse(new Message()
                            {
                                Id = request.Id,
                                Method = "result",
                                Body = responseValue
                            });
                            if (!Program.Outbound.TryRemove(
                                    new KeyValuePair<int, JsonElement>(request.Id, responseValue)))
                            {
                                Console.WriteLine(
                                    $"Failed to remove response with id {request.Id} from outbound queue");
                            }
                        }
                        else
                        {
                            await SendResponse(new Message()
                            {
                                Id = request.Id,
                                Method = "no"
                            });
                        }
                        break;
                    }
                    case "read":
                    {
                        if (Program.Outbound.TryGetValue(request.Id, out JsonElement responseValue))
                        {
                            await SendResponse(new Message()
                            {
                                Id = request.Id,
                                Method = "result",
                                Body = responseValue
                            });
                            if (!Program.Outbound.TryRemove(
                                    new KeyValuePair<int, JsonElement>(request.Id, responseValue)))
                            {
                                Console.WriteLine(
                                    $"Failed to remove response with id {request.Id} from outbound queue");
                            }
                        }
                        else
                        {
                            await SendResponse(new Message()
                            {
                                Id = request.Id,
                                Method = "no"
                            });
                        }

                        break;
                    }
                }

                Console.WriteLine($"Served request {request.Id} from {RemoteEndPoint} with method {request.Method}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                break;
            }
        }
        
        Dead = true;
        Console.WriteLine($"Connection {RemoteEndPoint} died");
        try
        {
            Client.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}