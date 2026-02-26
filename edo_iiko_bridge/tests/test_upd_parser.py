"""Тесты парсера УПД (XML → строки товаров)."""
import pytest

from edo_iiko_bridge.parsers.upd import UpdLineItem, parse_upd_xml_line_items


def test_parse_empty_or_invalid_returns_empty_list():
    assert parse_upd_xml_line_items(b"") == []
    assert parse_upd_xml_line_items(b"<root/>") == []
    assert parse_upd_xml_line_items(b"not xml") == []


def test_parse_upd_with_svedtov_extracts_lines():
    xml = """<?xml version="1.0" encoding="UTF-8"?>
    <Doc>
        <СведТов>
            <НаимТов>Milk</НаимТов>
            <КолТов>10</КолТов>
            <ОКЕИ_Тов>l</ОКЕИ_Тов>
            <ЦенаТов>80.50</ЦенаТов>
            <СумНал>805.00</СумНал>
        </СведТов>
        <СведТов>
            <НаимТов>Bread</НаимТов>
            <КолТов>2</КолТов>
            <ЦенаТов>45</ЦенаТов>
        </СведТов>
    </Doc>
    """.encode("utf-8")
    lines = parse_upd_xml_line_items(xml)
    assert len(lines) == 2
    assert lines[0].line_number == 1
    assert lines[0].name == "Milk"
    assert lines[0].quantity == "10"
    assert lines[0].unit == "l"
    assert lines[0].price == "80.50"
    assert lines[0].sum_with_vat == "805.00"
    assert lines[1].name == "Bread"
    assert lines[1].quantity == "2"
    assert lines[1].price == "45"


def test_parse_upd_extracts_articul_and_unit():
    """Артикул (product_code) и единица измерения (unit) обязательно отдаются в выводе."""
    xml = """<?xml version="1.0" encoding="UTF-8"?>
    <Doc>
        <СведТов>
            <НаимТов>Item</НаимТов>
            <КодТов>ART-12345</КодТов>
            <ОКЕИ_Тов>796</ОКЕИ_Тов>
            <КолТов>2</КолТов>
        </СведТов>
        <СведТов>
            <НаимТов>Another</НаимТов>
            <Артикул>SKU-999</Артикул>
            <ЕдИзм>кг</ЕдИзм>
            <КолТов>1</КолТов>
        </СведТов>
    </Doc>
    """.encode("utf-8")
    lines = parse_upd_xml_line_items(xml)
    assert len(lines) == 2
    assert lines[0].product_code == "ART-12345"
    assert lines[0].unit == "796"
    assert lines[1].product_code == "SKU-999"
    assert lines[1].unit == "кг"


def test_parse_upd_with_namespace():
    xml = """<?xml version="1.0"?>
    <doc xmlns:ns="urn:fns">
        <ns:СведТов>
            <ns:НаимТов>Item A</ns:НаимТов>
            <ns:КолТов>1</ns:КолТов>
        </ns:СведТов>
    </doc>
    """.encode("utf-8")
    lines = parse_upd_xml_line_items(xml)
    assert len(lines) == 1
    assert lines[0].name == "Item A"
    assert lines[0].quantity == "1"
