FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Contracts/Shared.Contracts.csproj", "src/Shared.Contracts/"]
COPY ["src/TripService/TripService.csproj", "src/TripService/"]
RUN dotnet restore "src/TripService/TripService.csproj"
COPY ["src/Shared.Contracts/", "src/Shared.Contracts/"]
COPY ["src/TripService/", "src/TripService/"]
WORKDIR "/src/src/TripService"
RUN dotnet build "TripService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TripService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TripService.dll"]
