"""Resolve and record the FinBERT model revision + library versions."""
import json
import urllib.request

info = {"model": "ProsusAI/finbert", "revision": "unresolved"}
try:
    with urllib.request.urlopen(
        "https://huggingface.co/api/models/ProsusAI/finbert", timeout=30
    ) as r:
        info["revision"] = json.load(r).get("sha", "unknown")
except Exception as ex:
    info["error"] = str(ex)[:200]

try:
    import torch, transformers
    info["torch"] = torch.__version__
    info["transformers"] = transformers.__version__
    import sys
    info["python"] = sys.version.split()[0]
except Exception as ex:
    info["error"] = str(ex)[:200]

with open("scripts/nlp/MODEL_VERSION.txt", "w") as f:
    json.dump(info, f, indent=2)
print(json.dumps(info))
