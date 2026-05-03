using System.Collections.Generic;

namespace TheDiscDb.Data.Import.Pipeline;

public class DataImportPipelineBuilder
{
    private IList<IMiddleware> middlewares = new List<IMiddleware>();

    public DataImportPipelineBuilder Use(IMiddleware middleware)
    {
        this.middlewares.Add(middleware);
        return this;
    }

    public DataImportPipeline Build()
    {
        var pipeline = new DataImportPipeline(this.middlewares);

        // The builder may be a singleton (see ImportPipelineExtensions.AddImportPipeline)
        // and reused across multiple Build calls. The pipeline takes its own copy of the
        // middleware list, so we reset ours here to avoid appending duplicates on the
        // next Build, which would otherwise process every item through each middleware
        // twice.
        this.middlewares = new List<IMiddleware>();

        return pipeline;
    }
}
