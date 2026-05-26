# Stage 1: base (Debugger attaches here)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
#RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
RUN apt-get update && apt-get install -y ffmpeg comskip && rm -rf /var/lib/apt/lists/*

# Stage 2: build (Compiles the C# code)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy project files for caching
COPY ["src/Vora.Api/Vora.Api.csproj", "src/Vora.Api/"]
COPY ["src/Vora.Application/Vora.Application.csproj", "src/Vora.Application/"]
COPY ["src/Vora.Domain/Vora.Domain.csproj", "src/Vora.Domain/"]
COPY ["src/Vora.Infrastructure/Vora.Infrastructure.csproj", "src/Vora.Infrastructure/"]
RUN dotnet restore "src/Vora.Api/Vora.Api.csproj"

# Copy the entire source tree
COPY . .
WORKDIR "/src/src/Vora.Api"
RUN dotnet build "Vora.Api.csproj" -c Release -o /app/build

# Stage 3: publish
FROM build AS publish
RUN dotnet publish "Vora.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 4: ui-build (React)
FROM node:20 AS ui-build
WORKDIR /ui-src
COPY ["src/Vora.Web/package.json", "src/Vora.Web/package-lock.json*", "./"]
RUN npm install
COPY src/Vora.Web/ .
RUN npm run build

# Stage 5: final
FROM base AS final
WORKDIR /app
COPY --from=publish --chown=app:app /app/publish .
COPY --from=ui-build --chown=app:app /ui-src/dist ./wwwroot
RUN mkdir -p /app/data /transcode && chown -R app:app /app/data /transcode
USER app
ENTRYPOINT ["dotnet", "Vora.Api.dll"]