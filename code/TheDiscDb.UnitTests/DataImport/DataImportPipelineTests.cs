using TheDiscDb.Data.Import.Pipeline;

namespace TheDiscDb.UnitTests.DataImport;

public class DataImportPipelineTests
{
    [Test]
    public async Task Pipeline_WithNoMiddleware_DoesNotThrow()
    {
        var builder = new DataImportPipelineBuilder();
        await using var pipeline = builder.Build();

        var item = new ImportItem();
        await pipeline.ProcessItem(item);
    }

    [Test]
    public async Task Pipeline_SingleMiddleware_ProcessesCalled()
    {
        var middleware = new TrackingMiddleware();
        var builder = new DataImportPipelineBuilder();
        builder.Use(middleware);
        await using var pipeline = builder.Build();

        var item = new ImportItem();
        await pipeline.ProcessItem(item);

        await Assert.That(middleware.WasCalled).IsTrue();
    }

    [Test]
    public async Task Pipeline_MultipleMiddleware_ExecutesInOrder()
    {
        var callOrder = new List<string>();

        var first = new OrderTrackingMiddleware("first", callOrder);
        var second = new OrderTrackingMiddleware("second", callOrder);
        var third = new OrderTrackingMiddleware("third", callOrder);

        var builder = new DataImportPipelineBuilder();
        builder.Use(first).Use(second).Use(third);
        await using var pipeline = builder.Build();

        await pipeline.ProcessItem(new ImportItem());

        await Assert.That(callOrder).Count().IsEqualTo(3);
        await Assert.That(callOrder[0]).IsEqualTo("first");
        await Assert.That(callOrder[1]).IsEqualTo("second");
        await Assert.That(callOrder[2]).IsEqualTo("third");
    }

    [Test]
    public async Task Pipeline_MiddlewareCanShortCircuit()
    {
        var callOrder = new List<string>();
        var first = new OrderTrackingMiddleware("first", callOrder, shortCircuit: true);
        var second = new OrderTrackingMiddleware("second", callOrder);

        var builder = new DataImportPipelineBuilder();
        builder.Use(first).Use(second);
        await using var pipeline = builder.Build();

        await pipeline.ProcessItem(new ImportItem());

        await Assert.That(callOrder).Count().IsEqualTo(1);
        await Assert.That(callOrder[0]).IsEqualTo("first");
    }

    [Test]
    public async Task Pipeline_Builder_ReturnsSelf_ForFluent()
    {
        var builder = new DataImportPipelineBuilder();
        var returnValue = builder.Use(new TrackingMiddleware());

        await Assert.That(returnValue).IsSameReferenceAs(builder);
    }

    private class TrackingMiddleware : IMiddleware
    {
        public bool WasCalled { get; private set; }
        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            WasCalled = true;
            await Next(item, cancellationToken);
        }
    }

    private class OrderTrackingMiddleware : IMiddleware
    {
        private readonly string name;
        private readonly List<string> callOrder;
        private readonly bool shortCircuit;

        public OrderTrackingMiddleware(string name, List<string> callOrder, bool shortCircuit = false)
        {
            this.name = name;
            this.callOrder = callOrder;
            this.shortCircuit = shortCircuit;
        }

        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            callOrder.Add(name);
            if (!shortCircuit)
            {
                await Next(item, cancellationToken);
            }
        }
    }
}
