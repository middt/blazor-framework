using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Persistence;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Sample.Api;

/// <summary>
/// Seeds initial <c>customer-onboarding</c> workflow instances when the workflow database is empty so
/// <c>x-lookup</c> dropdowns and Server-Driven UI grids have initial JSON records out of the box.
/// </summary>
public sealed class DemoWorkflowSeeder(IServiceScopeFactory scopeFactory, ILogger<DemoWorkflowSeeder> logger) : IHostedService
{
    private static readonly WorkflowUser SeedUser = new()
    {
        Id = "test@test.com",
        Name = "test@test.com",
        Roles = ["manager", "idp-admin"],
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            if (await db.Instances.AsNoTracking().AnyAsync(i => i.WorkflowKey == "customer-onboarding", cancellationToken))
            {
                return;
            }

            var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

            await engine.StartAsync(
                "customer-onboarding",
                new StartWorkflowRequest
                {
                    Data = new JsonObject
                    {
                        ["fullName"] = "Ahmet Yılmaz",
                        ["identityNumber"] = "12345678901",
                        ["email"] = "ahmet.yilmaz@ornek.com",
                        ["phone"] = "05321234567",
                        ["segment"] = "Bireysel",
                        ["monthlyIncome"] = 85000,
                    },
                },
                SeedUser,
                cancellationToken);

            await engine.StartAsync(
                "customer-onboarding",
                new StartWorkflowRequest
                {
                    Data = new JsonObject
                    {
                        ["fullName"] = "Anadolu Lojistik A.Ş.",
                        ["identityNumber"] = "98765432109",
                        ["email"] = "finans@anadolulojistik.com",
                        ["phone"] = "02124440123",
                        ["segment"] = "Kurumsal",
                        ["monthlyIncome"] = 450000,
                    },
                },
                SeedUser,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Skipping initial demo workflow seed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
