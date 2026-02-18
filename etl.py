import os
import json
import hashlib
from dataclasses import dataclass
from datetime import datetime, timezone, date, timedelta
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple
from zoneinfo import ZoneInfo

import requests
from dotenv import load_dotenv

import psycopg2
from psycopg2.extras import execute_values


# =============================
# Config
# =============================

@dataclass
class Config:
    neon_host: str
    neon_db: str
    neon_user: str
    neon_password: str

    report_id: str
    date_from: str
    date_to: str

    iiko_base_url: str
    iiko_login: str
    iiko_pass_sha1: str
    iiko_verify_ssl: bool

    transaction_types: List[str]
    product_types: List[str]

    raw_dir: Path


def _env(name: str) -> str:
    v = os.getenv(name)
    if not v or not str(v).strip():
        raise RuntimeError(f"Missing env var: {name}")
    return str(v).strip()


def _env_list(name: str) -> List[str]:
    raw = os.getenv(name, "") or ""
    return [p.strip() for p in raw.replace(";", ",").split(",") if p.strip()]


def _verify_ssl() -> bool:
    raw = os.getenv("IIKO_VERIFY_SSL", "1").strip()
    return raw not in ("0", "false", "False", "no", "NO")


# =============================
# Period logic (ВОТ ТУТ ЕДИНСТВЕННОЕ ИЗМЕНЕНИЕ)
# =============================

def last_closed_week_tue_to_tue(today: Optional[date] = None) -> Tuple[str, str]:
    if today is None:
        today = datetime.now().date()

    # Tue = 1
    weekday = today.weekday()
    days_since_tue = (weekday - 1) % 7
    this_tuesday = today - timedelta(days=days_since_tue)

    start = this_tuesday - timedelta(days=7)
    end = this_tuesday

    return start.isoformat(), end.isoformat()


def _parse_optional_date(name: str) -> Optional[str]:
    raw = os.getenv(name, "").strip()
    if not raw:
        return None
    try:
        datetime.strptime(raw, "%Y-%m-%d")
        return raw
    except ValueError:
        raise RuntimeError(f"Env {name} must be YYYY-MM-DD, got: {raw!r}")


def load_config() -> Config:
    load_dotenv()

    date_from_env = _parse_optional_date("DATE_FROM")
    date_to_env = _parse_optional_date("DATE_TO")

    if date_from_env and date_to_env:
        date_from, date_to = date_from_env, date_to_env
    elif date_from_env or date_to_env:
        raise RuntimeError("Set BOTH DATE_FROM and DATE_TO, or none")
    else:
        date_from, date_to = last_closed_week_tue_to_tue()

    return Config(
        neon_host=_env("NEON_HOST"),
        neon_db=_env("NEON_DB"),
        neon_user=_env("NEON_USER"),
        neon_password=_env("NEON_PASSWORD"),

        report_id=_env("REPORT_ID"),
        date_from=date_from,
        date_to=date_to,

        iiko_base_url=_env("IIKO_BASE_URL").rstrip("/"),
        iiko_login=_env("IIKO_LOGIN"),
        iiko_pass_sha1=_env("IIKO_PASS_SHA1"),
        iiko_verify_ssl=_verify_ssl(),

        transaction_types=_env_list("TRANSACTION_TYPES"),
        product_types=_env_list("PRODUCT_TYPES"),

        raw_dir=Path(os.getenv("RAW_DIR", "src/data/raw")).resolve(),
    )


# =============================
# iiko auth
# =============================

def get_iiko_key(cfg: Config) -> str:
    base = cfg.iiko_base_url.rstrip("/")
    login = cfg.iiko_login.strip()
    sha1 = cfg.iiko_pass_sha1.strip().lower()

    endpoints = [
        f"{base}/api/auth",
        f"{base}/resto/api/auth",
    ]

    for url in endpoints:
        resp = requests.get(
            url,
            params={"login": login, "pass": sha1},
            verify=cfg.iiko_verify_ssl,
            timeout=30,
        )
        if resp.status_code == 200 and resp.text.strip():
            return resp.text.strip()

    raise RuntimeError("iiko auth failed")


# =============================
# OLAP
# =============================

def build_olap_request(cfg: Config) -> Dict[str, Any]:
    return {
        "reportType": "TRANSACTIONS",
        "buildSummary": False,
        "groupByRowFields": [
            "Department",
            "DateTime.Typed",
            "TransactionType",
            "Product.Num",
            "Product.Name",
            "Product.Category",
            "Product.MeasureUnit",
            "Contr-Account.Name",
        ],
        "groupByColFields": [],
        "aggregateFields": [
            "Amount.Out",
            "Amount.In",
            "Sum.Outgoing",
            "Sum.Incoming",
        ],
        "filters": {
            "DateTime.OperDayFilter": {
                "filterType": "DateRange",
                "periodType": "CUSTOM",
                "from": f"{cfg.date_from}T00:00:00.000",
                "to": f"{cfg.date_to}T00:00:00.000",
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


def fetch_olap(cfg: Config, body: Dict[str, Any]) -> Dict[str, Any]:
    key = get_iiko_key(cfg)
    resp = requests.post(
        f"{cfg.iiko_base_url}/resto/api/v2/reports/olap?key={key}",
        json=body,
        headers={"Content-Type": "application/json"},
        verify=cfg.iiko_verify_ssl,
        timeout=180,
    )
    if resp.status_code != 200:
        raise RuntimeError(resp.text)
    return resp.json()


# =============================
# Normalize
# =============================

def parse_posting_dt(raw: str) -> datetime:
    s = raw.strip()
    if s.endswith("Z"):
        s = s.replace("Z", "+00:00")
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=ZoneInfo("Europe/Moscow"))
    return dt


def normalize(cfg: Config, data: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    rows = []

    for r in data:
        try:
            posting_dt = parse_posting_dt(r["DateTime.Typed"])
            amount_out = float(r.get("Amount.Out") or 0)
            amount_in = float(r.get("Amount.In") or 0)
            sum_outgoing = float(r.get("Sum.Outgoing") or 0)
            sum_incoming = float(r.get("Sum.Incoming") or 0)
        except Exception:
            continue

        posting_norm = posting_dt.astimezone(timezone.utc).isoformat()

        payload = {
            "report_id": cfg.report_id,
            "date_from": cfg.date_from,
            "date_to": cfg.date_to,
            "department": str(r["Department"]).strip(),
            "posting_dt": posting_dt,
            "product_num": str(r["Product.Num"]).strip(),
            "product_name": str(r.get("Product.Name") or "").strip(),
            "product_category": str(r.get("Product.Category") or "").strip(),
            "product_measure_unit": str(r.get("Product.MeasureUnit") or "").strip(),
            "contr_account_name": str(r.get("Contr-Account.Name") or "").strip(),
            "transaction_type": str(r["TransactionType"]).strip(),
            "amount_out": amount_out,
            "amount_in": amount_in,
            "sum_outgoing": sum_outgoing,
            "sum_incoming": sum_incoming,
        }

        source_hash = hashlib.sha256(
            json.dumps({**payload, "posting_dt": posting_norm}, sort_keys=True).encode()
        ).hexdigest()

        payload["source_hash"] = source_hash
        rows.append(payload)

    return rows


# =============================
# DB
# =============================

def db_connect(cfg: Config):
    return psycopg2.connect(
        host=cfg.neon_host,
        dbname=cfg.neon_db,
        user=cfg.neon_user,
        password=cfg.neon_password,
        sslmode="require",
    )


def delete_period(cfg: Config) -> int:
    """Удаляет из RAW все строки за период (report_id, date_from, date_to). Возвращает число удалённых строк."""
    sql = """
    delete from inventory_raw.olap_postings
    where report_id = %s and date_from = %s and date_to = %s;
    """
    with db_connect(cfg) as conn:
        with conn.cursor() as cur:
            cur.execute(sql, (cfg.report_id, cfg.date_from, cfg.date_to))
            deleted = cur.rowcount
            conn.commit()
    return deleted


def insert_rows(cfg: Config, rows: List[Dict[str, Any]]):
    if not rows:
        return

    sql = """
    insert into inventory_raw.olap_postings
    (report_id, date_from, date_to, department, posting_dt,
     product_num, product_name, product_category, product_measure_unit,
     contr_account_name, transaction_type,
     amount_out, amount_in, sum_outgoing, sum_incoming,
     source_hash, loaded_at)
    values %s
    on conflict (source_hash) do nothing;
    """

    values = [
        (
            r["report_id"], r["date_from"], r["date_to"], r["department"], r["posting_dt"],
            r["product_num"], r["product_name"], r["product_category"], r["product_measure_unit"],
            r["contr_account_name"], r["transaction_type"],
            r["amount_out"], r["amount_in"], r["sum_outgoing"], r["sum_incoming"],
            r["source_hash"], datetime.now(timezone.utc),
        )
        for r in rows
    ]

    with db_connect(cfg) as conn:
        with conn.cursor() as cur:
            execute_values(cur, sql, values, page_size=1000)
            conn.commit()


# =============================
# Main
# =============================

def main():
    cfg = load_config()
    print(f"[period] {cfg.date_from} → {cfg.date_to}")

    body = build_olap_request(cfg)
    resp = fetch_olap(cfg, body)

    rows = normalize(cfg, resp.get("data") or [])
    deleted = delete_period(cfg)
    if deleted:
        print(f"[period] перезапись: удалено строк за период: {deleted}")
    insert_rows(cfg, rows)

    print(f"[done] rows inserted: {len(rows)}")


if __name__ == "__main__":
    main()
