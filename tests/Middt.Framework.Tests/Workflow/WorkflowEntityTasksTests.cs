using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

public sealed class TestEntityDbContext(DbContextOptions<TestEntityDbContext> options) : DbContext(options)
{
    public DbSet<TestCustomerEntity> Customers => Set<TestCustomerEntity>();
}

public sealed class TestCustomerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; }

    [System.ComponentModel.ReadOnly(true)]
    public string? InternalNote { get; set; }
}

public sealed class WorkflowEntityTasksTests
{
    private const string CreateWorkflow = """
        {
          "key": "create-customer-wf",
          "start": { "key": "start", "target": "creating", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "creating",
              "onEntry": [
                {
                  "task": "entity-create",
                  "config": {
                    "entity": "TestCustomerEntity",
                    "values": {
                      "Name": "{{ data.name }}",
                      "Email": "{{ data.email }}",
                      "IsActive": true
                    },
                    "into": "customerId",
                    "uniqueBy": ["Email"]
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string UpdateWorkflow = """
        {
          "key": "update-customer-wf",
          "start": { "key": "start", "target": "updating", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "updating",
              "onEntry": [
                {
                  "task": "entity-update",
                  "config": {
                    "entity": "TestCustomerEntity",
                    "key": "{{ data.customerId }}",
                    "values": {
                      "Name": "{{ data.newName }}",
                      "IsActive": false
                    }
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string LoadWorkflow = """
        {
          "key": "load-customer-wf",
          "start": { "key": "start", "target": "loading", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "loading",
              "onEntry": [
                {
                  "task": "entity-load",
                  "config": {
                    "entity": "TestCustomerEntity",
                    "key": "{{ data.customerId }}",
                    "into": "customer"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string DeleteWorkflow = """
        {
          "key": "delete-customer-wf",
          "start": { "key": "start", "target": "deleting", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "deleting",
              "onEntry": [
                {
                  "task": "entity-delete",
                  "config": {
                    "entity": "TestCustomerEntity",
                    "key": "{{ data.customerId }}"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string DbSqlWorkflow = """
        {
          "key": "db-sql-wf",
          "start": { "key": "start", "target": "executing", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "executing",
              "onEntry": [
                {
                  "task": "db-sql",
                  "config": {
                    "sql": "UPDATE Customers SET IsActive = 1 WHERE Id = {0}",
                    "parameters": ["{{ data.customerId }}"],
                    "into": "affected"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string DbQueryWorkflow = """
        {
          "key": "db-query-wf",
          "start": { "key": "start", "target": "querying", "input": { "schema": { "type": "object" } } },
          "states": [
            {
              "key": "querying",
              "onEntry": [
                {
                  "task": "db-query",
                  "config": {
                    "sql": "SELECT Name FROM Customers WHERE Id = @p0",
                    "parameters": ["{{ data.customerId }}"],
                    "into": "customerName",
                    "singleValue": true
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class TestSetup(WorkflowTestHost host, SqliteConnection connection) : IAsyncDisposable
    {
        public WorkflowTestHost Host { get; } = host;
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await connection.DisposeAsync();
        }

        public async Task<T> DbAsync<T>(Func<TestEntityDbContext, Task<T>> query)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TestEntityDbContext>();
            return await query(db);
        }

        public async Task DbAsync(Func<TestEntityDbContext, Task> command)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TestEntityDbContext>();
            await command(db);
        }
    }

    private static async Task<TestSetup> CreateHostAsync(params string[] definitions)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var host = await WorkflowTestHost.StartAsync(
            definitions,
            configure: options =>
            {
                options.UseApplicationDbContext<TestEntityDbContext>();
            },
            services: services =>
            {
                services.AddDbContext<TestEntityDbContext>(db => db.UseSqlite(connection));
            });

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestEntityDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new TestSetup(host, connection);
    }

    [Fact]
    public async Task EntityCreateTask_inserts_entity_and_returns_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(CreateWorkflow);
        var host = setup.Host;

        var startInput = new JsonObject
        {
            ["name"] = "Ahmet Yılmaz",
            ["email"] = "ahmet@test.com",
        };

        var instance = await host.StartInstanceAsync("create-customer-wf", startInput);
        instance.Data.Should().ContainKey("customerId");
        var customerId = instance.Data["customerId"]!.GetValue<int>();
        customerId.Should().BeGreaterThan(0);

        var customer = await setup.DbAsync(db => db.Customers.FindAsync([customerId], ct).AsTask());
        customer.Should().NotBeNull();
        customer!.Name.Should().Be("Ahmet Yılmaz");
        customer.Email.Should().Be("ahmet@test.com");
        customer.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task EntityCreateTask_sets_error_on_uniqueBy_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(CreateWorkflow);
        var host = setup.Host;

        await setup.DbAsync(async db =>
        {
            db.Customers.Add(new TestCustomerEntity { Name = "Mevcut", Email = "ahmet@test.com", IsActive = true });
            await db.SaveChangesAsync(ct);
        });

        var startInput = new JsonObject
        {
            ["name"] = "Yeni Ahmet",
            ["email"] = "ahmet@test.com",
        };

        var instance = await host.StartInstanceAsync("create-customer-wf", startInput);
        instance.Data.Should().ContainKey("error");
        instance.Data["error"]!.GetValue<string>().Should().Contain("zaten mevcut");
        instance.Data.Should().NotContainKey("customerId");
    }

    [Fact]
    public async Task EntityUpdateTask_updates_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(UpdateWorkflow);
        var host = setup.Host;

        var entityId = await setup.DbAsync(async db =>
        {
            var entity = new TestCustomerEntity { Name = "Eski İsim", Email = "update@test.com", IsActive = true };
            db.Customers.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.Id;
        });

        var startInput = new JsonObject
        {
            ["customerId"] = entityId,
            ["newName"] = "Güncel İsim",
        };

        await host.StartInstanceAsync("update-customer-wf", startInput);

        var updated = await setup.DbAsync(db => db.Customers.FindAsync([entityId], ct).AsTask());
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Güncel İsim");
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task EntityUpdateTask_ignores_primary_key_and_read_only_properties()
    {
        const string attemptProtectedUpdateWf = """
            {
              "key": "update-protected-wf",
              "start": { "key": "start", "target": "updating", "input": { "schema": { "type": "object" } } },
              "states": [
                {
                  "key": "updating",
                  "onEntry": [
                    {
                      "task": "entity-update",
                      "config": {
                        "entity": "TestCustomerEntity",
                        "key": "{{ data.customerId }}",
                        "values": {
                          "Id": 9999,
                          "Name": "Safe Update",
                          "InternalNote": "overwritten"
                        }
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(attemptProtectedUpdateWf);
        var entityId = await setup.DbAsync(async db =>
        {
            var entity = new TestCustomerEntity { Name = "Original", InternalNote = "protected", IsActive = true };
            db.Customers.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.Id;
        });

        await setup.Host.StartInstanceAsync("update-protected-wf", new JsonObject { ["customerId"] = entityId });

        var updated = await setup.DbAsync(db => db.Customers.FindAsync([entityId], ct).AsTask());
        updated.Should().NotBeNull();
        updated!.Id.Should().Be(entityId);
        updated.Name.Should().Be("Safe Update");
        updated.InternalNote.Should().Be("protected");
    }

    [Fact]
    public async Task EntityLoadTask_loads_entity_into_data()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(LoadWorkflow);
        var host = setup.Host;

        var entityId = await setup.DbAsync(async db =>
        {
            var entity = new TestCustomerEntity { Name = "Ali Veli", Email = "ali@test.com", IsActive = true };
            db.Customers.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.Id;
        });

        var startInput = new JsonObject { ["customerId"] = entityId };

        var instance = await host.StartInstanceAsync("load-customer-wf", startInput);
        instance.Data.Should().ContainKey("customer");
        var loaded = instance.Data["customer"] as JsonObject;
        loaded.Should().NotBeNull();
        loaded!["Name"]!.GetValue<string>().Should().Be("Ali Veli");
        loaded["Email"]!.GetValue<string>().Should().Be("ali@test.com");
    }

    [Fact]
    public async Task EntityDeleteTask_removes_entity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(DeleteWorkflow);
        var host = setup.Host;

        var entityId = await setup.DbAsync(async db =>
        {
            var entity = new TestCustomerEntity { Name = "Silinecek", Email = "sil@test.com", IsActive = true };
            db.Customers.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.Id;
        });

        var startInput = new JsonObject { ["customerId"] = entityId };
        await host.StartInstanceAsync("delete-customer-wf", startInput);

        var exists = await setup.DbAsync(db => db.Customers.AnyAsync(c => c.Id == entityId, ct));
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DbSqlTask_and_DbQueryTask_execute_successfully()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await CreateHostAsync(DbSqlWorkflow, DbQueryWorkflow);
        var host = setup.Host;

        var entityId = await setup.DbAsync(async db =>
        {
            var entity = new TestCustomerEntity { Name = "SQL Test", Email = "sql@test.com", IsActive = false };
            db.Customers.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity.Id;
        });

        // 1. DbSqlTask: update IsActive = 1
        var sqlInput = new JsonObject { ["customerId"] = entityId };
        var sqlInstance = await host.StartInstanceAsync("db-sql-wf", sqlInput);
        sqlInstance.Data.Should().ContainKey("affected");
        sqlInstance.Data["affected"]!.GetValue<int>().Should().Be(1);

        var updated = await setup.DbAsync(db => db.Customers.FindAsync([entityId], ct).AsTask());
        updated.Should().NotBeNull();
        updated!.IsActive.Should().BeTrue();

        // 2. DbQueryTask: read scalar Name
        var queryInstance = await host.StartInstanceAsync("db-query-wf", sqlInput);
        queryInstance.Data.Should().ContainKey("customerName");
        queryInstance.Data["customerName"]!.GetValue<string>().Should().Be("SQL Test");
    }

    [Fact]
    public void WorkflowTaskRegistry_allows_runtime_registration_without_restarting()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new MiddtWorkflowOptions());
        var registry = new WorkflowTaskRegistry(options);

        // Built-ins exist
        registry.Find("entity-create").Should().Be<EntityCreateTask>();
        registry.Find("entity-update").Should().Be<EntityUpdateTask>();
        registry.Find("db-sql").Should().Be<DbSqlTask>();

        // Register custom at runtime
        registry.Find("runtime-custom").Should().BeNull();
        registry.Register("runtime-custom", typeof(TestCustomRuntimeTask));
        registry.Find("runtime-custom").Should().Be<TestCustomRuntimeTask>();
    }

    [Fact]
    public async Task WorkflowRegistry_reload_loads_new_definitions_at_runtime()
    {
        var dynamicDefinitions = new List<(string Source, string Json)>
        {
            ("initial.json", """{ "key": "dyn-wf-1", "start": { "key": "start", "target": "done" }, "states": [ { "key": "done", "type": "Finish" } ] }""")
        };

        await using var setup = await CreateHostAsync();
        var host = setup.Host;
        var workflowOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MiddtWorkflowOptions>>().Value;
        workflowOptions.DefinitionSources.Add(_ => dynamicDefinitions);

        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        registry.Reload();

        var initial = registry.GetLatest("dyn-wf-1");
        initial.Should().NotBeNull();
        initial.Key.Should().Be("dyn-wf-1");

        // Add another definition at runtime and reload without restarting
        dynamicDefinitions.Add(("runtime-added.json", """{ "key": "dyn-wf-2", "start": { "key": "start", "target": "done" }, "states": [ { "key": "done", "type": "Finish" } ] }"""));
        registry.Reload();

        var added = registry.GetLatest("dyn-wf-2");
        added.Should().NotBeNull();
        added.Key.Should().Be("dyn-wf-2");
    }

    private sealed class TestCustomRuntimeTask : IWorkflowTask
    {
        public Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
