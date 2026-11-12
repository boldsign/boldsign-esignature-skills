"""Start the app: python run.py"""
import logging

from app import create_app
from app.boldsign_client import BoldSign
from app.config import config, load_env
from app.store import Store

load_env()
cfg = config()
logging.basicConfig(level=logging.INFO)
if not cfg.api_key:
    print("BOLDSIGN_API_KEY is not set: sending will fail. See .env.example.")
app = create_app(cfg, BoldSign(cfg), Store(cfg.store_file))

if __name__ == "__main__":
    print(f"Listening on http://localhost:{cfg.port}/documents (BoldSign region {cfg.region})")
    app.run(host="127.0.0.1", port=cfg.port)
