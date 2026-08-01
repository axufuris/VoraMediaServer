# Stage 1: base (Debugger attaches here)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
# Install jellyfin-ffmpeg7 instead of Debian's stock ffmpeg. Jellyfin's
# build ships --enable-cuda-llvm + their tonemap_cuda filter, which
# does HDR→SDR tonemap directly on the GPU via plain CUDA — no Vulkan
# or OpenCL ICD needed. This works identically on Linux/Unraid AND
# WSL2/Docker Desktop because the WSL2 NVIDIA driver does pass CUDA
# (and NVENC/NVDEC) through to the container, it just doesn't pass
# Vulkan/OpenCL. The vanilla Debian ffmpeg has neither tonemap_cuda
# nor a working CUDA-LLVM build, so the HDR tonemap step was pinned
# to CPU on the old toolchain.
#
# We also keep libvulkan1 + ocl-icd-libopencl1 (and their ICD JSON
# files) so the existing libplacebo / tonemap_opencl filter chains
# still work IF anyone wants to use them on a host where Vulkan or
# OpenCL ARE exposed (bare-metal Linux + Unraid with capabilities
# [gpu, video, compute, graphics]). On WSL2 those loaders are present
# but find no platform — harmless, ffmpeg just doesn't pick them.
#
# The /usr/local/bin symlinks keep `Process.Start("ffmpeg", ...)` from
# C# working without any code change: the jellyfin binary sits at
# /usr/lib/jellyfin-ffmpeg/ffmpeg, the symlink puts it earlier on PATH.
# Step 1: runtime libraries Vora needs at all times. These are in the
# default repo of every Debian / Ubuntu base, so this layer never fails.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libvulkan1 \
        ocl-icd-libopencl1 \
        gnupg \
        curl \
        gosu \
        ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# Step 2: comskip (optional). Lives in `contrib` on bookworm and isn't
# even packaged on some newer distros — install best-effort and keep
# going rather than failing the whole image build over a quality-of-
# life tool the API doesn't strictly need.
RUN apt-get update && \
    (apt-get install -y --no-install-recommends comskip || echo "comskip unavailable in this distro; continuing without it") && \
    rm -rf /var/lib/apt/lists/*

# Step 3: jellyfin-ffmpeg7. We source /etc/os-release so the same
# Dockerfile works whether the base image rolls forward from bookworm
# to trixie to noble — apt sources line is built from $ID and
# $VERSION_CODENAME instead of being pinned. Then we symlink the
# binaries into /usr/local/bin so existing `Process.Start("ffmpeg")`
# C# code resolves to the jellyfin build without any code change.
RUN . /etc/os-release && \
    mkdir -p /etc/apt/keyrings && \
    curl -fsSL https://repo.jellyfin.org/jellyfin_team.gpg.key | gpg --dearmor -o /etc/apt/keyrings/jellyfin.gpg && \
    echo "deb [signed-by=/etc/apt/keyrings/jellyfin.gpg] https://repo.jellyfin.org/${ID} ${VERSION_CODENAME} main" > /etc/apt/sources.list.d/jellyfin.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends jellyfin-ffmpeg7 && \
    ln -sf /usr/lib/jellyfin-ffmpeg/ffmpeg /usr/local/bin/ffmpeg && \
    ln -sf /usr/lib/jellyfin-ffmpeg/ffprobe /usr/local/bin/ffprobe && \
    rm -rf /var/lib/apt/lists/*

# Step 4: ICD JSON files that point the OpenCL loader + Vulkan loader
# at the NVIDIA driver libraries. The libraries themselves
# (libnvidia-opencl.so.1, libGLX_nvidia.so.0) are mounted into the
# container by the NVIDIA Container Toolkit when the right
# capabilities are requested in compose ([gpu, video, compute,
# graphics]). On WSL2 those mounts don't happen, but the JSON files
# are harmless — ffmpeg's tonemap_cuda path doesn't use either ICD.
RUN mkdir -p /etc/OpenCL/vendors /etc/vulkan/icd.d /usr/share/vulkan/icd.d && \
    echo "libnvidia-opencl.so.1" > /etc/OpenCL/vendors/nvidia.icd && \
    printf '%s\n' \
        '{' \
        '    "file_format_version" : "1.0.0",' \
        '    "ICD": {' \
        '        "library_path": "libGLX_nvidia.so.0",' \
        '        "api_version" : "1.3.289"' \
        '    }' \
        '}' > /usr/share/vulkan/icd.d/nvidia_icd.json

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
COPY --from=publish /app/publish .
COPY --from=ui-build /ui-src/dist ./wwwroot
RUN mkdir -p /app/data /transcode
COPY src/Vora.Api/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
ENV PUID=99 PGID=100
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]