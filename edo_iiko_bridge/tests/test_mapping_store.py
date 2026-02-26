"""Тесты хранилища маппинга."""
import json
import tempfile
from pathlib import Path

import pytest

from edo_iiko_bridge.mapping_store import (
    MappingEntry,
    find_mapping_for_line,
    load_mapping,
    save_mapping,
)


def test_load_mapping_empty_path():
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as f:
        path = Path(f.name)
    path.unlink(missing_ok=True)
    assert load_mapping(path) == []


def test_save_and_load_mapping():
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as f:
        path = Path(f.name)
    try:
        entries = [
            MappingEntry("msg|ent", 1, "ART-1", "iiko-id-1", "ART-1"),
            MappingEntry("msg|ent", 2, "ART-2", "iiko-id-2", "ART-2"),
        ]
        save_mapping(path, entries)
        loaded = load_mapping(path)
        assert len(loaded) == 2
        assert loaded[0].document_key == "msg|ent" and loaded[0].line_number == 1
        assert loaded[1].iiko_articul == "ART-2"
    finally:
        path.unlink(missing_ok=True)


def test_find_mapping_for_line():
    entries = [
        MappingEntry("doc1", 1, "A1", "id1", "A1"),
        MappingEntry("doc1", 2, "A2", "id2", "A2"),
    ]
    assert find_mapping_for_line(entries, "doc1", 1).iiko_product_id == "id1"
    assert find_mapping_for_line(entries, "doc1", 2, "A2").iiko_articul == "A2"
    assert find_mapping_for_line(entries, "doc2", 1) is None
    assert find_mapping_for_line(entries, "doc1", 1, "OTHER") is None
