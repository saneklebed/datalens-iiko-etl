"""Точка входа: команды моста ЭДО ↔ iiko."""
import json
import sys


def _usage() -> str:
    return (
        "Использование:\n"
        "  python -m edo_iiko_bridge.cli fetch-incoming\n"
        "  python -m edo_iiko_bridge.cli fetch-document <messageId> <entityId>\n"
        "  python -m edo_iiko_bridge.cli list-products\n"
        "  python -m edo_iiko_bridge.cli create-incoming "
        "<messageId> <entityId> <supplierId> <storeId> <documentNumber> <dateIncoming>"
    )


def cmd_fetch_incoming() -> None:
    from edo_iiko_bridge.config import Config
    from edo_iiko_bridge.clients import DiadocClient

    cfg = Config.from_env()
    client = DiadocClient(cfg.diadoc)
    box_id = client.get_default_box_id()
    print(f"Ящик: {box_id}", file=sys.stderr)
    docs = client.get_incoming_documents(box_id=box_id, limit=20)
    print(f"Входящих документов: {len(docs)}", file=sys.stderr)
    for i, d in enumerate(docs):
        doc_type = d.get("DocumentType") or d.get("TypeNamedId") or "?"
        doc_number = d.get("DocumentNumber") or ""
        print(
            json.dumps(
                {
                    "index": i + 1,
                    "type": doc_type,
                    "documentNumber": doc_number,
                    "messageId": d.get("MessageId"),
                    "entityId": d.get("EntityId"),
                },
                ensure_ascii=False,
            )
        )


def cmd_fetch_document(message_id: str, entity_id: str) -> None:
    from edo_iiko_bridge.config import Config
    from edo_iiko_bridge.clients import DiadocClient
    from edo_iiko_bridge.parsers import parse_upd_xml_line_items

    cfg = Config.from_env()
    client = DiadocClient(cfg.diadoc)
    box_id = client.get_default_box_id()
    content = client.get_entity_content(box_id, message_id, entity_id)
    print(f"Размер контента: {len(content)} байт", file=sys.stderr)
    lines = parse_upd_xml_line_items(content)
    print(f"Строк в УПД: {len(lines)}", file=sys.stderr)
    for item in lines:
        print(
            json.dumps(
                {
                    "lineNumber": item.line_number,
                    "name": item.name,
                    "quantity": item.quantity,
                    "unit": item.unit,
                    "price": item.price,
                    "sumWithVat": item.sum_with_vat,
                    "productCode": item.product_code,
                },
                ensure_ascii=False,
            )
        )


def cmd_list_products() -> None:
    """Список товаров iiko (id, название, артикул) для сопоставления с УПД."""
    from edo_iiko_bridge.config import Config
    from edo_iiko_bridge.clients import IikoRestoClient

    cfg = Config.from_env()
    client = IikoRestoClient(cfg.iiko)
    products = client.get_products()
    print(f"Товаров в номенклатуре: {len(products)}", file=sys.stderr)
    for p in products:
        print(json.dumps({"id": p["id"], "name": p["name"], "articul": p["articul"]}, ensure_ascii=False))


def cmd_create_incoming(
    message_id: str,
    entity_id: str,
    supplier_id: str,
    store_id: str,
    document_number: str,
    date_incoming: str,
) -> None:
    """Создать приходную накладную в iiko по УПД из Диадока.

    Пример:
      python -m edo_iiko_bridge.cli create-incoming <messageId> <entityId> <supplierId> <storeId> 123 2024-02-27
    """
    from pathlib import Path

    from edo_iiko_bridge.config import Config
    from edo_iiko_bridge.clients import DiadocClient, IikoRestoClient
    from edo_iiko_bridge.parsers import parse_upd_xml_line_items
    from edo_iiko_bridge.mapping_store import load_mapping, find_mapping_for_line
    from edo_iiko_bridge.incoming_invoice_builder import (
        IncomingInvoiceHeader,
        build_incoming_invoice_xml,
    )

    cfg = Config.from_env()

    # 1. Забираем XML УПД из Диадока
    diadoc_client = DiadocClient(cfg.diadoc)
    box_id = diadoc_client.get_default_box_id()
    content = diadoc_client.get_entity_content(box_id, message_id, entity_id)

    # 2. Парсим строки УПД
    items = parse_upd_xml_line_items(content)
    print(f"Строк в УПД: {len(items)}", file=sys.stderr)

    # 3. Загружаем маппинг «строка УПД ↔ товар iiko»
    mapping_entries = load_mapping(Path(cfg.mapping_file))
    document_key = f"{message_id}|{entity_id}"
    lines = []
    for item in items:
        m = find_mapping_for_line(
            mapping_entries,
            document_key=document_key,
            line_number=item.line_number,
            product_code_edo=item.product_code or None,
        )
        lines.append((item, m))

    mapped_count = sum(1 for _, m in lines if m is not None)
    print(f"Замапленных строк: {mapped_count}", file=sys.stderr)

    # 4. Собираем шапку и XML incomingInvoice
    header = IncomingInvoiceHeader(
        supplier_id=supplier_id,
        store_id=store_id,
        document_number=document_number,
        date_incoming=date_incoming,
    )
    xml_body = build_incoming_invoice_xml(header, lines)

    # 5. Отправляем в iiko
    iiko_client = IikoRestoClient(cfg.iiko)
    result = iiko_client.import_incoming_invoice(xml_body)

    print(json.dumps(result, ensure_ascii=False))


def main() -> None:
    if len(sys.argv) < 2:
        print(_usage(), file=sys.stderr)
        sys.exit(1)
    cmd = sys.argv[1]
    try:
        if cmd == "fetch-incoming":
            cmd_fetch_incoming()
        elif cmd == "fetch-document":
            if len(sys.argv) != 4:
                print("fetch-document требует messageId и entityId", file=sys.stderr)
                print(_usage(), file=sys.stderr)
                sys.exit(1)
            cmd_fetch_document(sys.argv[2], sys.argv[3])
        elif cmd == "list-products":
            cmd_list_products()
        elif cmd == "create-incoming":
            if len(sys.argv) != 8:
                print(
                    "create-incoming требует: messageId entityId supplierId storeId documentNumber dateIncoming",
                    file=sys.stderr,
                )
                print(_usage(), file=sys.stderr)
                sys.exit(1)
            cmd_create_incoming(
                sys.argv[2],
                sys.argv[3],
                sys.argv[4],
                sys.argv[5],
                sys.argv[6],
                sys.argv[7],
            )
        else:
            print(_usage(), file=sys.stderr)
            sys.exit(1)
    except RuntimeError as e:
        print(f"Ошибка: {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Ошибка: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
