using System.Buffers;

namespace Lanbridge;

public class LineLengthPenaltyLogitTransform : ILogitTransform
{
    public LineLengthPenaltyLogitTransform(int targetLineLength = 80, double penaltySeverity = 1)
    {
        TargetLineLength = targetLineLength;
        PenaltySeverity = penaltySeverity;
    }

    public int TargetLineLength { get; set; }
    public double PenaltySeverity { get; set; }
    
    public void Process(Memory<double> data, Memory<double> output, GenerationContext context)
    {
        var inputIds = context.InputTokens;
        var lineLength = 0;

        for (int i = inputIds.Count - 1; i >= 0; i--)
        {
            var token = context.TokenToText(inputIds[i]);
            if (token.Contains('\n'))
            {
                lineLength += token.Length - token.LastIndexOf('\n');
                break;
            }
            
            lineLength += token.Length;
        }
        
        var dataSpan = data.Span;
        var outputSpan = output.Span;

        var softmaxBuffer = MemoryPool<double>.Shared.Rent(data.Length);
        var softmaxMemory = softmaxBuffer.Memory[..data.Length];
        var softmaxSpan = softmaxMemory.Span;
        TokenUtilities.Softmax(data, softmaxMemory);

        var penalty = Math.Min(5d, lineLength / TargetLineLength);
        // penalty = 5 - penalty;

        var penalized = 0;
        var unpenalized = 0;
        
        for (int i = 0; i < data.Length; i++)
        {
            if (softmaxSpan[i] == 0)
            {
                outputSpan[i] = 0;
                continue;
            }

            var token = context.TokenToText(i);

            if (!token.Contains('\n'))
            {
                // penalize
                outputSpan[i] = lineLength > TargetLineLength ? double.NegativeInfinity : dataSpan[i] - (penalty * PenaltySeverity);
                penalized++;
            }
            else
            {
                // boost
                outputSpan[i] = lineLength > TargetLineLength ? 1 : dataSpan[i] + (penalty * PenaltySeverity);
                unpenalized++;
            }
        }
        
        softmaxBuffer.Dispose();
        Console.WriteLine($"Line length {lineLength}, penalty {penalty}, penalized {penalized} tokens, not penalized {unpenalized} tokens");
    }
}