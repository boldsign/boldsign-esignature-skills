import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from app.sample_pdf import sample_pdf  # noqa: E402

with open("sample.pdf", "wb") as f:
    f.write(sample_pdf())
print("Wrote sample.pdf (open it: the text tags are white, so only the labels show).")
