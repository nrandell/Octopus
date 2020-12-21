FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:5.0.101 as build

WORKDIR /build

COPY ["Directory.Build.props", "GlobalSuppressions.cs", "global.json", ".editorconfig", "./"]
COPY ["Nick.Energy.Models.CS/Nick.Energy.Models.CS.csproj", "Nick.Energy.Models.CS/"]
COPY ["Nick.Energy.Models/Nick.Energy.Models.fsproj", "Nick.Energy.Models/"]
COPY ["LiveServer/LiveServer.fsproj", "LiveServer/"]
WORKDIR /build/LiveServer
RUN dotnet restore

WORKDIR /build
COPY ["Nick.Energy.Models.CS/", "Nick.Energy.Models.CS/"]
COPY ["Nick.Energy.Models/", "Nick.Energy.Models/"]
COPY ["LiveServer/", "LiveServer/"]
WORKDIR /build/LiveServer

RUN dotnet build -c Release

FROM build as publish
RUN dotnet publish -c Release --no-build -o /publish

FROM mcr.microsoft.com/dotnet/runtime:5.0.1 as final
WORKDIR /app
COPY --from=publish /publish .

ENTRYPOINT ["dotnet", "LiveServer.dll"]