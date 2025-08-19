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
        return new DataImportPipeline(this.middlewares);
    }
}
