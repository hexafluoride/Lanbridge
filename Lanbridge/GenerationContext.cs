using System.Buffers;
using System.Text.Json;

namespace Lanbridge;

public class GenerationContext
{
    private NetworkedGptInstance GptInstance;
    public int UniqueId;
    
    public List<int> InputTokens { get; set; } = new();
    public List<ILogitTransform> LogitTransforms { get; set; } = new();

    public string TokenToText(int tokenId) => GptInstance.Tokens[tokenId];
    public string Decode(IEnumerable<int> tokens) => string.Join("", tokens.Select(TokenToText));
    public double CumulativeGpuTime;
    
    public GenerationContext(NetworkedGptInstance gptInstance)
    {
        GptInstance = gptInstance;
        UniqueId = Random.Shared.Next();
    }

    public List<int> Tokenize(string text)
    {
        var request = GptInstance.RequestTokenize(text);
        var decodeResponse = GptInstance.SubmitAndWait(request).Body ?? throw new Exception("Could not read response");
        var startTokensText = decodeResponse.GetProperty("result").GetProperty("response_decoded").GetString() ?? throw new Exception("Could not decode response");
        var tokens = startTokensText.Trim('[',']').Split(',').Select(n => int.Parse(n)).ToList();
        tokens.Insert(0, 1);
        return tokens;
    }

    public void CalculateLogits(Memory<double> output, int trimPast = -1, bool keepWarm = true)
    {
        var request = new LightGenerationRequest()
        {
            Tokens = InputTokens,
            SavePast = UniqueId,
            UsePast = UniqueId,
            KeepWarm = keepWarm
        };

        if (trimPast >= 0)
        {
            request.TrimPast = trimPast;
        }

        var requestBundle = GptInstance.RequestGeneration(request);
        var resultMessage = GptInstance.SubmitAndWait(requestBundle);
        var resultObject = resultMessage?.Body?.GetProperty("result") ?? throw new Exception();
        var timingsProperty = resultObject.GetProperty("timings");

        JsonElement tempJsonElement;
        var timeDone = timingsProperty.TryGetProperty("done", out tempJsonElement) ? tempJsonElement.GetDouble() : -1;
        var timeLogitsReady = timingsProperty.TryGetProperty("logits_ready", out tempJsonElement) ? tempJsonElement.GetDouble() : -1;
        var timeStart = timingsProperty.TryGetProperty("start", out tempJsonElement) ? tempJsonElement.GetDouble() : -1;
        var timeResumeStart = timingsProperty.TryGetProperty("resume_start", out tempJsonElement) ? tempJsonElement.GetDouble() : -1;

        var modelTimeSpent = timeDone - timeStart;
        var modelTimeSpentCore = timeLogitsReady - timeResumeStart;
        CumulativeGpuTime += modelTimeSpentCore;

        var logitsProperty = resultObject.GetProperty("logits");

        if (logitsProperty.ValueKind == JsonValueKind.String)
        {
            if (!logitsProperty.TryGetBytesFromBase64(out byte[]? logitBytes))
            {
                throw new Exception("Failed to decode base64");
            }
            
            var floatLength = logitBytes.Length / 32000;
            if (floatLength != 4)
            {
                throw new Exception($"Score blob was {logitBytes.Length} bytes for vocab size of {32000}, expected 4 bytes for score, got {floatLength}");
            }

            var tokenIndex = 0;
            for (int j = 0; j < logitBytes.Length; j += floatLength)
            {
                var score = BitConverter.ToSingle(logitBytes, j);
                output.Span[tokenIndex] = (double) score;
                tokenIndex++;
            }
        }
        else if (logitsProperty.ValueKind == JsonValueKind.Array)
        {
            throw new Exception();
        }
    }
    
    public void ProcessLogits(Memory<double> logitsIn, Memory<double> scoresOut)
    {
        using var buffer1 = MemoryPool<double>.Shared.Rent(logitsIn.Length);
        using var buffer2 = MemoryPool<double>.Shared.Rent(logitsIn.Length);
        
        var buffer1Memory = buffer1.Memory[..logitsIn.Length];
        var buffer2Memory = buffer2.Memory[..logitsIn.Length];

        logitsIn.CopyTo(buffer1Memory);

        foreach (var transform in LogitTransforms)
        {
            transform.Process(buffer1Memory, buffer2Memory, this);
            buffer2Memory.CopyTo(buffer1Memory);
        }
        
        TokenUtilities.Softmax(buffer1Memory, scoresOut);
    }

    public void CommitToken(int token)
    {
        InputTokens.Add(token);
    }
}