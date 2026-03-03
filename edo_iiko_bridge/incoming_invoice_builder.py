"""Построение XML incomingInvoice для импорта прихода в iiko.

На входе:
- шапка накладной (supplier, склад, номера/даты, комментарий);
- строки УПД (UpdLineItem) + сопоставления с товарами iiko (MappingEntry).

На выходе: XML-строка `<document>...</document>` в формате incomingInvoiceDto.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable
import xml.etree.ElementTree as ET

from edo_iiko_bridge.parsers.upd import UpdLineItem
from edo_iiko_bridge.mapping_store import MappingEntry


@dataclass
class IncomingInvoiceHeader:
    """Минимальный набор полей для шапки приходной накладной iiko."""

    supplier_id: str
    store_id: str
    document_number: str
    date_incoming: str  # yyyy-MM-dd или dd.MM.yyyy (iiko поддерживает оба, но yyyy-MM-dd предпочтительнее)

    # Дополнительные, опциональные поля
    comment: str | None = None
    invoice_number: str | None = None  # номер счёт-фактуры
    due_date: str | None = None  # yyyy-MM-dd
    incoming_document_number: str | None = None
    incoming_date: str | None = None  # yyyy-MM-dd
    use_default_document_time: bool | None = None
    conception_id: str | None = None
    employee_pass_to_account_id: str | None = None
    transport_invoice_number: str | None = None


def build_incoming_invoice_xml(
    header: IncomingInvoiceHeader,
    lines: Iterable[tuple[UpdLineItem, MappingEntry | None]],
) -> str:
    """Собирает XML incomingInvoice (`<document>`) из шапки и строк.

    Каждый элемент `lines` — кортеж (строка УПД, сопоставление с товаром iiko).
    MappingEntry может быть None (строка пока не замаплена) — такие строки пропускаем.
    """
    root = ET.Element("document")

    # items / item
    items_el = ET.SubElement(root, "items")

    for upd_item, mapping in lines:
        if mapping is None:
            # Строка без сопоставленного товара iiko в приход не идёт
            continue

        item_el = ET.SubElement(items_el, "item")

        # Количество в основных единицах измерения
        _set_text(item_el, "amount", _as_decimal(upd_item.quantity))

        # Привязка к товару поставщика и товару в iiko
        if upd_item.product_code:
            _set_text(item_el, "supplierProductArticle", upd_item.product_code)
        if mapping.iiko_product_id:
            _set_text(item_el, "product", mapping.iiko_product_id)
        if mapping.iiko_articul:
            _set_text(item_el, "productArticle", mapping.iiko_articul)

        # Номер строки
        _set_text(item_el, "num", str(upd_item.line_number))

        # Базовая единица измерения и склад
        # amountUnit (guid) мы сейчас не знаем, поэтому не заполняем.
        if header.store_id:
            _set_text(item_el, "store", header.store_id)

        # Цена и суммы
        if upd_item.price:
            _set_text(item_el, "price", _as_decimal(upd_item.price))
        if upd_item.sum_with_vat:
            _set_text(item_el, "sum", _as_decimal(upd_item.sum_with_vat))

        # Фактическое количество обычно совпадает с amount
        if upd_item.quantity:
            _set_text(item_el, "actualAmount", _as_decimal(upd_item.quantity))

        # НДС (vatPercent / vatSum) сейчас не заполняем — пусть берётся из карточки товара.

    # --- Шапка документа ---
    _set_text(root, "conception", header.conception_id)
    _set_text(root, "comment", header.comment)
    _set_text(root, "documentNumber", header.document_number)
    _set_text(root, "dateIncoming", header.date_incoming)
    _set_text(root, "invoice", header.invoice_number)
    _set_text(root, "defaultStore", header.store_id)
    _set_text(root, "supplier", header.supplier_id)
    _set_text(root, "dueDate", header.due_date)
    _set_text(root, "incomingDocumentNumber", header.incoming_document_number)
    _set_text(root, "employeePassToAccount", header.employee_pass_to_account_id)
    _set_text(root, "transportInvoiceNumber", header.transport_invoice_number)

    # incomingDate: если не задана, iiko возьмёт её из dateIncoming
    _set_text(root, "incomingDate", header.incoming_date)

    if header.use_default_document_time is not None:
        _set_text(root, "useDefaultDocumentTime", "true" if header.use_default_document_time else "false")

    # Сериализация XML
    xml_bytes = ET.tostring(root, encoding="utf-8")
    return xml_bytes.decode("utf-8")


def _set_text(parent: ET.Element, name: str, value: str | None) -> None:
    if value is None:
        return
    text = str(value).strip()
    if not text:
        return
    el = ET.SubElement(parent, name)
    el.text = text


def _as_decimal(value: str) -> str:
    """Небольшая нормализация числовых строк (замена запятой на точку, трим пробелов)."""
    v = (value or "").strip()
    if not v:
        return v
    return v.replace(",", ".")


__all__ = ["IncomingInvoiceHeader", "build_incoming_invoice_xml"]

