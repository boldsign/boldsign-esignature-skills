"""A tiny JSON file store keyed by BoldSign document ID. Replace it with your database."""
import json
import os
import threading


class Store:
    def __init__(self, path: str):
        self.path = path
        self._lock = threading.Lock()

    def _read(self) -> dict:
        try:
            with open(self.path, encoding="utf-8") as f:
                return json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            return {}

    def all(self) -> list:
        return sorted(self._read().values(), key=lambda d: d.get("createdAt", ""), reverse=True)

    def get(self, document_id: str):
        return self._read().get(document_id)

    def save(self, record: dict) -> dict:
        with self._lock:
            data = self._read()
            data[record["documentId"]] = record
            os.makedirs(os.path.dirname(self.path) or ".", exist_ok=True)
            with open(self.path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
        return record
