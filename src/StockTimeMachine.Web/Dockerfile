# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/StockTimeMachine.Web/StockTimeMachine.Web.csproj", "src/StockTimeMachine.Web/"]
COPY ["Contact_Manager_Module/Entities/Entities.csproj", "Contact_Manager_Module/Entities/"]
COPY ["Contact_Manager_Module/ServiceContracts/ServiceContracts.csproj", "Contact_Manager_Module/ServiceContracts/"]
COPY ["Contact_Manager_Module/Servicess/Servicess.csproj", "Contact_Manager_Module/Servicess/"]
COPY ["src/Repositories/Repositories.csproj", "src/Repositories/"]
COPY ["src/RepositryContracts/RepositryContracts.csproj", "src/RepositryContracts/"]
COPY ["src/ServiceContracts/ServiceContractsContacts.csproj", "src/ServiceContracts/"]
COPY ["src/Entities/EntitiesStocks.csproj", "src/Entities/"]
COPY ["src/Repositories_Stocks/Repositories_Stocks.csproj", "src/Repositories_Stocks/"]
COPY ["src/RipositoryContracts/RepositoryContracts.csproj", "src/RipositoryContracts/"]
COPY ["src/Services/Services.csproj", "src/Services/"]
RUN dotnet restore "./src/StockTimeMachine.Web/StockTimeMachine.Web.csproj"
COPY . .
WORKDIR "/src/src/StockTimeMachine.Web"
RUN dotnet build "./StockTimeMachine.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./StockTimeMachine.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StockTimeMachine.Web.dll"]