"""Точка входа: команды моста ЭДО ↔ iiko."""
import json
import sys


def _usage() -> str:
    return (
        "Использование:\n"
        "  python -m edo_iiko_bridge.cli fetch-incoming\n"
        "  python -m edo_iiko_bridge.cli fetch-document <messageId> <entityId>\n"
        "  python -m edo_iiko_bridge.cli list-products"
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
