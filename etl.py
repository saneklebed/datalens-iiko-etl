import os
import hashlib
from datetime import date

import pandas as pd
import psycopg2
from dotenv import load_dotenv

load_dotenv()

# Neon
NEON_HOST = os.getenv("NEON_HOST")
NEON_DB = os.getenv("NEON_DB")
NEON_USER = os.getenv("NEON_USER")
NEON_PASSWORD = os.getenv("NEON_PASSWORD")

# Meta
REPORT_ID = os.getenv("REPORT_ID")
XLSX_PATH = os.getenv("XLSX_PATH")


def sha(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def norm(s: str) -> str:
    # нормализуем заголовки: лишние пробелы/переводы строк
    return " ".join(str(s).replace("\n", " ").split()).strip()


def main():
    # --- env validation ---
    for k, v in {
        "NEON_HOST": NEON_HOST,
        "NEON_DB": NEON_DB,
        "NEON_USER": NEON_USER,
        "NEON_PASSWORD": NEON_PASSWORD,
        "REPORT_ID": REPORT_ID,
        "XLSX_PATH": XLSX_PATH,
    }.items():
        if not v:
            raise RuntimeError(f"{k} is missing in .env")

    if not os.path.exists(XLSX_PATH):
        raise RuntimeError(f"Excel file not found: {XLSX_PATH}")

    print("NEON_HOST =", NEON_HOST)
    print("NEON_DB   =", NEON_DB)
    print("NEON_USER =", NEON_USER)
    print("PWD len   =", len(NEON_PASSWORD))
    print("XLSX_PATH =", XLSX_PATH)

    # --- period (пока фикс, потом сделаем параметром) ---
    date_from = date(2026, 1, 1)
    date_to = date(2026, 2, 1)

    # --- expected columns ---
    COL_DEPARTMENT = "Торговое предприятие"
    COL_PRODUCTNUM = "Артикул элемента номенклатуры"

    TRANSACTION_COLS = {
        "Акт приготовления": "PRODUCTION",
        "Инвентаризация": "INVENTORY_CORRECTION",
        "Расходная накладная": "OUTGOING_INVOICE",
        "Реализация товаров": "SESSION_WRITEOFF",
        "Списание": "WRITEOFF",
    }

    # --- read excel ---
    df = pd.read_excel(XLSX_PATH, engine="openpyxl")

    # нормализуем заголовки (иногда Excel приносит лишние пробелы/переносы)
    df.columns = [norm(c) for c in df.columns]

    # проверка наличия нужных колонок
    need = [COL_DEPARTMENT, COL_PRODUCTNUM] + list(TRANSACTION_COLS.keys())
    missing = [c for c in need if c not in df.columns]
    if missing:
        raise RuntimeError(
            "Missing columns in Excel: " + ", ".join(missing) +
            "\nAvailable columns: " + ", ".join([str(c) for c in df.columns])
        )

    rows = []

    # iterrows надёжнее для русских колонок
    for _, row in df.iterrows():
        department = str(row[COL_DEPARTMENT]).strip() if pd.notna(row[COL_DEPARTMENT]) else ""
        product_num = str(row[COL_PRODUCTNUM]).strip() if pd.notna(row[COL_PRODUCTNUM]) else ""

        if not department or not product_num:
            continue

        for col_name, tr_type in TRANSACTION_COLS.items():
            v = row[col_name]
            if pd.isna(v):
                continue

            amount_out = float(v)
            if amount_out == 0:
                continue

            source_hash = sha(
                f"{REPORT_ID}|{date_from}|{date_to}|{department}|{product_num}|{tr_type}|{amount_out}"
            )

            rows.append(
                (
                    REPORT_ID,
                    date_from,
                    date_to,
                    department,
                    product_num,
                    tr_type,
                    amount_out,
                    source_hash,
                )
            )

    print(f"Prepared rows: {len(rows)}")

    # --- insert ---
    with psycopg2.connect(
        host=NEON_HOST,
        dbname=NEON_DB,
        user=NEON_USER,
        password=NEON_PASSWORD,
        sslmode="require",
        channel_binding="require",
    ) as conn:
        with conn.cursor() as cur:
            cur.executemany(
                """
                insert into raw.olap_postings
                (report_id, date_from, date_to, department, product_num, transaction_type, amount_out, source_hash)
                values (%s,%s,%s,%s,%s,%s,%s,%s)
                """,
                rows,
            )
        conn.commit()

    print(f"OK: inserted {len(rows)} rows into raw.olap_postings")


if __name__ == "__main__":
    main()
