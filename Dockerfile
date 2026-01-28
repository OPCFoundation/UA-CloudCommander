FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 4840

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["UACloudCommander.csproj", "."]
RUN dotnet restore "./UACloudCommander.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "UACloudCommander.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UACloudCommander.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UACloudCommander.dll"]
