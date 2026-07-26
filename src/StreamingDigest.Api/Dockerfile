FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/StreamingDigest.Api/StreamingDigest.Api.csproj", "src/StreamingDigest.Api/"]
COPY ["src/StreamingDigest.Application/StreamingDigest.Application.csproj", "src/StreamingDigest.Application/"]
COPY ["src/StreamingDigest.Infrastructure/StreamingDigest.Infrastructure.csproj", "src/StreamingDigest.Infrastructure/"]
COPY ["src/StreamingDigest.MatrixNotifier/StreamingDigest.MatrixNotifier.csproj", "src/StreamingDigest.MatrixNotifier/"]
COPY ["src/StreamingDigest.Web/StreamingDigest.Web.csproj", "src/StreamingDigest.Web/"]
RUN dotnet restore "src/StreamingDigest.Api/StreamingDigest.Api.csproj"

COPY . .
WORKDIR /src/src/StreamingDigest.Api
RUN dotnet publish "StreamingDigest.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "StreamingDigest.Api.dll"]
