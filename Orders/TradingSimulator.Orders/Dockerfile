FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradingSimulator.Commons/TradingSimulator.Commons.csproj", "Shared/TradingSimulator.Commons/"]
COPY ["Orders/TradingSimulator.Orders/TradingSimulator.Orders.csproj", "Orders/TradingSimulator.Orders/"]
RUN dotnet restore "Orders/TradingSimulator.Orders/TradingSimulator.Orders.csproj"
COPY . .
WORKDIR "/src/Orders/TradingSimulator.Orders"
RUN dotnet build "./TradingSimulator.Orders.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradingSimulator.Orders.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradingSimulator.Orders.dll"]
