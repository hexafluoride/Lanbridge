namespace Lanbridge;

public interface ILogitTransform
{
    public void Process(Memory<double> input, Memory<double> output, GenerationContext context);
}