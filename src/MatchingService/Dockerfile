FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Contracts/Shared.Contracts.csproj", "src/Shared.Contracts/"]
COPY ["src/MatchingService/MatchingService.csproj", "src/MatchingService/"]
RUN dotnet restore "src/MatchingService/MatchingService.csproj"
COPY ["src/Shared.Contracts/", "src/Shared.Contracts/"]
COPY ["src/MatchingService/", "src/MatchingService/"]
WORKDIR "/src/src/MatchingService"
RUN dotnet build "MatchingService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MatchingService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MatchingService.dll"]
