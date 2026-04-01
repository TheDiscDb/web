using System;
using System.Collections.Generic;

namespace TheDiscDb.Naming;

public sealed class TemplateParseResult
{
    public bool IsSuccess { get; }
    public ParsedTemplate? Template { get; }
    public IReadOnlyList<TemplateParseError>? Errors { get; }

    private TemplateParseResult(ParsedTemplate template)
    {
        IsSuccess = true;
        Template = template;
    }

    private TemplateParseResult(IReadOnlyList<TemplateParseError> errors)
    {
        IsSuccess = false;
        Errors = errors;
    }

    public static TemplateParseResult Success(ParsedTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new(template);
    }

    public static TemplateParseResult Failure(IReadOnlyList<TemplateParseError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new(errors);
    }
}
