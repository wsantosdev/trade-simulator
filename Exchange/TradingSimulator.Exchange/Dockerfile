FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradingSimulator.Commons/TradingSimulator.Commons.csproj", "Shared/TradingSimulator.Commons/"]
COPY ["Exchange/TradingSimulator.Exchange/TradingSimulator.Exchange.csproj", "Exchange/TradingSimulator.Exchange/"]
RUN dotnet restore "Exchange/TradingSimulator.Exchange/TradingSimulator.Exchange.csproj"
COPY . .
WORKDIR "/src/Exchange/TradingSimulator.Exchange"
RUN dotnet build "./TradingSimulator.Exchange.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradingSimulator.Exchange.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradingSimulator.Exchange.dll"]
