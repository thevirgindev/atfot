FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY atfot.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# RUNTIME STAGE (Ubuntu 24.04)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y \
    python3 python3-pip git golang curl \
    whatweb dnsrecon \
    && rm -rf /var/lib/apt/lists/*

# Install SpiderFoot (GitHub clone, not PyPI)
RUN git clone https://github.com/smicallef/spiderfoot.git /opt/spiderfoot \
    && cd /opt/spiderfoot \
    && pip3 install --break-system-packages --no-cache-dir -r requirements.txt \
    && ln -sf /opt/spiderfoot/sf.py /usr/local/bin/sf

# Install recon-ng (GitHub clone, not PyPI)
RUN git clone https://github.com/lanmaster53/recon-ng.git /opt/recon-ng \
    && cd /opt/recon-ng \
    && pip3 install --break-system-packages --no-cache-dir -r REQUIREMENTS \
    && ln -sf /opt/recon-ng/recon-ng /usr/local/bin/recon-ng

# Install Photon (OSINT URL/domain crawler, GitHub clone)
RUN git clone https://github.com/s0md3v/Photon.git /opt/photon \
    && cd /opt/photon \
    && pip3 install --break-system-packages --no-cache-dir -r requirements.txt \
    && ln -sf /opt/photon/photon.py /usr/local/bin/photon

# Install maigret (username OSINT, successor to Sherlock-like tools, GitHub clone)
RUN git clone https://github.com/soxoj/maigret.git /opt/maigret \
    && cd /opt/maigret \
    && pip3 install --break-system-packages --no-cache-dir -r requirements.txt \
    && ln -sf /opt/maigret/maigret.py /usr/local/bin/maigret

# Install theHarvester (email/subdomain OSINT, GitHub clone)
RUN git clone https://github.com/laramies/theHarvester.git /opt/theharvester \
    && cd /opt/theharvester \
    && pip3 install --break-system-packages --no-cache-dir -r requirements.txt \
    && python3 setup.py install \
    && ln -sf /opt/theharvester/theHarvester.py /usr/local/bin/theHarvester

# Install other Python CLI tools that ARE on PyPI
RUN pip3 install --break-system-packages --no-cache-dir \
    sherlock-project torbot whocord holehe sublist3r

# Install Go tools (subfinder, amass, waybackurls, gau)
RUN go install -v github.com/subfinder/subfinder/v2/cmd/subfinder@latest && \
    go install -v github.com/owasp-amass/amass/v4/...@master && \
    go install -v github.com/tomnomnom/waybackurls@latest && \
    go install -v github.com/lc/gau/v2/cmd/gau@latest

ENV PATH="/root/go/bin:${PATH}"

# Copy compiled bot
COPY --from=build /app/publish /app

RUN mkdir -p /app/resources /app/logs

ENTRYPOINT ["dotnet", "atfot.dll"]