# Multi-stage build for .NET 10 MVC Web API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY Kendo.UserService.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001

ENTRYPOINT ["dotnet", "Kendo.UserService.dll"]
