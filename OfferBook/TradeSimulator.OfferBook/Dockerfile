FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shared/TradeSimulator.Commons/TradeSimulator.Commons.csproj", "Shared/TradeSimulator.Commons/"]
COPY ["OfferBook/TradeSimulator.OfferBook/TradeSimulator.OfferBook.csproj", "OfferBook/TradeSimulator.OfferBook/"]
RUN dotnet restore "OfferBook/TradeSimulator.OfferBook/TradeSimulator.OfferBook.csproj"
COPY . .
WORKDIR "/src/OfferBook/TradeSimulator.OfferBook"
RUN dotnet build "./TradeSimulator.OfferBook.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TradeSimulator.OfferBook.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TradeSimulator.OfferBook.dll"]
