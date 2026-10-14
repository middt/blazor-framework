# syntax=docker/dockerfile:1
# 1. Angular: the SDK packages (@middt/ui-core, @middt/angular) and the demo app that consumes them like any app would.
FROM node:24-alpine AS spa
WORKDIR /repo
COPY sdk/client/typescript/ui-core/ sdk/client/typescript/ui-core/
RUN cd sdk/client/typescript/ui-core && npm ci --no-audit --no-fund && npm run build
COPY sdk/client/angular/package.json sdk/client/angular/package-lock.json sdk/client/angular/
RUN cd sdk/client/angular && npm ci --no-audit --no-fund
COPY sdk/client/angular/ sdk/client/angular/
RUN cd sdk/client/angular && npx ng build middt-angular
COPY demo/client/angular/app/package.json demo/client/angular/app/package-lock.json demo/client/angular/app/
RUN cd demo/client/angular/app && npm ci --no-audit --no-fund
COPY demo/client/angular/app/ demo/client/angular/app/
RUN cd demo/client/angular/app && npx ng build sample --configuration production
# Precompress the bundle (.br/.gz next to each file, max. quality): UseMiddtSpaBff serves them when the browser accepts
# the encoding, so no CPU is spent per request. HTML is left out (served only to signed-in users, small, BREACH).
RUN cd demo/client/angular/app/dist/sample/browser && node -e '\
const fs = require("fs"), path = require("path"), zlib = require("zlib"); \
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]); \
for (const file of walk(".")) { \
  if (!/\.(js|mjs|css|svg|json|txt|xml|map|wasm)$/i.test(file)) continue; \
  const data = fs.readFileSync(file); \
  if (data.length < 1024) continue; \
  fs.writeFileSync(file + ".br", zlib.brotliCompressSync(data, { params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 11, [zlib.constants.BROTLI_PARAM_SIZE_HINT]: data.length } })); \
  fs.writeFileSync(file + ".gz", zlib.gzipSync(data, { level: 9 })); \
}'

# 2. BFF host (.NET)
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
COPY demo/shared/Middt.Sample.ServiceDefaults/*.csproj demo/shared/Middt.Sample.ServiceDefaults/
COPY demo/client/angular/Middt.Sample.Angular/*.csproj demo/client/angular/Middt.Sample.Angular/
RUN dotnet restore demo/client/angular/Middt.Sample.Angular/Middt.Sample.Angular.csproj
COPY sdk/ sdk/
COPY demo/shared/Middt.Sample.ServiceDefaults/ demo/shared/Middt.Sample.ServiceDefaults/
COPY demo/client/angular/Middt.Sample.Angular/ demo/client/angular/Middt.Sample.Angular/
RUN dotnet publish demo/client/angular/Middt.Sample.Angular/Middt.Sample.Angular.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=spa /repo/demo/client/angular/app/dist/sample/browser ./wwwroot/
USER $APP_UID
ENTRYPOINT ["dotnet", "Middt.Sample.Angular.dll"]
