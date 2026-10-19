using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Api;
using Middt.Framework.Api.DependencyInjection;
using Middt.Framework.Api.Security;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Tests.Api;

public class ApiItem
{
    public int Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }

    [MaxLength(20)]
    public string? Code { get; set; }
}

public class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    public DbSet<ApiItem> Items => Set<ApiItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiItem>().HasIndex(i => i.Code).IsUnique();
    }
}

public abstract class TestItemsControllerBase(ApiDbContext db) : BaseController
{
    [HttpGet("getall")]
    public async Task<ActionResult<BaseResponseDataModel<List<ApiItem>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await db.Items.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(BaseResponseDataModel<List<ApiItem>>.Success(items, items.Count));
    }

    [HttpGet("getbyid")]
    public async Task<ActionResult<BaseResponseDataModel<ApiItem>>> GetById([FromQuery] int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? NotFound(new ProblemDetails { Status = 404, Title = "Kayıt bulunamadı." }) : Ok(BaseResponseDataModel<ApiItem>.Success(item, 1));
    }

    [HttpPost("getitems")]
    public async Task<ActionResult<BaseResponseDataModel<List<ApiItem>>>> GetItems(CancellationToken cancellationToken)
    {
        var items = await db.Items.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(BaseResponseDataModel<List<ApiItem>>.Success(items, items.Count));
    }

    [HttpPost("insert")]
    [ValidateModel]
    public async Task<ActionResult<BaseResponseDataModel<ApiItem>>> Insert([FromBody] ApiItem item, CancellationToken cancellationToken)
    {
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(BaseResponseDataModel<ApiItem>.Success(item, 1));
    }
}

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class ItemsController(ApiDbContext db) : TestItemsControllerBase(db);

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public sealed class SecureItemsController(ApiDbContext db) : TestItemsControllerBase(db);

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class FaultsController : BaseController
{
    public const string SecretMessage = "Sensitive internal detail: connection string xyz";

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException(SecretMessage);

    [HttpGet("missing")]
    public IActionResult Missing() => throw new KeyNotFoundException("no such thing");
}

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class TokenController(IJwtTokenGenerator tokenGenerator, FakeUserStore users) : BaseTokenController(tokenGenerator)
{
    protected override Task<LoginResponseModel> CheckUsernamePasswordAsync(LoginRequestModel request, CancellationToken cancellationToken) =>
        Task.FromResult(users.Check(request.username, request.password));

    protected override Task<LoginResponseModel> LoadUserAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(users.Load(userId));
}

/// <summary>Echoes what the server sees of the connection (client IP after forwarded headers processing, scheme).</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class ClientInfoController : BaseController
{
    [HttpGet("ip")]
    public IActionResult Ip() => Ok(new { ip = HttpContext.Connection.RemoteIpAddress?.ToString(), scheme = Request.Scheme });
}

public sealed class FakeUserStore
{
    public const string Username = "alice";
    public const string Password = "P@ssw0rd";
    public const long UserId = 7;

    public const string OtherUsername = "bob";
    public const long OtherUserId = 8;

    /// <summary>Reserved for tests that disable the account (keeps alice/bob usable for parallel tests on a shared host).</summary>
    public const string DisposableUsername = "carol";
    public const long DisposableUserId = 9;

    private static readonly Dictionary<string, long> Accounts = new(StringComparer.Ordinal)
    {
        [Username] = UserId,
        [OtherUsername] = OtherUserId,
        [DisposableUsername] = DisposableUserId,
    };

    public static LoginResponseModel User => new() { UserID = UserId, Username = Username, Rol = 1 };

    public ConcurrentDictionary<long, bool> Disabled { get; } = new();

    public LoginResponseModel Check(string? username, string? password) =>
        username is not null && Accounts.TryGetValue(username, out var id) && password == Password && !Disabled.ContainsKey(id)
            ? new LoginResponseModel { UserID = id, Username = username, Rol = 1 }
            : new LoginResponseModel();

    public LoginResponseModel Load(long userId) =>
        Accounts.FirstOrDefault(a => a.Value == userId) is { Key: { } name } && !Disabled.ContainsKey(userId)
            ? new LoginResponseModel { UserID = userId, Username = name, Rol = 1 }
            : new LoginResponseModel();
}

/// <summary>
/// Lets a test choose the connection's remote IP (TestServer leaves it null) through the <c>X-Test-Remote-IP</c> header.
/// Runs before the application pipeline, i.e. before UseForwardedHeaders, exactly like a real socket address.
/// </summary>
internal sealed class TestRemoteIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public const string Header = "X-Test-Remote-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var value) && System.Net.IPAddress.TryParse(value.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>In-process API host built exactly like an application would: AddMiddtApi + UseMiddtApi on a TestServer.</summary>
public sealed class TestApiHost : IAsyncDisposable
{
    public const string Secret = "unit-test-signing-key-0123456789-abcdefghij";
    public const string Issuer = "middt-tests";
    public const string Audience = "middt-tests-clients";

    public static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly SqliteConnection connection;

    private TestApiHost(SqliteConnection connection, WebApplication app)
    {
        this.connection = connection;
        App = app;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public static async Task<TestApiHost> StartAsync(
        string environment = "Development",
        Action<MiddtApiOptions>? configure = null,
        IDictionary<string, string?>? settings = null,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(TestApiHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = null,
            ["JwtSettings:Secret"] = Secret,
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:Expires"] = "600",
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestRemoteIpStartupFilter>();

        builder.AddMiddtApi(options =>
        {
            options.Title = "Test API";
            options.TokenRequestsPerMinute = 1000;
            configure?.Invoke(options);
        });

        builder.Services.AddDbContext<ApiDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddSingleton<FakeUserStore>();
        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        app.UseMiddtApi();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await app.StartAsync();
        return new TestApiHost(connection, app);
    }

    public async Task SeedAsync(params ApiItem[] items)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.Items.AddRange(items);
        await db.SaveChangesAsync();
    }

    public async Task<TokenResponseModel> LoginAsync(string username = FakeUserStore.Username, string password = FakeUserStore.Password)
    {
        using var response = await Client.PostAsJsonAsync("api/v1/token/login", new LoginRequestModel { username = username, password = password }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponseModel>(Json))!;
    }

    public Task<HttpResponseMessage> RefreshAsync(string? accessToken, string? refreshToken) =>
        Client.PostAsJsonAsync("api/v1/token/refreshtoken", new TokenRefreshRequestModel { token = accessToken, refresh_token = refreshToken }, Json);

    public async Task<T> ScopedAsync<T>(Func<ApiDbContext, Task<T>> action)
    {
        using var scope = App.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApiDbContext>());
    }

    public static HttpRequestMessage Authorized(HttpMethod method, string uri, string? token)
    {
        var request = new HttpRequestMessage(method, uri);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        await connection.DisposeAsync();
    }
}

public abstract class ApiHostFixture(string environment, Action<MiddtApiOptions>? configure = null) : IAsyncLifetime
{
    public TestApiHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Host = await TestApiHost.StartAsync(environment, configure);

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class DevelopmentApiFixture() : ApiHostFixture("Development");

/// <summary>Production with OpenAPI explicitly enabled (it is Development-only by default).</summary>
public sealed class ProductionApiFixture() : ApiHostFixture("Production", o => o.EnableOpenApi = true);
