FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/StreamingDigest.Worker/StreamingDigest.Worker.csproj", "src/StreamingDigest.Worker/"]
COPY ["src/StreamingDigest.Application/StreamingDigest.Application.csproj", "src/StreamingDigest.Application/"]
COPY ["src/StreamingDigest.Domain/StreamingDigest.Domain.csproj", "src/StreamingDigest.Domain/"]
COPY ["src/StreamingDigest.Infrastructure/StreamingDigest.Infrastructure.csproj", "src/StreamingDigest.Infrastructure/"]
COPY ["src/StreamingDigest.MatrixNotifier/StreamingDigest.MatrixNotifier.csproj", "src/StreamingDigest.MatrixNotifier/"]

RUN dotnet restore "src/StreamingDigest.Worker/StreamingDigest.Worker.csproj"

COPY . .
WORKDIR /src/src/StreamingDigest.Worker
RUN dotnet publish "StreamingDigest.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "StreamingDigest.Worker.dll"]
