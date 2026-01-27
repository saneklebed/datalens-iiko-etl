import os
import hashlib
from datetime import date

import pandas as pd
import psycopg2
from dotenv import load_dotenv

load_dotenv()

# --- ENV ---
NEON_HOST = os.getenv("NEON_HOST")
NEON_DB = os.getenv("NEON_DB")
NEON_USER = os.getenv("NEON_USER")
NEON_PASSWORD = os.getenv("NEON_PASSWORD")

REPORT_ID = os.getenv("REPORT_ID")
XLSX_PATH = os.getenv("XLSX_PATH")

# Период отчёта (пока фиксируем как в твоём примере)
DATE_FROM = date(2026, 1, 1)
DATE_TO = date(2026, 2, 1)

# --- Excel columns ---
COL_DEPARTMENT = "Торговое предприятие"
COL_PRODUCTNUM = "Артикул элемента номенклатуры"

TRANSACTION_COLS = {
    "Акт приготовления": "PRODUCTION",
    "Инвентаризация": "INVENTORY_CORRECTION",
    "Расходная накладная": "OUTGOING_INVOICE",
    "Реализация товаров": "SESSION_WRITEOFF",
    "Списание": "WRITEOFF",
}


def sha(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def norm(s: str) -> str:
    return " ".join(str(s).replace("\n", " ").split()).strip()


def to_sku_str(x) -> str:
    """
    00001 должен остаться 00001.
    1.0 -> 00001 (если это чисто число)
    """
    if pd.isna(x):
        return ""

    if isinstance(x, (int, float)):
        if float(x).is_integer():
            s = str(int(x))
        else:
            s = str(x)
    else:
        s = str(x).strip()

    s = s.strip()

    if s.isdigit():
        return s.zfill(5)

    return s


def to_num(x) -> float:
    """
    Поддержка русской локали: "53,500" -> 53.5
    Пусто/NaN -> 0
    """
    if pd.isna(x):
        return 0.0
    if isinstance(x, (int, float)):
        return float(x)

    s = str(x).strip()
    if s == "":
        return 0.0

    s = s.replace(" ", "").replace("\u00a0", "")
    s = s.replace(",", ".")
    try:
        return float(s)
    except Exception:
        return 0.0


def require_env():
    missing = []
    for k, v in {
        "NEON_HOST": NEON_HOST,
        "NEON_DB": NEON_DB,
        "NEON_USER": NEON_USER,
        "NEON_PASSWORD": NEON_PASSWORD,
        "REPORT_ID": REPORT_ID,
        "XLSX_PATH": XLSX_PATH,
    }.items():
        if not v:
            missing.append(k)
    if missing:
        raise RuntimeError("Missing in .env: " + ", ".join(missing))

    if not os.path.exists(XLSX_PATH):
        raise RuntimeError(f"Excel file not found: {XLSX_PATH}")


def main():
    require_env()

    print("XLSX_PATH =", XLSX_PATH)

    df = pd.read_excel(XLSX_PATH, engine="openpyxl")
    df.columns = [norm(c) for c in df.columns]

    need = [COL_DEPARTMENT, COL_PRODUCTNUM] + list(TRANSACTION_COLS.keys())
    missing_cols = [c for c in need if c not in df.columns]
    if missing_cols:
        raise RuntimeError(
            "Missing columns in Excel: "
            + ", ".join(missing_cols)
            + "\nAvailable columns: "
            + ", ".join([str(c) for c in df.columns])
        )

    rows = []

    # Проходим все строки файла
    for _, r in df.iterrows():
        department = str(r[COL_DEPARTMENT]).strip() if pd.notna(r[COL_DEPARTMENT]) else ""
        product_num = to_sku_str(r[COL_PRODUCTNUM])

        if not department or not product_num:
            continue

        # Каждая ненулевая транзакция превращается в отдельную строку
        for excel_col, tr_type in TRANSACTION_COLS.items():
            amount_out = to_num(r[excel_col])
            if amount_out == 0:
                continue

            source_hash = sha(
                f"{REPORT_ID}|{DATE_FROM}|{DATE_TO}|{department}|{product_num}|{tr_type}|{amount_out}"
            )

            rows.append(
                (
                    REPORT_ID,
                    DATE_FROM,
                    DATE_TO,
                    department,
                    product_num,
                    tr_type,
                    amount_out,
                    source_hash,
                )
            )

    print("Prepared rows:", len(rows))

    if not rows:
        print("Nothing to insert. Check Excel content / columns / period.")
        return

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
