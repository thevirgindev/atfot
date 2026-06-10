# BUILD STAGE
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY atfot.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# RUNTIME STAGE (Ubuntu 24.04)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Install Python, pip, git, go, and other dependencies
RUN apt-get update && apt-get install -y \
    python3 python3-pip git golang curl \
    && rm -rf /var/lib/apt/lists/*

# Install Python CLI tools
RUN pip3 install --no-cache-dir sherlock-project theHarvester spiderfoot recon-ng torbot od-crawler whocord

# Install Go tools
RUN go install -v github.com/subfinder/subfinder/v2/cmd/subfinder@latest && \
    go install -v github.com/owasp-amass/amass/v4/...@master

ENV PATH="/root/go/bin:${PATH}"

# Copy compiled bot
COPY --from=build /app/publish /app

RUN mkdir -p /app/resources /app/logs

RUN pip3 install --no-cache-dir holehe
RUN go install -v github.com/tomnomnom/waybackurls@latest
RUN go install -v github.com/lc/gau/v2/cmd/gau@latest

ENTRYPOINT ["dotnet", "atfot.dll"]