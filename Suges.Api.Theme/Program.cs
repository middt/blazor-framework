
using Microsoft.AspNetCore.Builder;
using Suges.Framework.Common.Licence;
using Suges.Template.Api;
using System;

var builder = WebApplication.CreateBuilder(args);


BaseLicence baseLicence = new BaseLicence();
baseLicence.Register();

#if DEBUG
Environment.SetEnvironmentVariable("ENVIRONMENT", "debug");
#elif RELEASE
      Environment.SetEnvironmentVariable("ENVIRONMENT","release");
#elif BUILD1
      Environment.SetEnvironmentVariable("ENVIRONMENT","build1");
#endif

Startup startup = new Startup();
// Add services to the container.
startup.ConfigureServices(builder.Services);

var app = builder.Build();

startup.ConfigureApplication(app);


app.Run();
