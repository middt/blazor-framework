#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Sample/Middt.Sample.Api/Middt.Sample.Api.csproj", "Sample/Middt.Sample.Api/"]
COPY ["Middt.Framework.Plugin.Redis/Middt.Framework.Plugin.Redis.csproj", "Middt.Framework.Plugin.Redis/"]
COPY ["Middt.Framework.Common/Middt.Framework.Common.csproj", "Middt.Framework.Common/"]
COPY ["Middt.Framework.Model/Middt.Framework.Model.csproj", "Middt.Framework.Model/"]
COPY ["Sample/Middt.Sample.Common/Middt.Sample.Common.csproj", "Sample/Middt.Sample.Common/"]
COPY ["Middt.Framework.Plugin.MassTransit/Middt.Framework.Plugin.MassTransit.csproj", "Middt.Framework.Plugin.MassTransit/"]
COPY ["Middt.Framework.Api/Middt.Framework.Api.csproj", "Middt.Framework.Api/"]
RUN dotnet restore "Sample/Middt.Sample.Api/Middt.Sample.Api.csproj"
COPY . .
WORKDIR "/src/Sample/Middt.Sample.Api"
RUN dotnet build "Middt.Sample.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Middt.Sample.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Middt.Sample.Api.dll"]