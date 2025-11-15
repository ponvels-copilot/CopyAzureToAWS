FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["AzureToAWS.Api/AzureToAWS.Api.csproj", "AzureToAWS.Api/"]
COPY ["AzureToAWS.Data/AzureToAWS.Data.csproj", "AzureToAWS.Data/"]
RUN dotnet restore "AzureToAWS.Api/AzureToAWS.Api.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/AzureToAWS.Api"
RUN dotnet build "AzureToAWS.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AzureToAWS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AzureToAWS.Api.dll"]