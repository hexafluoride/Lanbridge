using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsonProxy;

public class Message
{
    [JsonIgnore]
    public Connection Source { get; set; }
    [JsonIgnore]
    public bool WakeOnCompletion { get; set; }
    public JsonElement? Body { get; set; }
    public int Id { get; set; }
    public string Method { get; set; }
}