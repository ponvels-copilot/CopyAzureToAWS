FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["CopyAzureToAWS.Api/CopyAzureToAWS.Api.csproj", "CopyAzureToAWS.Api/"]
COPY ["CopyAzureToAWS.Data/CopyAzureToAWS.Data.csproj", "CopyAzureToAWS.Data/"]
RUN dotnet restore "CopyAzureToAWS.Api/CopyAzureToAWS.Api.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/CopyAzureToAWS.Api"
RUN dotnet build "CopyAzureToAWS.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CopyAzureToAWS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CopyAzureToAWS.Api.dll"]