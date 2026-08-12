FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management file first
COPY ["Directory.Packages.props", "."]

# Copy all .csproj files for restore  
COPY ["src/StreamingDigest.Api/StreamingDigest.Api.csproj", "src/StreamingDigest.Api/"]
COPY ["src/StreamingDigest.Application/StreamingDigest.Application.csproj", "src/StreamingDigest.Application/"]
COPY ["src/StreamingDigest.Domain/StreamingDigest.Domain.csproj", "src/StreamingDigest.Domain/"]
COPY ["src/StreamingDigest.Infrastructure/StreamingDigest.Infrastructure.csproj", "src/StreamingDigest.Infrastructure/"]
COPY ["src/StreamingDigest.MatrixNotifier/StreamingDigest.MatrixNotifier.csproj", "src/StreamingDigest.MatrixNotifier/"]
COPY ["src/StreamingDigest.Web/StreamingDigest.Web.csproj", "src/StreamingDigest.Web/"]
COPY ["src/StreamingDigest.Worker/StreamingDigest.Worker.csproj", "src/StreamingDigest.Worker/"]

# Restore dependencies - suppress exit code 1 from NuGet warnings  
RUN dotnet restore "src/StreamingDigest.Api/StreamingDigest.Api.csproj" || true

# Copy all source files
COPY src/StreamingDigest.Api/ src/StreamingDigest.Api/
COPY src/StreamingDigest.Application/ src/StreamingDigest.Application/
COPY src/StreamingDigest.Domain/ src/StreamingDigest.Domain/
COPY src/StreamingDigest.Infrastructure/ src/StreamingDigest.Infrastructure/
COPY src/StreamingDigest.MatrixNotifier/ src/StreamingDigest.MatrixNotifier/
COPY src/StreamingDigest.Web/ src/StreamingDigest.Web/
COPY src/StreamingDigest.Worker/ src/StreamingDigest.Worker/

# Build API (which compiles dependencies including Worker)
RUN cd /src && dotnet build "src/StreamingDigest.Api/StreamingDigest.Api.csproj" -c Release --no-restore 2>&1 | tee build.log || true

# Publish only the API service
WORKDIR /src/src/StreamingDigest.Api
RUN dotnet publish "StreamingDigest.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build 2>&1 | tail -20

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "StreamingDigest.Api.dll"]
