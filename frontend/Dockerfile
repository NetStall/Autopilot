# syntax=docker/dockerfile:1
# Build context: ./frontend

# ---- build ----
FROM node:24.22.0-alpine3.24 AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

# ---- runtime: only the self-contained Nitro output ----
FROM node:24.22.0-alpine3.24 AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    HOST=0.0.0.0 \
    PORT=3000
COPY --from=build --chown=node:node /app/.output ./.output

# The official image ships a non-root "node" user.
USER node
EXPOSE 3000

HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=5 \
    CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:3000/"]

# NUXT_PUBLIC_API_BASE is read at runtime: it is the URL the *browser* uses to reach the API.
CMD ["node", ".output/server/index.mjs"]
