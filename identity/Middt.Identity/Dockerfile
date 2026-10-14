# syntax=docker/dockerfile:1
# Middt Identity service. Build from the repository root: docker build -f identity/Middt.Identity/Dockerfile .
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
# Development signing/encryption certificates live in the user's X509 store; compose mounts a volume here so tokens
# stay valid when the container is recreated (production configures Identity:SigningCertificate instead).
RUN mkdir -p /home/app/.dotnet/corefx/cryptography/x509stores && chown -R $APP_UID /home/app/.dotnet

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
# Only the framework libraries the service builds on (Common, Workflow and their dependencies) + the service itself.
COPY sdk/shared/ sdk/shared/
COPY sdk/server/ sdk/server/
COPY identity/Middt.Identity/ identity/Middt.Identity/
RUN dotnet restore identity/Middt.Identity/Middt.Identity.csproj
RUN dotnet publish identity/Middt.Identity/Middt.Identity.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Identity.dll"]
