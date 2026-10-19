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
COPY sdk/server/Middt.Framework.Api/*.csproj sdk/server/Middt.Framework.Api/
COPY sdk/client/dotnet/Middt.Framework.Client/*.csproj sdk/client/dotnet/Middt.Framework.Client/
COPY sdk/server/Middt.Framework.Workflow/*.csproj sdk/server/Middt.Framework.Workflow/
COPY sdk/server/Middt.Framework.Ui.Server/*.csproj sdk/server/Middt.Framework.Ui.Server/
COPY sdk/server/plugins/Middt.Framework.Plugin.Rebus/*.csproj sdk/server/plugins/Middt.Framework.Plugin.Rebus/
COPY demo/shared/Middt.Sample.ServiceDefaults/*.csproj demo/shared/Middt.Sample.ServiceDefaults/
COPY demo/server/Middt.Sample.Api/*.csproj demo/server/Middt.Sample.Api/
RUN dotnet restore demo/server/Middt.Sample.Api/Middt.Sample.Api.csproj
COPY . .
RUN dotnet publish demo/server/Middt.Sample.Api/Middt.Sample.Api.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Sample.Api.dll"]
