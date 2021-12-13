using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Api.Swagger;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Model.Model.Authentication;
using Middt.Framework.Model.Model.Configuration;
using Swashbuckle.AspNetCore.Filters;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Middt.Framework.Api
{
    public abstract class BaseApiStartup
    {
        public abstract void CustomConfigureServices(IServiceCollection services);
        public abstract void CustomConfigure(IApplicationBuilder app);
        public abstract void CustomSignalRHub(IApplicationBuilder app);

        protected BaseApiConfiguration baseConfiguration;

        protected IApiVersionDescriptionProvider apiVersionDescriptionProvider;

        public BaseApiStartup()
        {
            baseConfiguration = new BaseApiConfiguration();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors();
            AddCorsFromSettings(services);
            AddDependecyFromSettings(services);


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

            apiVersionDescriptionProvider = services.BuildServiceProvider().GetService<IApiVersionDescriptionProvider>();

        }

        private void AddSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(swagger =>
            {
                swagger.EnableAnnotations();
                swagger.DocumentFilter<SwaggerFilter>();
                swagger.ExampleFilters();


                swagger.OperationFilter<AddResponseHeadersFilter>(); // [SwaggerResponseHeader]

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

            services.ConfigureOptions<ConfigureSwaggerOptions>();
            //services.AddSwaggerGenNewtonsoftSupport();
        }
        private void AddVersioning(IServiceCollection services)
        {
            services.AddApiVersioning(setup =>
            {
                setup.DefaultApiVersion = new ApiVersion(1, 0);
                setup.AssumeDefaultVersionWhenUnspecified = true;
                setup.ReportApiVersions = true;
            });

            services.AddVersionedApiExplorer(setup =>
            {
                setup.GroupNameFormat = "'v'VVV";
                setup.SubstituteApiVersionInUrl = true;
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
        private void ConfigureStaticFiles(IApplicationBuilder app)
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
        private void AddJwtAuthService(IServiceCollection services)
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


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void ConfigureApplication(WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseRouting();
            //app.UseCors("default");
            //app.UseMiddleware<IPControlMiddleware>();


            ConfigureStaticFiles(app);

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.DefaultModelsExpandDepth(-1);

                foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
                {
                    options.SwaggerEndpoint(
                        $"{description.GroupName}/swagger.json",
                        description.GroupName.ToUpperInvariant());
                }
            });

            //app.UseEndpoints(endpoints =>
            //{
            //    endpoints.MapDefaultControllerRoute();
            //});
            app.MapControllers();

            CustomConfigure(app);
            CustomSignalRHub(app);
        }
    }
}
