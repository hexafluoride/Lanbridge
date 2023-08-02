using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Lanbridge;

namespace Client;

public class Program
{
    public static string[] Tokens { get; set; }

    public static async Task Main(string[] args)
    {
        var host = "172.16.0.102";
        var port = 8952;

        var instance = new NetworkedGptInstance(host, port);
        Tokens = instance.Tokens = NetworkedGptInstance.GetTokens(instance);

        // Find longest tokens in tokenizer
        var tokensCopy = new string[32000];
        Array.Copy(Tokens, tokensCopy, Tokens.Length);
        var indices = new int[32000];
        var lengths = new int[32000];

        for (int i = 0; i < 32000; i++)
        {
            indices[i] = i;
            lengths[i] = Tokens[i].Length;
        }

        Array.Sort(lengths, indices);
        Array.Reverse(indices);
        for (int i = 0; i < 10; i++)
        {
            var token = indices[i];
            Console.WriteLine($"Token {token}: {Tokens[token]}");
        }

        // Some examples
        RunAgeExample(instance, "No one is going to wear hardware on their face for hours. People don’t even like wearing glasses on their face and they’re lighter than ever.");
        RunTranslateExample(instance, "French", "Spanish", "Je suis actuellement à 30 000 pieds au moment où je tape ceci");

        var transforms = new List<ILogitTransform>()
        {
            new BannedTokensLogitTransform(new[] {1, 2}),
            new TopTokensLogitTransform(0.95, 50),
            new TemperatureLogitTransform(0.32),
            new RepetitionPenaltyLogitTransform(1.2)
        };
        
        for (int i = 25; i <= 250; i += 25)
        {
            RunSampledGeneration(instance, transforms, i, "I saw a girl with a telescope.");
        }
    }

    public static void RunTranslateExample(NetworkedGptInstance instance, string sourceLang, string targetLang,
        string text)
    {
        string prompt =
            $"I am an excellent translator. I translate text from {sourceLang} to {targetLang} for a living. I am lauded " +
            $"for my accuracy, professionalism, and excellent performance. I have a translation job:\n" +
            $"Translate the following text from {sourceLang} to {targetLang}. Make sure both lines mean the same thing, to the greatest extent possible.\n" +
            $"\n" +
            $"{sourceLang}: \"{text}\"\n" +
            $"{targetLang}: \"";
        
        var generationContext = new GenerationContext(instance);
        generationContext.InputTokens = generationContext.Tokenize(prompt);
        generationContext.LogitTransforms = new ()
        {
            new TopTokensLogitTransform(0.95, 50),
            new TemperatureLogitTransform(0.3),
            new RepetitionPenaltyLogitTransform(1.05)
        };
        
        using var buf = MemoryPool<double>.Shared.Rent(32000);
        var memory = buf.Memory[..32000];
        var maxTokenCount = 500;

        var translatedTokens = new List<int>();
        
        for (int i = 0; i < maxTokenCount; i++)
        {
            generationContext.CalculateLogits(memory);
            generationContext.ProcessLogits(memory, memory);
            var sampled = TokenUtilities.SampleLogit(memory);
            generationContext.CommitToken(sampled);
            translatedTokens.Add(sampled);

            if (sampled == 2) // end of sequence token
            {
                break;
            }

            if (Tokens[sampled].Contains('"')) // end of quote
            {
                break;
            }
        }
        
        Console.WriteLine($"Result: {generationContext.Decode(translatedTokens)}");
    }

    public static void RunSampledGeneration(NetworkedGptInstance instance, List<ILogitTransform> transforms,
        int tokenCount, string prompt)
    {
        var generationContext = new GenerationContext(instance);
        generationContext.UniqueId = Process.GetCurrentProcess().Id;
        // generationContext.UniqueId = Random.Shared.Next() + 1;
        generationContext.InputTokens = generationContext.Tokenize(prompt);
        generationContext.LogitTransforms = transforms;
        using var buf = MemoryPool<double>.Shared.Rent(32000);
        var memory = buf.Memory[..32000];

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < tokenCount; i++)
        {
            generationContext.CalculateLogits(memory, keepWarm: i < tokenCount - 1);
            generationContext.ProcessLogits(memory, memory);
            var sampled = TokenUtilities.SampleLogit(memory);
            // Console.WriteLine($"Sampled: {sampled} \"{Tokens[sampled]}\"");
            generationContext.CommitToken(sampled);
            // Console.WriteLine($"Current string: {generationContext.Decode(generationContext.InputTokens)}");
        }

        sw.Stop();
        Console.WriteLine($"Result: {generationContext.Decode(generationContext.InputTokens)}");
        Console.WriteLine($"Generated {tokenCount} tokens in {sw.Elapsed.TotalSeconds:0.00}s, with {generationContext.CumulativeGpuTime:0.00}s GPU time, for an overhead of " +
                          $"{1 - (generationContext.CumulativeGpuTime / sw.Elapsed.TotalSeconds):P}");
    }
    
    public static void RunAgeExample(NetworkedGptInstance instance, string text)
    {
        string startText = "Here is a social media post: \n" +
                    $"\"{text}\"\n" +
                    "The age of this user is estimated to be: ";
    
        var generationContext = new GenerationContext(instance);
        generationContext.InputTokens = generationContext.Tokenize(startText);

        var bannedTokens = new List<int>();

        var digits = new int[10];
        for (int i = 0; i < 10; i++)
        {
            digits[i] = -1;
        }
        
        for (int i = 0; i < Tokens.Length; i++)
        {
            if (Tokens[i].Length == 1 && char.IsDigit(Tokens[i][0]) && char.IsAscii(Tokens[i][0]) && i > 100)
            {
                Console.WriteLine($"Allowing token {i}: \"{Tokens[i]}\"");
                digits[int.Parse(Tokens[i])] = i;
            }
            else if (i != 1 && i != 2)
            {
                bannedTokens.Add(i);
            }
        }
        
        generationContext.LogitTransforms = new ()
        {
            new BannedTokensLogitTransform(bannedTokens.ToArray()),
        };
        
        using var buf = MemoryPool<double>.Shared.Rent(32000);
        var memory = buf.Memory[..32000];
        
        generationContext.CalculateLogits(memory);
        generationContext.ProcessLogits(memory, memory);

        double[] perAgeScores = new double[100];
        double[] firstDigitScores = new double[10];
        var tempArr = new double[32000];

        for (int i = 1; i < 10; i++)
        {
            var digitToken = digits[i];
            firstDigitScores[i] = memory.Span[digitToken];

            var currentTrim = generationContext.InputTokens.Count;
            generationContext.InputTokens.Add(digitToken);
            generationContext.CalculateLogits(memory, currentTrim);
            var nonDigitScores = 0d;
            TokenUtilities.Softmax(memory, tempArr);

            for (int j = 0; j < 32000; j++)
            {
                if (digits.Contains(j))
                    continue;
                nonDigitScores += tempArr[j];
            }
            
            generationContext.ProcessLogits(memory, memory);
            generationContext.InputTokens.RemoveAt(generationContext.InputTokens.Count - 1);

            for (int j = 0; j < 10; j++)
            {
                var nextDigitScore = memory.Span[digits[j]];
                var predictedAge = i * 10 + j;
                perAgeScores[predictedAge] = firstDigitScores[i] * nextDigitScore;
            }
            
            perAgeScores[i] = firstDigitScores[i] * nonDigitScores;
        }

        for (int i = 0; i < perAgeScores.Length; i++)
        {
            Console.WriteLine($"Chance of age {i,3}: {perAgeScores[i]:P}");
        }
        for (int i = 1; i < perAgeScores.Length; i++)
        {
            Console.WriteLine($"{i},{perAgeScores[i] * 100.0:0.000000000}");
        }
    }

    public static void Ping(NetworkedGptInstance instance, int n = 10)
    {
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < n; i++)
        {
            var requestId = Random.Shared.Next();
            var ping = JsonSerializer.SerializeToElement(new
            {
                id = requestId,
                type = "ping",
                ticks = stopwatch.ElapsedTicks
            });

            var responseMessage = instance.SubmitAndWait(new NetworkedGptInstance.RequestBundle(ping, requestId));
            var response = responseMessage?.Body ?? throw new Exception();

            var ticksReturned = response.GetProperty("result").GetInt64();
            var ticksNow = stopwatch.ElapsedTicks;
            var ticksDiff = ticksNow - ticksReturned;
            var msDiff = (ticksDiff * 1000d) / Stopwatch.Frequency;

            Console.WriteLine(
                $"roundtrip took {msDiff:0.00}ms (received at {ticksNow}, sent at {ticksReturned}, {ticksDiff})");
        }
    }
}