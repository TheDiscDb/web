namespace TheDiscDb.Naming;

public sealed record TemplateParseError(string Message, int Position, int Length);
