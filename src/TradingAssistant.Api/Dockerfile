FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["TradingAssistant.Api.csproj", "."]
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Npgsql requires Kerberos libs for GSSAPI
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libkrb5-3 libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Create data protection keys directory with correct permissions
RUN mkdir -p /app/keys && chown app:app /app/keys

# Run as built-in non-root user
USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TradingAssistant.Api.dll"]
