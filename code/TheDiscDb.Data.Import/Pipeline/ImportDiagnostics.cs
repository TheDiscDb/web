namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ImportDiagnostics
{
    public IList<ImportStageTiming> Stages { get; } = new List<ImportStageTiming>();
    public bool Failed { get; set; }
    public Exception? FailureException { get; set; }
    public ISet<string> FailedStages { get; } = new HashSet<string>(StringComparer.Ordinal);
    public int IndexedDocuments { get; set; }
    public int RefreshedSiblingDocuments { get; set; }
    internal TimeSpan MeasuredDuration => TimeSpan.FromTicks(this.Stages.Sum(stage => stage.Duration.Ticks));
}

public sealed record ImportStageTiming(string Name, TimeSpan Duration, bool Succeeded);
