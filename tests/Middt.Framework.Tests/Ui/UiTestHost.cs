using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Tests.Workflow;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Server;
using Middt.Framework.Ui.Server.DependencyInjection;
using Middt.Framework.Ui.Server.Endpoints;
using Middt.Framework.Ui.Server.Persistence;
using Middt.Framework.Ui.Server.Runtime;

namespace Middt.Framework.Tests.Ui;

/// <summary>
/// In-process UI host: SQLite in-memory (connection open for the host lifetime), <see cref="FakeTimeProvider"/>, layouts seeded
/// from a temporary directory through <c>UiStartup</c>, and a TestServer exposing <c>MapMiddtUi("api/ui")</c> with the header
/// based <see cref="TestAuthHandler"/> (<c>X-Test-User</c>, <c>X-Test-Roles</c>).
/// </summary>
public sealed class UiTestHost : IAsyncDisposable
{
    public const string Admin = "ui-admin";

    private readonly bool ownsConnection;
    private readonly DirectoryInfo? seedDirectory;

    private UiTestHost(WebApplication app, SqliteConnection connection, bool ownsConnection, DirectoryInfo? seedDirectory, FakeTimeProvider time)
    {
        App = app;
        Connection = connection;
        this.ownsConnection = ownsConnection;
        this.seedDirectory = seedDirectory;
        Time = time;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public IServiceProvider Services => App.Services;

    public SqliteConnection Connection { get; }

    public FakeTimeProvider Time { get; }

    public HttpClient Client { get; }

    public static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>Starts a host seeded with <paramref name="seeds"/> (layout JSON, written to a temporary directory).</summary>
    public static async Task<UiTestHost> StartAsync(
        IEnumerable<string>? seeds = null,
        Action<MiddtUiOptions>? configure = null,
        SqliteConnection? connection = null)
    {
        var seedList = seeds?.ToList() ?? [];
        var directory = seedList.Count > 0 ? WriteSeeds(seedList) : null;
        var ownsConnection = connection is null;
        if (connection is null)
        {
            connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
        }

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(UiTestHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(time);

        var sqlite = connection;
        builder.Services.AddMiddtUi(o =>
        {
            o.UseDbContext(db => db.UseSqlite(sqlite));
            o.AutoCreateSchema = true;
            if (directory is not null)
            {
                o.AddLayoutsFromDirectory(directory.FullName);
            }

            configure?.Invoke(o);
        });
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMiddtUi("api/ui");

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }

            directory?.Delete(recursive: true);
            throw;
        }

        return new UiTestHost(app, connection, ownsConnection, directory, time);
    }

    private static DirectoryInfo WriteSeeds(List<string> seeds)
    {
        var directory = Directory.CreateTempSubdirectory("middt-ui-tests-");
        for (var i = 0; i < seeds.Count; i++)
        {
            File.WriteAllText(Path.Combine(directory.FullName, $"layout-{i:D2}.json"), seeds[i]);
        }

        return directory;
    }

    /// <summary>A minimal valid layout.</summary>
    public static string Layout(string key, string kind = "page", int version = 1, string roles = "[]", string body = """[ { "widget": "heading", "text": "Hello {{ query.name }}" } ]""", string extra = "") =>
        $$"""{ "key": "{{key}}", "title": "{{key}} title", "kind": "{{kind}}", "version": {{version}}, "roles": {{roles}}, {{extra}} "body": {{body}} }""";

    public async Task<T> WithStoreAsync<T>(Func<UiLayoutStore, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UiLayoutStore>());
    }

    public async Task<T> WithDbAsync<T>(Func<UiDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UiDbContext>());
    }

    public HttpRequestMessage Request(HttpMethod method, string uri, object? body = null, string? user = "alice", params string[] roles)
    {
        var request = new HttpRequestMessage(method, uri);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
            if (roles.Length > 0)
            {
                request.Headers.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
            }
        }

        if (body is not null)
        {
            request.Content = body is string json
                ? new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                : System.Net.Http.Json.JsonContent.Create(body, body.GetType(), options: Json);
        }

        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body = null, string? user = "alice", params string[] roles)
    {
        using var request = Request(method, uri, body, user, roles);
        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static UiLayoutDocument Parse(string json) => UiJson.Parse(json);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }

        seedDirectory?.Delete(recursive: true);
    }
}
