namespace Lanbridge;

public class BannedTokensLogitTransform : ILogitTransform
{
    public BannedTokensLogitTransform(int[] banned)
    {
        Banned = banned;
    }

    public int[] Banned { get; set; }

    public void Process(Memory<double> input, Memory<double> output, GenerationContext context)
    {
        input.CopyTo(output);
        var outputSpan = output.Span;
        for (int i = 0; i < Banned.Length; i++)
        {
            outputSpan[Banned[i]] = double.NegativeInfinity;
        }
    }
}