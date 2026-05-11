namespace Nuvio.Core.Validation;

public sealed class NuvioValidationException : Exception
{
    public NuvioValidationException(string message)
        : base(message)
    {
    }
}
