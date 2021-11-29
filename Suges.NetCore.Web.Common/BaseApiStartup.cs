using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Suges.Framework.Api.Configuration.Model;
using Suges.Framework.Api.Security;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Model.Model.Authentication;
using Suges.Framework.Model.Model.Configuration;
using Swashbuckle.AspNetCore.Filters;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Api
{
    public abstract class BaseApiStartup
    {
        public abstract void CustomConfigureServices(IServiceCollection services);
        public abstract void CustomConfigure(IApplicationBuilder app);
        public abstract void CustomSignalRHub(IApplicationBuilder app);

        protected BaseApiConfiguration baseConfiguration;
        public BaseApiStartup(IConfiguration configuration)
        {
            Configuration = configuration;
            baseConfiguration = new BaseApiConfiguration();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors();
            AddCorsFromSettings(services);
            AddDependecyFromSettings(services);


            services.AddSignalR();

            services.AddMvc()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                    options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    options.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Error;
                });
            services.AddHttpContextAccessor();

            AddVersioning(services);
            AddSwagger(services);
            services.AddSwaggerExamplesFromAssemblyOf<LoginRequestModelSwaggerExample>();
            CustomConfigureServices(services);
            AddJwtAuthService(services);
        }

        private void AddSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(swagger =>
            {
                swagger.EnableAnnotations();
                swagger.DocumentFilter<SwaggerFilter>();
                swagger.ExampleFilters();
        
             
                swagger.OperationFilter<AddResponseHeadersFilter>(); // [SwaggerResponseHeader]
                //This is to generate the Default UI of Swagger Documentation    
                swagger.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = "SUGES Web API",
                    Description = "SUGES Web API with JWT and Swagger"
                });
               
                // To Enable authorization using Swagger (JWT)    
                swagger.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter 'Bearer' [space] and then your valid token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\"",
                });
                swagger.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                          new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                            new string[] {}

                    }
                });
              //  swagger.OperationFilter<SecurityRequirementsOperationFilter>();
            });
            services.AddSwaggerGenNewtonsoftSupport();
        }
        private void AddVersioning(IServiceCollection services)
        {
            services.AddApiVersioning(o =>
            {
                o.ReportApiVersions = true;
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.DefaultApiVersion = new ApiVersion(1, 0);

                //o.ApiVersionReader = ApiVersionReader.Combine(new HeaderApiVersionReader("X-version"), new QueryStringApiVersionReader("api-version"));
            });
        }
        private void AddDependecyFromSettings(IServiceCollection services)
        {
            // configden gelen dependencyler ekleniyor.
            DependencySettings dependencySettings = baseConfiguration.Get<DependencySettings>();
            if (
            dependencySettings != null &&
            dependencySettings.DependencyModelList != null &&
            dependencySettings.DependencyModelList.Count > 0)
            {
                foreach (DependencyModel service in dependencySettings.DependencyModelList)
                {
                    services.Add(new ServiceDescriptor(serviceType: Type.GetType(service.ServiceType),
                                                       implementationType: Type.GetType(service.ImplementationType),
                                                       lifetime: service.Lifetime));
                }
            }
        }

        private void AddCorsFromSettings(IServiceCollection services)
        {
            CorsSettings corsSettings = baseConfiguration.Get<CorsSettings>();
            if (
            corsSettings != null &&
            corsSettings.CorsModelList != null &&
            corsSettings.CorsModelList.Count > 0)
            {
                foreach (CorsModel corsModel in corsSettings.CorsModelList)
                {
                    if (corsModel.Url != null && corsModel.Url.Length > 0)
                    {
                        services.AddCors(options =>
                        {
                            options.AddPolicy(corsModel.Name,
                                    builder => builder.WithOrigins(corsModel.Url)
                                           .AllowAnyHeader()
                                           .AllowAnyMethod());
                        });
                    }
                    else
                    {
                        services.AddCors(options =>
                        {
                            options.AddPolicy(corsModel.Name,
                                    builder => builder.AllowAnyOrigin()
                                           .AllowAnyHeader()
                                           .AllowAnyMethod());
                        });
                    }


                }

            }
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseRouting();
            app.UseCors("default");
            //app.UseMiddleware<IPControlMiddleware>();


            ConfigureStaticFiles(app, env);

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSwagger();
            app.UseSwaggerUI(option =>
            {
                option.DefaultModelsExpandDepth(-1);
                option.SwaggerEndpoint("v1/swagger.json", "Web API V1");
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });

            CustomConfigure(app);
            CustomSignalRHub(app);
        }
        // Add header token for signalr

        protected void ConfigureStaticFiles(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var fileExtensionContentTypeProvider = new FileExtensionContentTypeProvider();
            fileExtensionContentTypeProvider.Mappings.Add(".alp", "application/octet-stream");
            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider(
                        Path.Combine(Directory.GetCurrentDirectory(), @"Files")),
                RequestPath = new PathString("/Files"),
                ContentTypeProvider = fileExtensionContentTypeProvider
            });
        }
        protected void AddJwtAuthService(IServiceCollection services)
        {
            JwtSettings jwtSettings = baseConfiguration.Get<JwtSettings>();
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
            var tokenValidationParameters = new TokenValidationParameters
            {
                // The signing key must match!
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,

                // Validate the JWT Issuer (iss) claim
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,

                // Validate the JWT Audience (aud) claim
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,

                // Validate the token expiry
                ValidateLifetime = true,

                // to use timeout value
                ClockSkew = TimeSpan.Zero
            };

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(o =>
            {
                //o.Authority = "";
                //o.Audience = "";
                o.TokenValidationParameters = tokenValidationParameters;
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.Value.StartsWith("/hubs/"))
                        {
                            var accessToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(accessToken))
                            {
                                context.Token = accessToken;
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });
        }


    }
}
