#!/usr/bin/env python3
"""
Выгружает структуру таблиц Neon (inventory_raw, inventory_core, inventory_mart)
в docs/neon-tables.md для контекста AI и разработки.

Запуск:
  - Локально: из корня проекта с .env (NEON_HOST, NEON_DB, NEON_USER, NEON_PASSWORD).
  - Через GitHub: Actions → "Dump Neon schema" → Run workflow (берёт NEON_* из секретов и пушит обновлённый файл).
"""
import os
import sys
from pathlib import Path

# корень проекта = родитель папки scripts
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from dotenv import load_dotenv
load_dotenv(ROOT / ".env")

import psycopg2


def main():
    host = os.getenv("NEON_HOST")
    db = os.getenv("NEON_DB")
    user = os.getenv("NEON_USER")
    password = os.getenv("NEON_PASSWORD")
    if not all([host, db, user, password]):
        print("Задай NEON_HOST, NEON_DB, NEON_USER, NEON_PASSWORD в .env или запусти workflow Dump Neon schema в GitHub Actions (секреты).")
        sys.exit(1)

    conn = psycopg2.connect(
        host=host,
        dbname=db,
        user=user,
        password=password,
        sslmode="require",
    )

    sql = """
    SELECT table_schema, table_name, column_name, data_type, is_nullable
    FROM information_schema.columns
    WHERE table_schema IN ('inventory_raw', 'inventory_core', 'inventory_mart')
    ORDER BY table_schema, table_name, ordinal_position;
    """
    with conn.cursor() as cur:
        cur.execute(sql)
        rows = cur.fetchall()
    conn.close()

    # Группируем по schema -> table -> columns
    from collections import OrderedDict
    schema_tables = OrderedDict()
    for schema, table, column, dtype, nullable in rows:
        key = (schema, table)
        if key not in schema_tables:
            schema_tables[key] = []
        schema_tables[key].append((column, dtype, nullable))

    out_path = ROOT / "docs" / "neon-tables.md"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    lines = [
        "# Структура таблиц Neon (PostgreSQL)",
        "",
        "Сгенерировано скриптом `scripts/dump_neon_schema.py`. Обновить: запустить скрипт снова (нужен .env с NEON_*).",
        "",
        "---",
        "",
    ]

    prev_schema = None
    for (schema, table), cols in schema_tables.items():
        if schema != prev_schema:
            lines.append(f"## {schema}")
            lines.append("")
            prev_schema = schema
        lines.append(f"### {table}")
        lines.append("")
        lines.append("| Колонка | Тип | NULL |")
        lines.append("|---------|-----|------|")
        for col, dtype, nullable in cols:
            lines.append(f"| {col} | {dtype} | {nullable} |")
        lines.append("")
        lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Схема записана в {out_path}")


if __name__ == "__main__":
    main()
