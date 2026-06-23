FROM rust:1.85-bookworm AS builder

WORKDIR /app/backend
COPY backend/Cargo.toml ./
COPY backend/src ./src
RUN cargo build --release

FROM debian:bookworm-slim AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=builder /app/backend/target/release/spectralis-backend /usr/local/bin/spectralis-backend
COPY web-share ./web-share

ENV SPECTRALIS_BACKEND_HOST=0.0.0.0
ENV SPECTRALIS_BACKEND_DATA=/data
ENV SPECTRALIS_WEB_SHARE_ROOT=/app/web-share

CMD ["spectralis-backend"]
