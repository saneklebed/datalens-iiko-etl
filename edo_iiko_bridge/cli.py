"""Точка входа: команды моста ЭДО ↔ iiko."""
import sys


def main() -> None:
    if len(sys.argv) < 2 or sys.argv[1] != "fetch-incoming":
        print("Использование: python -m edo_iiko_bridge.cli fetch-incoming", file=sys.stderr)
        sys.exit(1)
    try:
        from edo_iiko_bridge.config import Config
        cfg = Config.from_env()
        print("Конфиг загружен (Диадок, iiko). Получение входящих из Диадока — TODO.")
    except RuntimeError as e:
        print(f"Ошибка: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
