"""FinBERT sentiment sidecar (CPU, deterministic).

POST /analyze {"texts": [...]} -> {"results": [{"pos":..,"neu":..,"neg":..,
  "score":..,"confidence":..,"model":..,"revision":..}], "model":.., "revision":..}
GET /health -> {"status": "ok", "model":.., "revision":.., "device": "cpu"}

Determinism: model.eval() + torch.no_grad(), no sampling; same code+model+
input => same output. Truncation (512 tokens) is tokenizer-standard and logged
per call count, never silent per item.
stdlib HTTP only: no web framework. Run: python server.py [--port 5252]
"""
import json
import sys
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer

MODEL_ID = "ProsusAI/finbert"
MAX_BATCH = 32

_model = None
_tokenizer = None
_revision = None
_torch = None


def load():
    global _model, _tokenizer, _revision, _torch
    import torch
    from transformers import AutoModelForSequenceClassification, AutoTokenizer

    _torch = torch
    _tokenizer = AutoTokenizer.from_pretrained(MODEL_ID)
    _model = AutoModelForSequenceClassification.from_pretrained(MODEL_ID)
    _model.eval()
    # Resolved revision for provenance (recorded per score downstream).
    try:
        with urllib.request.urlopen(
            f"https://huggingface.co/api/models/{MODEL_ID}", timeout=30
        ) as r:
            _revision = json.load(r).get("sha", "unknown")
    except Exception:
        _revision = "unresolved"
    return _revision


class Handler(BaseHTTPRequestHandler):
    server_version = "FinBertSidecar/1.0"

    def _send(self, code, payload):
        body = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._send(200, {"status": "ok", "model": MODEL_ID,
                             "revision": _revision, "device": "cpu"})
        else:
            self._send(404, {"error": "unknown endpoint"})

    def do_POST(self):
        if self.path != "/analyze":
            self._send(404, {"error": "unknown endpoint"})
            return
        try:
            length = int(self.headers.get("Content-Length", 0))
            payload = json.loads(self.rfile.read(length) or b"{}")
            texts = payload.get("texts", [])
            if not isinstance(texts, list) or len(texts) > MAX_BATCH:
                self._send(400, {"error": f"texts must be a list of at most {MAX_BATCH}"})
                return
            results = []
            with _torch.no_grad():
                for text in texts:
                    t = text if isinstance(text, str) else ""
                    enc = _tokenizer(t, return_tensors="pt", truncation=True,
                                     max_length=512, padding=True)
                    logits = _model(**enc).logits[0]
                    probs = _torch.softmax(logits, dim=-1).tolist()
                    # ProsusAI/finbert label order: positive, negative, neutral.
                    pos, neg, neu = probs[0], probs[1], probs[2]
                    results.append({
                        "pos": pos, "neu": neu, "neg": neg,
                        "score": pos - neg,
                        "confidence": max(probs),
                        "model": MODEL_ID, "revision": _revision,
                    })
            self._send(200, {"results": results, "model": MODEL_ID,
                             "revision": _revision})
        except Exception as ex:  # never hang the caller on bad input
            self._send(500, {"error": str(ex)[:300]})

    def log_message(self, *args):
        pass  # quiet; callers log what they need


if __name__ == "__main__":
    port = int(sys.argv[sys.argv.index("--port") + 1]) if "--port" in sys.argv else 5252
    rev = load()
    print(f"finbert ready model={MODEL_ID} revision={rev} port={port}", flush=True)
    HTTPServer(("127.0.0.1", port), Handler).serve_forever()
