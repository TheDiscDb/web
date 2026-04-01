namespace TheDiscDb.Naming;

public abstract record TemplateSegment;

public sealed record LiteralSegment(string Text) : TemplateSegment;

public sealed record TokenSegment(string TokenName, int Position) : TemplateSegment;
