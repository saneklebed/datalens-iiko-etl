"""Разбор XML УПД (формат ФНС/Диадок): извлечение строк таблицы товаров."""
from __future__ import annotations

import xml.etree.ElementTree as ET
from dataclasses import dataclass
from typing import Any


@dataclass
class UpdLineItem:
    """Одна строка товара/услуги из УПД.

    product_code — артикул/код товара (КодТов, Артикул и т.д.).
    unit — единица измерения (код ОКЕИ или наименование: ОКЕИ_Тов, ЕдИзм, НаимЕдИзм и т.д.).
    """
    line_number: int
    name: str
    quantity: str
    unit: str          # единица измерения
    price: str
    sum_with_vat: str
    product_code: str  # артикул / код товара


def _local_name(el: ET.Element) -> str:
    return el.tag.split("}")[-1] if "}" in el.tag else el.tag


def _find_text(parent: ET.Element | None, *local_names: str) -> str:
    if parent is None:
        return ""
    for name in local_names:
        for child in parent:
            if _local_name(child) == name and child.text:
                return (child.text or "").strip()
    return ""


def _find_all_rows(root: ET.Element) -> list[ET.Element]:
    """Ищем контейнер таблицы товаров и все строки (СведТов/СвТов)."""
    rows: list[ET.Element] = []
    # Типичные контейнеры: ТаблСведТов, СведТов (множество дочерних СведТов/СвТов)
    for elem in root.iter():
        tag = _local_name(elem)
        if tag in ("СведТов", "СвТов"):
            # Строка должна содержать хотя бы наименование или количество
            if _find_text(elem, "НаимТов", "КолТов", "Количество"):
                rows.append(elem)
    return rows


def parse_upd_xml_line_items(xml_bytes: bytes) -> list[UpdLineItem]:
    """Парсит XML УПД (ФНС 5.02/5.03 и др.), возвращает список строк товаров."""
    try:
        root = ET.fromstring(xml_bytes)
    except ET.ParseError:
        return []
    rows = _find_all_rows(root)
    result: list[UpdLineItem] = []
    for i, row in enumerate(rows, start=1):
        name = _find_text(row, "НаимТов", "Наименование")
        qty = _find_text(row, "КолТов", "Количество")
        # Единица измерения: код ОКЕИ или наименование (ФНС: ОКЕИ_Тов, ЕдИзм, НаимЕдИзм, ЕдИзмПрослеж)
        unit = _find_text(
            row,
            "ОКЕИ_Тов", "ЕдИзм", "НаимЕдИзм", "НаимЕдИзмПрослеж",
            "ЕдиницаИзмерения", "ОКЕИ",
        )
        price = _find_text(row, "ЦенаТов", "Цена")
        sum_vat = _find_text(row, "СумНал", "СумСНал", "СуммаСНал", "Сумма")
        # Артикул / код товара (ФНС: КодТов; в накладных часто Артикул, НомТов)
        code = _find_text(row, "КодТов", "Артикул", "Код", "НомТов", "КодНоменклатуры")
        result.append(
            UpdLineItem(
                line_number=i,
                name=name or "",
                quantity=qty or "",
                unit=unit or "",
                price=price or "",
                sum_with_vat=sum_vat or "",
                product_code=code or "",
            )
        )
    return result
