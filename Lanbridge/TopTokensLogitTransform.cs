using System.Buffers;

namespace Lanbridge;

public class TopTokensLogitTransform : ILogitTransform
{
    public TopTokensLogitTransform(double topP, int topK)
    {
        TopP = topP;
        TopK = topK;
    }

    public double TopP { get; set; }
    public int TopK { get; set; }
    
    public void Process(Memory<double> data, Memory<double> output, GenerationContext context)
    {
        var indices = MemoryPool<int>.Shared.Rent(data.Length);
        var buffer = MemoryPool<double>.Shared.Rent(data.Length);
        var buffer2 = MemoryPool<double>.Shared.Rent(data.Length);
        var indicesMemory = indices.Memory[..data.Length];
        var bufferMemory = buffer.Memory[..data.Length];
        var buffer2Memory = buffer2.Memory[..data.Length];
        
        var indicesSpan = indicesMemory.Span;
        var bufferSpan = bufferMemory.Span;
        var buffer2Span = buffer2Memory.Span;
        
        var dataSpan = data.Span;
        var outputSpan = output.Span;
        
        for (int i = 0; i < data.Length; i++)
        {
            indicesSpan[i] = i;
            bufferSpan[i] = dataSpan[i];
            outputSpan[i] = double.NegativeInfinity;
        }
        
        bufferSpan.Sort(indicesSpan);
        indicesSpan.Reverse();

        if (TopK != 0)
        {
            for (int i = TopK; i < data.Length; i++)
            {
                bufferSpan[i] = Double.NegativeInfinity;
            }
        }

        TokenUtilities.Softmax(bufferMemory, buffer2Memory);

        // bufferSpan.Sort(indicesSpan);
        // indicesSpan.Reverse();

        double sum = 0d;
        for (int i = 0; i < data.Length; i++)
        {
            var index = indicesSpan[i];
            var value = buffer2Span[index];
            sum += value;
            outputSpan[index] = dataSpan[index];

            if (sum >= TopP)
            {
                break;
            }
        }
        
        indices.Dispose();
        buffer.Dispose();
        buffer2.Dispose();
    }
}