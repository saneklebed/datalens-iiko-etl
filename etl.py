import os
import json
import hashlib
import uuid
from dataclasses import dataclass
from datetime import datetime, date
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import requests
from dotenv import load_dotenv

import psycopg2
from psycopg2.extras import execute_values


# -----------------------------
# Config
# -----------------------------

@dataclass
class Config:
    # Neon
    neon_host: str
    neon_db: str
    neon_user: str
    neon_password: str

    # Report
    report_id: str
    date_from: str  # YYYY-MM-DD
    date_to: str    # YYYY-MM-DD (верхняя граница, как правило "до", не включая)

    # iiko
    iiko_base_url: str
    iiko_key: str
    iiko_verify_ssl: bool

    # Filters
    transaction_types: List[str]      # e.g. PRODUCTION, INVENTORY_CORRECTION...
    product_types: List[str]          # e.g. GOODS, PREPARED

    # Files
    raw_dir: Path


def _env(name: str, default: Optional[str] = None) -> str:
    v = os.getenv(name, default)
    if v is None or str(v).strip() == "":
        raise RuntimeError(f"Missing env var: {name}")
    return str(v).strip()


def _env_list(name: str, default: str = "") -> List[str]:
    raw = os.getenv(name, default) or ""
    # allow comma/semicolon separated
    parts = [p.strip() for p in raw.replace(";", ",").split(",") if p.strip()]
    return parts


def load_config() -> Config:
    # .env рядом с файлом или в текущей папке
    load_dotenv()

    verify_ssl_raw = os.getenv("IIKO_VERIFY_SSL", "1").strip()
    verify_ssl = not (verify_ssl_raw in ("0", "false", "False", "no", "NO"))

    raw_dir = Path(os.getenv("RAW_DIR", "src/data/raw")).resolve()

    transaction_types = _env_list("TRANSACTION_TYPES")
    product_types = _env_list("PRODUCT_TYPES")

    cfg = Config(
        neon_host=_env("NEON_HOST"),
        neon_db=_env("NEON_DB"),
        neon_user=_env("NEON_USER"),
        neon_password=_env("NEON_PASSWORD"),

        report_id=_env("REPORT_ID"),
        date_from=_env("DATE_FROM"),
        date_to=_env("DATE_TO"),

        iiko_base_url=_env("IIKO_BASE_URL").rstrip("/"),
        iiko_key=_env("IIKO_KEY"),
        iiko_verify_ssl=verify_ssl,

        transaction_types=transaction_types,
        product_types=product_types,

        raw_dir=raw_dir,
    )

    if not cfg.transaction_types:
        raise RuntimeError("TRANSACTION_TYPES is empty. Fill it in .env")
    if not cfg.product_types:
        raise RuntimeError("PRODUCT_TYPES is empty. Fill it in .env")

    return cfg


# -----------------------------
# iiko OLAP
# -----------------------------

def build_olap_request(cfg: Config) -> Dict[str, Any]:
    """
    Формируем OLAP v2 TRANSACTIONS: строки (Department, Product.Num, TransactionType)
    агрегация Amount.Out, фильтры по дате + типам транзакций + типам номенклатуры.
    """
    # iiko ждёт datetime с миллисекундами
    dt_from = f"{cfg.date_from}T00:00:00.000"
    dt_to = f"{cfg.date_to}T00:00:00.000"

    body = {
        "reportType": "TRANSACTIONS",
        "buildSummary": False,
        "groupByRowFields": [
            "Department",
            "Product.Num",
            "TransactionType",
        ],
        "groupByColFields": [],
        "aggregateFields": [
            "Amount.Out",
        ],
        "filters": {
            # В твоём отчёте из iikoOffice фигурирует DateTime.OperDayFilter — его и используем
            "DateTime.OperDayFilter": {
                "filterType": "DateRange",
                "periodType": "CUSTOM",
                "from": dt_from,
                "to": dt_to,
                "includeLow": True,
                "includeHigh": False,
            },
            "TransactionType": {
                "filterType": "IncludeValues",
                "values": cfg.transaction_types,
            },
            "Product.Type": {
                "filterType": "IncludeValues",
                "values": cfg.product_types,
            },
        },
    }
    return body


def fetch_olap(cfg: Config, body: Dict[str, Any]) -> Dict[str, Any]:
    url = f"{cfg.iiko_base_url}/resto/api/v2/reports/olap?key={cfg.iiko_key}"

    headers = {"Content-Type": "application/json; charset=utf-8"}
    resp = requests.post(url, headers=headers, json=body, verify=cfg.iiko_verify_ssl, timeout=180)

    # дебаг на ошибках
    if resp.status_code != 200:
        raise RuntimeError(f"OLAP request failed: {resp.status_code} {resp.text[:2000]}")

    return resp.json()


def save_raw(cfg: Config, request_body: Dict[str, Any], response_json: Dict[str, Any]) -> Tuple[Path, Path]:
    cfg.raw_dir.mkdir(parents=True, exist_ok=True)

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    rnd = uuid.uuid4().hex[:8]
    base = f"{cfg.report_id}_{cfg.date_from}_{cfg.date_to}_{rnd}_{ts}"

    req_path = cfg.raw_dir / f"olap_request_{base}.json"
    resp_path = cfg.raw_dir / f"olap_response_{base}.json"

    req_path.write_text(json.dumps(request_body, ensure_ascii=False, indent=2), encoding="utf-8")
    resp_path.write_text(json.dumps(response_json, ensure_ascii=False), encoding="utf-8")

    return req_path, resp_path


# -----------------------------
# Cleaning / normalization
# -----------------------------

TOTAL_MARKERS = ("итого", "всего")


def _has_totals_marker(val: Optional[str]) -> bool:
    if val is None:
        return False
    s = str(val).strip().lower()
    if not s:
        return False
    return any(m in s for m in TOTAL_MARKERS)


def is_total_row(department: Optional[str], product_num: Optional[str], transaction_type: Optional[str]) -> bool:
    """
    Режем любые строки, где встречается "всего"/"итого" в любом из полей.
    Это перекрывает:
      - "Списание всего" (подитог по типу транзакции)
      - "Домодедово всего" (подитог по филиалу)
      - "Итого" (глобальный итог)
    """
    return (
        _has_totals_marker(department)
        or _has_totals_marker(product_num)
        or _has_totals_marker(transaction_type)
    )


def normalize_rows(cfg: Config, olap_json: Dict[str, Any]) -> Tuple[List[Dict[str, Any]], Dict[str, int]]:
    data = olap_json.get("data") or []
    out: List[Dict[str, Any]] = []

    skipped = {
        "missing_department": 0,
        "missing_product": 0,
        "missing_tr_type": 0,
        "totals_rows": 0,
        "bad_amount": 0,
    }

    for r in data:
        dept = r.get("Department")
        prod = r.get("Product.Num")
        trt = r.get("TransactionType")
        amt = r.get("Amount.Out")

        # режем итоги
        if is_total_row(dept, prod, trt):
            skipped["totals_rows"] += 1
            continue

        # обязательные поля
        if dept is None or str(dept).strip() == "":
            skipped["missing_department"] += 1
            continue
        if prod is None or str(prod).strip() == "":
            skipped["missing_product"] += 1
            continue
        if trt is None or str(trt).strip() == "":
            skipped["missing_tr_type"] += 1
            continue

        # amount
        try:
            amount_out = float(amt) if amt is not None else 0.0
        except Exception:
            skipped["bad_amount"] += 1
            continue

        department = str(dept).strip()
        product_num = str(prod).strip()
        transaction_type = str(trt).strip()

        # hash для дедупликации
        payload = {
            "report_id": cfg.report_id,
            "date_from": cfg.date_from,
            "date_to": cfg.date_to,
            "department": department,
            "product_num": product_num,
            "transaction_type": transaction_type,
            "amount_out": round(amount_out, 10),
        }
        source_hash = hashlib.sha256(
            json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest()

        out.append({
            "report_id": cfg.report_id,
            "date_from": cfg.date_from,
            "date_to": cfg.date_to,
            "department": department,
            "product_num": product_num,
            "transaction_type": transaction_type,
            "amount_out": amount_out,
            "source_hash": source_hash,
        })

    return out, skipped


# -----------------------------
# DB
# -----------------------------

def db_connect(cfg: Config):
    # Neon любит sslmode=require.
    # ВАЖНО: никаких options/statement_timeout на подключении к pooled endpoint.
    dsn = (
        f"host={cfg.neon_host} "
        f"dbname={cfg.neon_db} "
        f"user={cfg.neon_user} "
        f"password={cfg.neon_password} "
        f"sslmode=require"
    )
    return psycopg2.connect(dsn)


def insert_rows(cfg: Config, rows: List[Dict[str, Any]]) -> int:
    if not rows:
        return 0

    sql = """
        INSERT INTO raw.olap_postings
        (report_id, date_from, date_to, department, product_num, transaction_type, amount_out, source_hash, loaded_at)
        VALUES %s
        ON CONFLICT (source_hash) DO NOTHING;
    """

    values = [
        (
            r["report_id"],
            r["date_from"],
            r["date_to"],
            r["department"],
            r["product_num"],
            r["transaction_type"],
            r["amount_out"],
            r["source_hash"],
            datetime.utcnow(),
        )
        for r in rows
    ]

    with db_connect(cfg) as conn:
        with conn.cursor() as cur:
            # before
            cur.execute("SELECT COUNT(*) FROM raw.olap_postings;")
            before = cur.fetchone()[0]
            print(f"[db] before: {before}")

            print("[db] inserting (execute_values)...")
            execute_values(cur, sql, values, page_size=1000)
            conn.commit()

            cur.execute("SELECT COUNT(*) FROM raw.olap_postings;")
            after = cur.fetchone()[0]
            print(f"[db] after:  {after}")

    inserted = max(0, after - before)
    return inserted


# -----------------------------
# Main
# -----------------------------

def main():
    cfg = load_config()

    # Если ты работаешь с self-signed/проксируемым https и отключил verify_ssl — подавим warnings
    if not cfg.iiko_verify_ssl:
        try:
            import urllib3
            urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
        except Exception:
            pass

    print(f"[auth] using key: {cfg.iiko_key}")

    body = build_olap_request(cfg)
    resp = fetch_olap(cfg, body)

    req_path, resp_path = save_raw(cfg, body, resp)
    print(f"[files] request saved:  {req_path}")
    print(f"[files] response saved: {resp_path}")

    rows, skipped = normalize_rows(cfg, resp)
    print(f"[olap] rows from API: {len(resp.get('data') or [])}")
    print(f"[prep] rows prepared: {len(rows)}")
    print(f"[prep] skipped: {skipped}")

    print("[db] connecting to Neon...")
    inserted = insert_rows(cfg, rows)
    print(f"[done] inserted rows: {inserted}")


if __name__ == "__main__":
    main()
