namespace Lanbridge;

public class RepetitionPenaltyLogitTransform : ILogitTransform
{
    public RepetitionPenaltyLogitTransform(double repetitionPenalty)
    {
        RepetitionPenalty = repetitionPenalty;
    }

    public double RepetitionPenalty { get; set; }
    
    public void Process(Memory<double> data, Memory<double> output, GenerationContext context)
    {
        data.CopyTo(output);
        var outputSpan = output.Span;
        for (int i = 0; i < context.InputTokens.Count; i++)
        {
            var token = context.InputTokens[i];
            outputSpan[token] /= RepetitionPenalty;
        }
    }
}