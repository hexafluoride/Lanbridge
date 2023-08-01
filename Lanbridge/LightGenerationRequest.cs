using System.Text.Json.Serialization;

namespace Lanbridge;

public class LightGenerationRequest
{
    [JsonPropertyName("type")]
    public string Type => "generate_light";
    
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("tokens")]
    public List<int> Tokens { get; set; } = new();
    
    [JsonPropertyName("use_past")]
    public int UsePast { get; set; }
    
    [JsonPropertyName("save_past")]
    public int SavePast { get; set; }
    
    [JsonPropertyName("trim_past")]
    public int TrimPast { get; set; }
    
    [JsonPropertyName("decode_method")]
    public string DecodeMethod { get; set; } = "additive";
    
    [JsonPropertyName("keep_warm")]
    public bool KeepWarm { get; set; }
}