FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.Gateway/AK.Gateway.API/AK.Gateway.API.csproj", "AK.Gateway/AK.Gateway.API/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.Gateway/AK.Gateway.API/AK.Gateway.API.csproj" --configfile nuget.config
COPY . .
WORKDIR "/src/AK.Gateway/AK.Gateway.API"
RUN dotnet build "AK.Gateway.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.Gateway.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.Gateway.API.dll"]
