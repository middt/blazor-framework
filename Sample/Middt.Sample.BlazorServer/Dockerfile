# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY Middt.Framework.Model/*.csproj Middt.Framework.Model/
COPY Middt.Framework.Common/*.csproj Middt.Framework.Common/
COPY Middt.Framework.Blazor.Web/*.csproj Middt.Framework.Blazor.Web/
COPY Sample/Middt.Sample.Common/*.csproj Sample/Middt.Sample.Common/
COPY Sample/Middt.Sample.ServiceDefaults/*.csproj Sample/Middt.Sample.ServiceDefaults/
COPY Sample/Middt.Sample.BlazorServer/*.csproj Sample/Middt.Sample.BlazorServer/
RUN dotnet restore Sample/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj
COPY . .
RUN dotnet publish Sample/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Sample.BlazorServer.dll"]
