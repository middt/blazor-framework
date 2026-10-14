using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Cache;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.ExceptionHandling;
using Middt.Framework.Api.OpenApi;
using Middt.Framework.Api.Security;
using Middt.Framework.Api.Swagger;
using Middt.Framework.Common.DependencyInjection;
using Middt.Framework.Common.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

namespace Middt.Framework.Api.DependencyInjection;

/// <summary>
/// Minimal-hosting entry points that replace the former <c>BaseApiStartup</c> class:
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// builder.AddMiddtApi(o => o.Title = "Sample API");
/// var app = builder.Build();
/// app.UseMiddtApi();
/// app.Run();
/// </code>
/// </summary>
public static class MiddtApiExtensions
{
    public const string TokenRateLimitPolicy = "middt-token";
    public const string DefaultCorsPolicy = "default";

    public static WebApplicationBuilder AddMiddtApi(this WebApplicationBuilder builder, Action<MiddtApiOptions>? configure = null)
    {
        var options = new MiddtApiOptions();
        configure?.Invoke(options);

        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddMiddtForwardedHeaders(configuration);
        services.AddServicesFromConfiguration(configuration);

        // Errors → RFC 9457 ProblemDetails
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // MVC + System.Text.Json
        services.AddControllers(o => o.Filters.Add(new ProblemDetailsContentTypeFilter()))
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });

        // OpenAPI schema generation uses the minimal-API JSON options. EF entities with bidirectional navigation
        // properties produce very deep schema graphs that exceed the default depth of 64.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.MaxDepth = 256);

        // Validation is opt-in per action through [ValidateModel] (see ValidateModelAttribute).
        services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true);

        var versioning = services.AddApiVersioning(o =>
            {
                o.DefaultApiVersion = new ApiVersion(1, 0);
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.ReportApiVersions = true;
            })
            .AddMvc()
            .AddApiExplorer(o =>
            {
                o.GroupNameFormat = "'v'VVV";
                o.SubstituteApiVersionInUrl = true;
            });

        if (options.EnableOpenApi ?? builder.Environment.IsDevelopment())
        {
            // One OpenAPI document per discovered API version (v1, v2, ...).
            versioning.AddOpenApi(o => ConfigureOpenApi(o.Document, options, builder.Environment));
        }

        AddCors(services, configuration);
        AddJwtAuthentication(services, configuration, options);
        AddRateLimiting(services, options);

        services.AddHybridCache();
        services.AddSingleton<IBaseCache, BaseHybridCache>();

        services.AddHealthChecks();

        if (options.EnableResponseCompression)
        {
            AddResponseCompression(services);
        }

        return builder;
    }

    public static WebApplication UseMiddtApi(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<MiddtApiOptions>();

        // Must run first: the client IP drives rate limiting and audit logs.
        app.UseForwardedHeaders();
        if (options.EnableResponseCompression)
        {
            app.UseResponseCompression();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        var headers = new SecurityHeadersOptions { ContentSecurityPolicy = SecurityHeadersOptions.ApiContentSecurityPolicy };
        headers.ContentSecurityPolicyExcludedPaths.Add("/scalar");
        app.UseMiddtSecurityHeaders(headers);

        MapStaticFiles(app, options);

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        if (options.EnableOpenApi ?? app.Environment.IsDevelopment())
        {
            app.MapOpenApi().WithDocumentPerVersion();
            app.MapScalarApiReference(o => o
                .WithTitle(options.Title)
                .AddPreferredSecuritySchemes("Bearer")
                .AddDocuments(options.ApiVersions.Select(v => $"v{v}")));
        }

        app.MapControllers();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }

    private static void AddResponseCompression(IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json", "application/schema+json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
    }

    private static void ConfigureOpenApi(Microsoft.AspNetCore.OpenApi.OpenApiOptions document, MiddtApiOptions options, IHostEnvironment environment)
    {
        document.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info.Title = options.Title;
            return Task.CompletedTask;
        });
        document.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

        if (options.HideUntaggedOperationsOutsideDevelopment && !environment.IsDevelopment())
        {
            var include = document.ShouldInclude;
            document.ShouldInclude = description =>
                include(description) &&
                description.ActionDescriptor.EndpointMetadata.OfType<SwaggerTagAttribute>().Any(t => t.ShownInRelease);
        }
    }

    /// <summary>
    /// Registers the policies of the <c>CorsSettings</c> section. Origins must be listed explicitly; <c>"*"</c> allows any
    /// origin but then credentials are never allowed. A policy without origins allows nothing (fail closed).
    /// </summary>
    private static void AddCors(IServiceCollection services, ConfigurationManager configuration)
    {
        var corsSettings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();

        services.AddCors(cors =>
        {
            foreach (var model in corsSettings.CorsModelList)
            {
                var origins = model.Url ?? [];

                void Configure(Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy)
                {
                    policy.AllowAnyHeader().AllowAnyMethod();
                    if (origins.Contains("*"))
                    {
                        policy.AllowAnyOrigin();
                    }
                    else
                    {
                        policy.WithOrigins(origins).AllowCredentials();
                    }
                }

                cors.AddPolicy(model.Name, Configure);
                if (string.Equals(model.Name, DefaultCorsPolicy, StringComparison.OrdinalIgnoreCase))
                {
                    cors.AddDefaultPolicy(Configure);
                }
            }
        });
    }

    private static void AddJwtAuthentication(IServiceCollection services, ConfigurationManager configuration, MiddtApiOptions options)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Refresh tokens live in IDistributedCache: in-memory by default, shared (e.g. Redis) when the app registers one.
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<IRefreshTokenStore, DistributedCacheRefreshTokenStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = JwtTokenGenerator.CreateValidationParameters(jwt.Value);
                bearer.Events = new JwtBearerEvents
                {
                    // WebSockets cannot send headers: SignalR passes the token in the query string.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments(options.HubPathPrefix))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
    }

    private static void AddRateLimiting(IServiceCollection services, MiddtApiOptions options) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(TokenRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.TokenRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

    private static void MapStaticFiles(WebApplication app, MiddtApiOptions options)
    {
        if (string.IsNullOrEmpty(options.StaticFilesFolder))
        {
            return;
        }

        var folder = Path.Combine(app.Environment.ContentRootPath, options.StaticFilesFolder);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".alp"] = "application/octet-stream";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folder),
            RequestPath = new PathString(options.StaticFilesRequestPath),
            ContentTypeProvider = contentTypes,
        });
    }
}
