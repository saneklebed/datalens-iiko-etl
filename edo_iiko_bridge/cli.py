"""Точка входа: команды моста ЭДО ↔ iiko."""
import json
import sys


def main() -> None:
    if len(sys.argv) < 2 or sys.argv[1] != "fetch-incoming":
        print("Использование: python -m edo_iiko_bridge.cli fetch-incoming", file=sys.stderr)
        sys.exit(1)
    try:
        from edo_iiko_bridge.config import Config
        from edo_iiko_bridge.clients import DiadocClient

        cfg = Config.from_env()
        client = DiadocClient(cfg.diadoc)
        box_id = client.get_default_box_id()
        print(f"Ящик: {box_id}", file=sys.stderr)
        docs = client.get_incoming_documents(box_id=box_id, limit=20)
        print(f"Входящих документов: {len(docs)}", file=sys.stderr)
        # Краткий вывод: индекс, тип, номер, дата (если есть)
        for i, d in enumerate(docs):
            doc_type = d.get("DocumentType") or d.get("TypeNamedId") or "?"
            doc_number = d.get("DocumentNumber") or ""
            # Метаданные могут быть в разных полях в зависимости от типа
            print(json.dumps({"index": i + 1, "type": doc_type, "documentNumber": doc_number, "messageId": d.get("MessageId"), "entityId": d.get("EntityId")}, ensure_ascii=False))
    except RuntimeError as e:
        print(f"Ошибка: {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Ошибка: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
