# Multi-stage build for .NET 10 MVC Web API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy all project files for restore layer caching
COPY Gateway/Kendo.Gateway.csproj Gateway/
COPY Shared/Kendo.Shared.csproj Shared/
RUN dotnet restore Gateway/Kendo.Gateway.csproj

# Copy all source and publish
COPY . .
RUN dotnet publish Gateway/Kendo.Gateway.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "Kendo.Gateway.dll"]