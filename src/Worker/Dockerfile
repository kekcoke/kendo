# Multi-stage build for .NET 10 Worker Service with health HTTP endpoint
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY Kendo.Worker.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5002
ENV ASPNETCORE_URLS=http://+:5002

ENTRYPOINT ["dotnet", "Kendo.Worker.dll"]
