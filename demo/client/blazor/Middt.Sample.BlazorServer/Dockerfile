# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY sdk/shared/Middt.Framework.Model/*.csproj sdk/shared/Middt.Framework.Model/
COPY sdk/shared/Middt.Framework.Expressions/*.csproj sdk/shared/Middt.Framework.Expressions/
COPY sdk/shared/Middt.Framework.Ui.Abstractions/*.csproj sdk/shared/Middt.Framework.Ui.Abstractions/
COPY sdk/server/Middt.Framework.Common/*.csproj sdk/server/Middt.Framework.Common/
COPY sdk/client/dotnet/Middt.Framework.Client/*.csproj sdk/client/dotnet/Middt.Framework.Client/
COPY sdk/client/bff/Middt.Framework.Bff/*.csproj sdk/client/bff/Middt.Framework.Bff/
COPY sdk/client/blazor/Middt.Framework.Workflow.Razor/*.csproj sdk/client/blazor/Middt.Framework.Workflow.Razor/
COPY sdk/client/blazor/Middt.Framework.Blazor.Web/*.csproj sdk/client/blazor/Middt.Framework.Blazor.Web/
COPY demo/shared/Middt.Sample.Common/*.csproj demo/shared/Middt.Sample.Common/
COPY demo/shared/Middt.Sample.ServiceDefaults/*.csproj demo/shared/Middt.Sample.ServiceDefaults/
COPY demo/client/blazor/Middt.Sample.BlazorServer/*.csproj demo/client/blazor/Middt.Sample.BlazorServer/
RUN dotnet restore demo/client/blazor/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj
COPY . .
RUN dotnet publish demo/client/blazor/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Sample.BlazorServer.dll"]
