# FinBERT sentiment sidecar (CPU, deterministic).
# Build from the repo root so paths stay stable:
#   docker build -f scripts/nlp/Dockerfile .
# Base is python:3.12-slim (not 3.14): the pinned torch CPU wheels in
# requirements-nlp.txt exist for 3.12. stdlib HTTP server, no web framework.
FROM python:3.12-slim AS base
WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    HF_HUB_CACHE=/cache/hf \
    PORT=5252
EXPOSE 5252

FROM base AS deps
COPY scripts/nlp/requirements-nlp.txt ./requirements-nlp.txt
RUN pip install --no-cache-dir -r requirements-nlp.txt

FROM deps AS final
COPY scripts/nlp/server.py ./server.py
# The ProsusAI/finbert weights (~440MB) download on first start into the
# /cache/hf volume (see compose/K8s), so the image stays small and the model
# survives restarts. Pre-warm once with scripts/nlp/setup.ps1 for local runs.
VOLUME /cache/hf
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 \
  CMD python -c "import json,urllib.request; print(json.load(urllib.request.urlopen('http://127.0.0.1:5252/health', timeout=8)))" || exit 1
ENTRYPOINT ["python", "server.py", "--port", "5252"]
