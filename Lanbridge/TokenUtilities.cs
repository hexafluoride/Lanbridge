using System.Buffers;

namespace Lanbridge;

public static class TokenUtilities
{
    public static void Softmax(Memory<double> data, Memory<double> output)
    {
        var sum = 0d;
        var dataSpan = data.Span;
        var outputSpan = output.Span;
        for (int i = 0; i < data.Length; i++)
        {
            var exp = Math.Exp(dataSpan[i]);
            outputSpan[i] = exp;
            sum += exp;
        }

        for (int i = 0; i < data.Length; i++)
        {
            outputSpan[i] /= sum;
        }
    }
    
    public static int SampleLogit(Memory<double> scores)
    {
        using var indices = MemoryPool<int>.Shared.Rent(scores.Length);
        using var buffer = MemoryPool<double>.Shared.Rent(scores.Length);
        
        var indicesMemory = indices.Memory[..scores.Length];
        var bufferMemory = buffer.Memory[..scores.Length];

        var indicesSpan = indicesMemory.Span;
        var bufferSpan = bufferMemory.Span;
        
        for (int i = 0; i < scores.Length; i++)
        {
            indicesSpan[i] = i;
            bufferSpan[i] = scores.Span[i];
        }

        bufferSpan.Sort(indicesSpan);
        indicesSpan.Reverse();
        
        var sampleLocation = Random.Shared.NextDouble();
        
        double sum = 0d;
        for (int i = 0; i < scores.Length; i++)
        {
            var index = indicesSpan[i];
            var value = scores.Span[index];
            sum += value;

            if (sum >= sampleLocation)
            {
                return index;
            }
        }

        throw new Exception($"Could not sample");
    }
}