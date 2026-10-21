FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradeSimulator.Commons/TradeSimulator.Commons.csproj", "Shared/TradeSimulator.Commons/"]
COPY ["Portfolio/TradeSimulator.Portfolio/TradeSimulator.Portfolio.csproj", "Portfolio/TradeSimulator.Portfolio/"]
RUN dotnet restore "Portfolio/TradeSimulator.Portfolio/TradeSimulator.Portfolio.csproj"
COPY . .
WORKDIR "/src/Portfolio/TradeSimulator.Portfolio"
RUN dotnet build "./TradeSimulator.Portfolio.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradeSimulator.Portfolio.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradeSimulator.Portfolio.dll"]
