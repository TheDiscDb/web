namespace TheDiscDb.GraphQL.Contribute.Exceptions;

public sealed class IntakeDiscMatchNotFoundException : Exception
{
    public IntakeDiscMatchNotFoundException()
        : base("The matching intake disc evidence is no longer available.")
    {
    }
}
