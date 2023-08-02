namespace Lanbridge;

public class TemperatureLogitTransform : ILogitTransform
{
    public TemperatureLogitTransform(double temperature)
    {
        Temperature = temperature;
    }

    public double Temperature { get; set; }
    public void Process(Memory<double> data, Memory<double> output, GenerationContext context)
    {
        var dataSpan = data.Span;
        var outputSpan = output.Span;
        for (int i = 0; i < data.Length; i++)
        {
            outputSpan[i] = (dataSpan[i] / Temperature) + 1e8;
        }
        
        TokenUtilities.Softmax(output, output);
    }
}