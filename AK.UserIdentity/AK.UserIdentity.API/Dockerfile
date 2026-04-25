FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.UserIdentity/AK.UserIdentity.API/AK.UserIdentity.API.csproj", "AK.UserIdentity/AK.UserIdentity.API/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.UserIdentity/AK.UserIdentity.API/AK.UserIdentity.API.csproj" --configfile nuget.config
COPY . .
WORKDIR "/src/AK.UserIdentity/AK.UserIdentity.API"
RUN dotnet build "AK.UserIdentity.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.UserIdentity.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.UserIdentity.API.dll"]
