FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:5.0.100-preview.7 as build

WORKDIR /build

COPY ["Directory.Build.props", "GlobalSuppressions.cs", ".editorconfig", "./"]
COPY ["Octopus/Octopus.csproj", "Octopus/"]
WORKDIR /build/Octopus
RUN dotnet restore

WORKDIR /build
COPY ["Octopus/", "Octopus/"]
WORKDIR /build/Octopus

RUN dotnet build -c Release

FROM build as publish
RUN dotnet publish -c Release --no-build -o /publish

FROM mcr.microsoft.com/dotnet/runtime:5.0.0-preview.7-buster-slim-arm32v7 as final
WORKDIR /app
COPY --from=publish /publish .

ENTRYPOINT ["dotnet", "Octopus.dll"]