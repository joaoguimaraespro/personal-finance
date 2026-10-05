# syntax=docker/dockerfile:1.7
FROM node:26-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build --configuration production

# Caddy serves the SPA and reverse-proxies /api to the API container. Runs unprivileged on :8080.
FROM caddy:2.11-alpine
# The upstream binary carries cap_net_bind_service; we listen on :8080 and run with all capabilities dropped,
# so remove the file capability (otherwise exec fails under no-new-privileges).
RUN apk add --no-cache libcap && setcap -r /usr/bin/caddy && apk del libcap
COPY deploy/Caddyfile deploy/Caddyfile.local deploy/Caddyfile.windows deploy/routes.caddy /etc/caddy/
COPY --from=build /web/dist/web/browser /srv
ENV XDG_CONFIG_HOME=/tmp/caddy-config \
    XDG_DATA_HOME=/tmp/caddy-data
USER 1000:1000
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/healthz"]
