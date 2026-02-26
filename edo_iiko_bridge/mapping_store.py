"""Хранение сопоставлений: строка документа УПД ↔ товар iiko (по артикулу или id)."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path


@dataclass
class MappingEntry:
    """Одна запись маппинга: строка накладной ЭДО → товар iiko."""
    document_key: str   # например messageId|entityId
    line_number: int
    product_code_edo: str   # артикул из УПД
    iiko_product_id: str
    iiko_articul: str


def load_mapping(path: Path) -> list[MappingEntry]:
    """Загрузить маппинг из JSON-файла."""
    if not path.exists():
        return []
    raw = path.read_text(encoding="utf-8")
    data = json.loads(raw)
    if not isinstance(data, list):
        return []
    out = []
    for item in data:
        if not isinstance(item, dict):
            continue
        out.append(
            MappingEntry(
                document_key=str(item.get("documentKey", item.get("document_key", ""))),
                line_number=int(item.get("lineNumber", item.get("line_number", 0))),
                product_code_edo=str(item.get("productCodeEdo", item.get("product_code_edo", ""))),
                iiko_product_id=str(item.get("iikoProductId", item.get("iiko_product_id", ""))),
                iiko_articul=str(item.get("iikoArticul", item.get("iiko_articul", ""))),
            )
        )
    return out


def save_mapping(path: Path, entries: list[MappingEntry]) -> None:
    """Сохранить маппинг в JSON-файл."""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = [
        {
            "documentKey": e.document_key,
            "lineNumber": e.line_number,
            "productCodeEdo": e.product_code_edo,
            "iikoProductId": e.iiko_product_id,
            "iikoArticul": e.iiko_articul,
        }
        for e in entries
    ]
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def find_mapping_for_line(
    entries: list[MappingEntry],
    document_key: str,
    line_number: int,
    product_code_edo: str | None = None,
) -> MappingEntry | None:
    """Найти запись маппинга по документу и номеру строки (и опционально по артикулу ЭДО)."""
    for e in entries:
        if e.document_key != document_key or e.line_number != line_number:
            continue
        if product_code_edo is not None and e.product_code_edo != product_code_edo:
            continue
        return e
    return None
