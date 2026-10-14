FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradingSimulator.Commons/TradingSimulator.Commons.csproj", "Shared/TradingSimulator.Commons/"]
COPY ["Account/TradingSimulator.Account/TradingSimulator.Account.csproj", "Account/TradingSimulator.Account/"]
RUN dotnet restore "Account/TradingSimulator.Account/TradingSimulator.Account.csproj"
COPY . .
WORKDIR "/src/Account/TradingSimulator.Account"
RUN dotnet build "./TradingSimulator.Account.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradingSimulator.Account.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradingSimulator.Account.dll"]
