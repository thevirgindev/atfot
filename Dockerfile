FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY atfot.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y \
    python3 python3-pip git golang-go curl \
    whatweb dnsrecon \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/smicallef/spiderfoot.git /opt/spiderfoot && cd /opt/spiderfoot && pip3 install --break-system-packages --no-cache-dir -r requirements.txt && ln -sf /opt/spiderfoot/sf.py /usr/local/bin/sf

RUN git clone https://github.com/lanmaster53/recon-ng.git /opt/recon-ng && cd /opt/recon-ng && pip3 install --break-system-packages --no-cache-dir -r REQUIREMENTS && ln -sf /opt/recon-ng/recon-ng /usr/local/bin/recon-ng

RUN git clone https://github.com/s0md3v/Photon.git /opt/photon && cd /opt/photon && pip3 install --break-system-packages --no-cache-dir -r requirements.txt && printf '#!/bin/sh\nexec python3 /opt/photon/photon.py "$@"\n' > /usr/local/bin/photon && chmod +x /usr/local/bin/photon

RUN git clone https://github.com/soxoj/maigret.git /opt/maigret && cd /opt/maigret && pip3 install --break-system-packages --no-cache-dir --ignore-installed soupsieve .

RUN git clone https://github.com/laramies/theHarvester.git /opt/theharvester && cd /opt/theharvester && pip3 install --break-system-packages --no-cache-dir -r requirements/base.txt && printf '#!/bin/sh\nexec python3 /opt/theharvester/theHarvester.py "$@"\n' > /usr/local/bin/theHarvester && chmod +x /usr/local/bin/theHarvester

RUN pip3 install --break-system-packages --no-cache-dir sherlock-project torbot whocord holehe sublist3r

RUN go install -v github.com/subfinder/subfinder/v2/cmd/subfinder@latest && go install -v github.com/owasp-amass/amass/v4/...@master && go install -v github.com/tomnomnom/waybackurls@latest && go install -v github.com/lc/gau/v2/cmd/gau@latest

ENV PATH="/root/go/bin:${PATH}"

COPY --from=build /app/publish /app

RUN mkdir -p /app/resources /app/logs

ENTRYPOINT ["dotnet", "atfot.dll"]