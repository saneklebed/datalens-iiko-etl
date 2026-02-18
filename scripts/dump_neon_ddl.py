#!/usr/bin/env python3
"""
Выгружает полный DDL схем Neon (inventory_raw, inventory_core, inventory_mart)
в docs/neon-schema.sql: CREATE TABLE, CREATE VIEW, функции и т.д.
Нужен для контекста AI и возможности править объекты по коду.

Запуск:
  - Локально: из корня проекта с .env (NEON_*). Нужен pg_dump в PATH (PostgreSQL client).
  - Через GitHub: workflow "Dump Neon schema" можно расширить или запустить этот скрипт отдельно.
"""
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from dotenv import load_dotenv
load_dotenv(ROOT / ".env")


def main():
    host = os.getenv("NEON_HOST")
    db = os.getenv("NEON_DB")
    user = os.getenv("NEON_USER")
    password = os.getenv("NEON_PASSWORD")
    if not all([host, db, user, password]):
        print("Задай NEON_HOST, NEON_DB, NEON_USER, NEON_PASSWORD в .env или в секретах workflow.")
        sys.exit(1)

    out_path = ROOT / "docs" / "neon-schema.sql"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # pg_dump с SSL (Neon требует sslmode=require)
    env = {**os.environ, "PGPASSWORD": password, "PGSSLMODE": "require"}
    cmd = [
        "pg_dump",
        "-h", host,
        "-U", user,
        "-d", db,
        "--schema-only",
        "--no-owner",
        "--no-privileges",
        "-n", "inventory_raw",
        "-n", "inventory_core",
        "-n", "inventory_mart",
        "-f", str(out_path),
    ]

    try:
        subprocess.run(cmd, env=env, check=True)
        print(f"DDL записан в {out_path}")
    except FileNotFoundError:
        print("pg_dump не найден. Установи PostgreSQL client (например: apt install postgresql-client, или клиент из установщика Postgres).")
        sys.exit(1)
    except subprocess.CalledProcessError as e:
        print(f"pg_dump завершился с ошибкой: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
