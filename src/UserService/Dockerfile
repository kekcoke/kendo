# Multi-stage build for .NET 10 MVC Web API with EF Core
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy all project files for restore layer caching
COPY UserService/Kendo.UserService.csproj UserService/
COPY Shared/Kendo.Shared.csproj Shared/
RUN dotnet restore UserService/Kendo.UserService.csproj

# Copy all source and publish
COPY . .
RUN dotnet publish UserService/Kendo.UserService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001

ENTRYPOINT ["dotnet", "Kendo.UserService.dll"]