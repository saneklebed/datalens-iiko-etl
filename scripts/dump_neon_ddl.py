#!/usr/bin/env python3
"""
Выгружает DDL объектов Neon (inventory_raw, inventory_core, inventory_mart)
в docs/neon-schema.sql: приблизительные CREATE TABLE (по колонкам),
CREATE VIEW / MATERIALIZED VIEW и функции.

Не использует pg_dump, только SQL через psycopg2, поэтому не зависит
от версии клиента PostgreSQL.

Запуск:
  - Локально: из корня проекта с .env (NEON_HOST, NEON_DB, NEON_USER, NEON_PASSWORD).
  - Через GitHub: workflow "Dump Neon schema" вызывает этот скрипт.
"""
import os
import sys
from pathlib import Path
from collections import defaultdict

import psycopg2
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

load_dotenv(ROOT / ".env")


SCHEMAS = ("inventory_raw", "inventory_core", "inventory_mart")


def main() -> None:
    host = os.getenv("NEON_HOST")
    db = os.getenv("NEON_DB")
    user = os.getenv("NEON_USER")
    password = os.getenv("NEON_PASSWORD")
    if not all([host, db, user, password]):
        print("Задай NEON_HOST, NEON_DB, NEON_USER, NEON_PASSWORD в .env или в секретах workflow.")
        sys.exit(1)

    conn = psycopg2.connect(
        host=host,
        dbname=db,
        user=user,
        password=password,
        sslmode="require",
    )

    out_path = ROOT / "docs" / "neon-schema.sql"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    lines: list[str] = []
    lines.append("-- Автогенерация: DDL объектов Neon для схем inventory_raw, inventory_core, inventory_mart")
    lines.append("-- Источник: scripts/dump_neon_ddl.py")
    lines.append("")

    with conn.cursor() as cur:
        # Таблицы: собираем колонки и приблизительный CREATE TABLE
        cur.execute(
            """
            SELECT table_schema, table_name, column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = ANY(%s)
            ORDER BY table_schema, table_name, ordinal_position;
            """,
            (list(SCHEMAS),),
        )
        table_cols = defaultdict(list)
        for schema, table, col, dtype, nullable in cur.fetchall():
            table_cols[(schema, table)].append((col, dtype, nullable))

        if table_cols:
            lines.append("-- === TABLES (approximate CREATE TABLE from information_schema) ===")
            lines.append("")
            for (schema, table), cols in sorted(table_cols.items()):
                lines.append(f"CREATE TABLE {schema}.{table} (")
                col_lines = []
                for col, dtype, nullable in cols:
                    nn = " NOT NULL" if nullable == "NO" else ""
                    col_lines.append(f"    {col} {dtype}{nn}")
                lines.append(",\n".join(col_lines))
                lines.append(");")
                lines.append("")

        # Вьюхи и матвьюхи: pg_get_viewdef
        cur.execute(
            """
            SELECT n.nspname AS schema_name,
                   c.relname AS view_name,
                   pg_get_viewdef(c.oid, true) AS definition,
                   c.relkind
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = ANY(%s)
              AND c.relkind IN ('v', 'm')
            ORDER BY n.nspname, c.relname;
            """,
            (list(SCHEMAS),),
        )
        views = cur.fetchall()
        if views:
            lines.append("-- === VIEWS / MATERIALIZED VIEWS ===")
            lines.append("")
            for schema, name, definition, relkind in views:
                kind = "MATERIALIZED VIEW" if relkind == "m" else "VIEW"
                lines.append(f"CREATE {kind} {schema}.{name} AS")
                lines.append(definition.rstrip(";"))
                lines.append(";")
                lines.append("")

        # Функции: pg_get_functiondef
        cur.execute(
            """
            SELECT n.nspname AS schema_name,
                   p.proname AS func_name,
                   pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = ANY(%s)
            ORDER BY n.nspname, p.proname;
            """,
            (list(SCHEMAS),),
        )
        funcs = cur.fetchall()
        if funcs:
            lines.append("-- === FUNCTIONS ===")
            lines.append("")
            for schema, name, definition in funcs:
                lines.append(definition.rstrip())
                lines.append("")

    conn.close()

    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"DDL (приблизительный) записан в {out_path}")


if __name__ == "__main__":
    main()
