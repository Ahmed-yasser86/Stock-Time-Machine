# Reproducible FinBERT sidecar setup: installs pins, pre-downloads the model
# (warm cache so first inference never pays download latency), records the
# resolved model revision. No model files enter git.
# Run once: powershell -ExecutionPolicy Bypass -File scripts/nlp/setup.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent | Split-Path -Parent)
pip install -r scripts/nlp/requirements-nlp.txt
$env:HF_HUB_OFFLINE = ''
python -c "from transformers import AutoModelForSequenceClassification, AutoTokenizer; AutoTokenizer.from_pretrained('ProsusAI/finbert'); AutoModelForSequenceClassification.from_pretrained('ProsusAI/finbert'); print('model cached')"
python scripts/nlp/record_revision.py
Write-Output 'setup complete — start with: python scripts/nlp/server.py --port 5252'
