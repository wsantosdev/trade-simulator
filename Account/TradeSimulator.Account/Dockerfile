FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradeSimulator.Commons/TradeSimulator.Commons.csproj", "Shared/TradeSimulator.Commons/"]
COPY ["Account/TradeSimulator.Account/TradeSimulator.Account.csproj", "Account/TradeSimulator.Account/"]
RUN dotnet restore "Account/TradeSimulator.Account/TradeSimulator.Account.csproj"
COPY . .
WORKDIR "/src/Account/TradeSimulator.Account"
RUN dotnet build "./TradeSimulator.Account.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradeSimulator.Account.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradeSimulator.Account.dll"]
