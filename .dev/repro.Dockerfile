# Local repro for the Wacs.WASI.Preview2.Test parallelism flake.
# Mirrors the relevant pieces of `.github/workflows/ci.yml`:
#   - ubuntu-latest base
#   - .NET 8 SDK installed
#   - dotnet restore + build + test from the repo root
# Volume-mount the repo at /repo so source edits are seen
# without rebuilding the image.
#
# Usage:
#   docker build -f .dev/repro.Dockerfile -t wacs-repro .dev
#   docker run --rm -v "$PWD":/repo -w /repo wacs-repro \
#     dotnet test -c Release \
#       Wacs.WASI/Wacs.WASI.Preview2/Wacs.WASI.Preview2.Test/Wacs.WASI.Preview2.Test.csproj
FROM mcr.microsoft.com/dotnet/sdk:9.0
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
# global.json pins 9.0+ for SDK selection, but several test
# projects target net8.0 — those need the .NET 8 runtime
# present too. The 9.0 SDK image only ships 9.x runtime,
# so layer the 8.0 runtime in via the dotnet-install script
# (the same script the SDK image uses internally).
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --runtime dotnet --channel 8.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh
WORKDIR /repo
