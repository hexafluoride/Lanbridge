using System.Net.Sockets;
using System.Text.Json;

namespace Lanbridge;

public class NetworkedGptInstance
{
    public record RequestBundle(JsonElement Body, int Id)
    {
        public int Id { get; set; } = Id;
        public JsonElement Body { get; set; } = Body;
    }
    
    public class Message
    {
        public JsonElement? Body { get; set; }
        public int Id { get; set; }
        public string? Method { get; set; }
    }
    
    public bool ModelDead { get; private set; }
    
    private TcpClient Connection { get; set; }
    private StreamReader Reader { get; set; }
    private StreamWriter Writer { get; set; }

    public string[] Tokens { get; set; } = new string[32000];

    public static JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    
    public NetworkedGptInstance(string host, int port)
    {
        Connection = new(host, port);
        Connection.NoDelay = true;
        var stream = Connection.GetStream();
        Reader = new StreamReader(stream);
        Writer = new StreamWriter(stream) {AutoFlush = true};
    }

    public Message? ConsumeMessage()
    {
        if (!Connection.Connected)
        {
            Kill();
        }

        if (ModelDead)
        {
            return null;
        }
        
        var line = Reader.ReadLine();
        if (line is null)
        {
            return null;
        }
        return JsonSerializer.Deserialize<Message>(line, JsonSerializerOptions);
    }

    public void SendMessage(Message message)
    {
        if (!Connection.Connected)
        {
            Kill();
        }

        if (ModelDead)
        {
            return;
        }
        
        var serialized = JsonSerializer.Serialize(message, JsonSerializerOptions);
        Writer.WriteLine(serialized);
    }

    public int SubmitRequest(RequestBundle bundle)
    {
        var id = bundle.Id;
        var body = bundle.Body;
        var request = new Message()
        {
            Method = "submit",
            Id = id,
            Body = body
        };
        
        SendMessage(request);
        var response = ConsumeMessage() ?? throw new Exception("Failed to consume reply to request submission");
        if (response.Id != id)
        {
            throw new Exception($"Expected reply to {id}, got {response.Id}");
        }

        if (response.Method != "ok")
        {
            throw new Exception($"Expected \"ok\", got {response.Method}");
        }

        return id;
    }

    public Message SubmitAndWait(RequestBundle bundle)
    {
        var id = bundle.Id;
        var body = bundle.Body;
        var request = new Message()
        {
            Method = "submit_poll",
            Id = id,
            Body = body
        };
        
        SendMessage(request);
        var response = ConsumeMessage() ?? throw new Exception("Failed to consume reply to request submission");
        if (response.Id != id)
        {
            throw new Exception($"Expected reply to {id}, got {response.Id}");
        }

        if (response.Method == "no")
        {
            throw new Exception($"Received error");
        }

        if (response.Method != "result")
        {
            throw new Exception($"Expected \"result\" or \"no\", got {response.Method}");
        }

        return response;
    }

    public RequestBundle RequestTokenize(string text)
    {
        var id = Random.Shared.Next();
        var request = JsonSerializer.SerializeToElement(new
        {
            type = "tokenize",
            id,
            text
        }, JsonSerializerOptions);

        return new RequestBundle(request, id);
    }
    
    public RequestBundle RequestTokenizerDump()
    {
        var id = Random.Shared.Next();
        var request = JsonSerializer.SerializeToElement(new
        {
            type = "dump_tokenizer",
            id
        }, JsonSerializerOptions);

        return new RequestBundle(request, id);
    }

    public RequestBundle RequestGeneration(LightGenerationRequest request)
    {
        var id = Random.Shared.Next();
        request.Id = id;
        var requestEncoded = JsonSerializer.SerializeToElement(request, JsonSerializerOptions);

        return new RequestBundle(requestEncoded, id);
    }

    public Message? PollForResponse(int requestId, bool wait)
    {
        var pollRequest = new Message()
        {
            Method = "read",
            Id = requestId
        };
        
        Message? nextResponse = null;

        do
        {
            if (nextResponse is not null)
            {
                Thread.Sleep(100);
            }

            SendMessage(pollRequest);
            nextResponse = ConsumeMessage() ?? throw new Exception($"Could not read reply to poll");

            if (nextResponse.Id != requestId)
            {
                throw new Exception($"Expected reply to {requestId}, got {nextResponse.Id}");
            }
        } while (wait && nextResponse.Method == "no" && !ModelDead);
        
        if (nextResponse.Method == "no")
        {
            return null;
        }
        else if (nextResponse.Method != "result")
        {
            throw new Exception($"Expected \"result\" or \"no\", got {nextResponse.Method}");
        }

        return nextResponse;
    }

    public void Kill()
    {
        Connection.Close();
        ModelDead = true;
    }

    public static string[] GetTokens(NetworkedGptInstance instance)
    {
        var request = instance.RequestTokenizerDump();
        var result = instance.SubmitAndWait(request);
        return result.Body?.GetProperty("result")
            .GetProperty("tokens")
            .EnumerateArray()
            .Select(l => l.GetString() ?? throw new Exception())
            .ToArray() ?? throw new Exception();
    }
}