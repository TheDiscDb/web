using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

public class SingletonDbContextFactory : IDbContextFactory<SqlServerDataContext>
{
    private readonly SqlServerDataContext context;

    public SingletonDbContextFactory(SqlServerDataContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public SqlServerDataContext CreateDbContext()
    {
        return this.context;
    }
}
