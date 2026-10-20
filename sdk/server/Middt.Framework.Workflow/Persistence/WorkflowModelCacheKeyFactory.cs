using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Middt.Framework.Workflow.Persistence;

/// <summary>
/// EF Core caches a model per context type, but <see cref="WorkflowDbContext"/> builds its model from
/// <see cref="MiddtWorkflowOptions.Schema"/>. Without the schema in the key, a second workflow schema in the same process
/// (an API and Middt Identity hosted together, tests on several schemas) would silently read and write the first one's tables.
/// </summary>
internal sealed class WorkflowModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is WorkflowDbContext workflow
            ? (context.GetType(), workflow.ModelSchema, designTime)
            : (context.GetType(), designTime);
}
