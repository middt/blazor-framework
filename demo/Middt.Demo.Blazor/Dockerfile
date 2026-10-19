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
COPY sdk/client/Middt.Framework.Client/*.csproj sdk/client/Middt.Framework.Client/
COPY sdk/client/Middt.Framework.Bff/*.csproj sdk/client/Middt.Framework.Bff/
COPY sdk/client/Middt.Framework.Blazor/*.csproj sdk/client/Middt.Framework.Blazor/
COPY demo/Middt.Demo.ServiceDefaults/*.csproj demo/Middt.Demo.ServiceDefaults/
COPY demo/Middt.Demo.Blazor/*.csproj demo/Middt.Demo.Blazor/
RUN dotnet restore demo/Middt.Demo.Blazor/Middt.Demo.Blazor.csproj
COPY . .
RUN dotnet publish demo/Middt.Demo.Blazor/Middt.Demo.Blazor.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Demo.Blazor.dll"]
