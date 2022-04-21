#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Sample/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj", "Sample/Middt.Sample.BlazorServer/"]
COPY ["Sample/Middt.Sample.Common/Middt.Sample.Common.csproj", "Sample/Middt.Sample.Common/"]
COPY ["Middt.Framework.Common/Middt.Framework.Common.csproj", "Middt.Framework.Common/"]
COPY ["Middt.Framework.Model/Middt.Framework.Model.csproj", "Middt.Framework.Model/"]
COPY ["Middt.Framework.Blazor.Web/Middt.Framework.Blazor.Web.csproj", "Middt.Framework.Blazor.Web/"]
RUN dotnet restore "Sample/Middt.Sample.BlazorServer/Middt.Sample.BlazorServer.csproj"
COPY . .
WORKDIR "/src/Sample/Middt.Sample.BlazorServer"
RUN dotnet build "Middt.Sample.BlazorServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Middt.Sample.BlazorServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Middt.Sample.BlazorServer.dll"]