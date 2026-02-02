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


def last_closed_week_tue_to_tue(today: Optional[date] = None) -> Tuple[str, str]:
    if today is None:
        today = datetime.now().date()

    # weekday: Mon=0 ... Sun=6 | Tue=1
    weekday = today.weekday()
    days_since_tue = (weekday - 1) % 7
    this_tuesday = today - timedelta(days=days_since_tue)

    start = this_tuesday - timedelta(days=7)
    end = this_tuesday

    return start.isoformat(), end.isoformat()


def load_config() -> Config:
    # локально не мешает, в Actions env уже есть
    load_dotenv()

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
    login = (cfg.iiko_login or "").strip()
    sha1 = (cfg.iiko_pass_sha1 or "").strip().lower()

    # Безопасный дебаг (не палим секрет полностью)
    print(f"[iiko-auth] login={login} sha1_len={len(sha1)} mask={sha1[:4]}...{sha1[-4:]}")

    endpoints = [
        f"{base}/api/auth",          # как у коллеги
        f"{base}/resto/api/auth",    # как было у нас
    ]

    last_err = None
    for url in endpoints:
        try:
            resp = requests.get(
                url,
                params={"login": login, "pass": sha1},  # ВАЖНО: pass=SHA1
                verify=cfg.iiko_verify_ssl,
                timeout=30,
            )
            if resp.status_code == 200 and resp.text.strip():
                token = resp.text.strip()
                print(f"[iiko-auth] ok via {url} token={token[:6]}...")
                return token

            last_err = f"{url}: {resp.status_code} {resp.text}"
            print(f"[iiko-auth] fail via {last_err}")
        except Exception as e:
            last_err = f"{url}: exception {e}"
            print(f"[iiko-auth] fail via {last_err}")

    raise RuntimeError(f"iiko auth failed. Last error: {last_err}")


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

    def _do_request(k: str):
        return requests.post(
            f"{cfg.iiko_base_url}/resto/api/v2/reports/olap?key={k}",
            json=body,
            headers={"Content-Type": "application/json"},
            verify=cfg.iiko_verify_ssl,
            timeout=180,
        )

    resp = _do_request(key)

    if resp.status_code in (401, 403):
        print("[iiko] key expired, reauth...")
        key = get_iiko_key(cfg)
        resp = _do_request(key)

    if resp.status_code != 200:
        raise RuntimeError(f"OLAP failed: {resp.status_code} {resp.text[:2000]}")

    return resp.json()


# =============================
# Normalization
# =============================

TOTAL_MARKERS = ("итого", "всего")


def is_total(*vals) -> bool:
    return any(v and any(m in str(v).lower() for m in TOTAL_MARKERS) for v in vals)


def parse_posting_dt(raw: str) -> datetime:
    if not raw:
        raise ValueError("Empty posting_dt")

    s = raw.strip()

    # iiko иногда отдаёт Z
    if s.endswith("Z"):
        s = s.replace("Z", "+00:00")

    dt = datetime.fromisoformat(s)

    # если таймзоны нет — считаем, что это локальное время (Москва)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=ZoneInfo("Europe/Moscow"))

    return dt


def normalize(cfg: Config, data: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []

    for r in data:
        if is_total(
            r.get("Department"),
            r.get("TransactionType"),
            r.get("Product.Num"),
            r.get("Product.Name"),
        ):
            continue

        # числа
        try:
            amount_out = float(r.get("Amount.Out") or 0)
            amount_in = float(r.get("Amount.In") or 0)
            sum_outgoing = float(r.get("Sum.Outgoing") or 0)
            sum_incoming = float(r.get("Sum.Incoming") or 0)
        except Exception:
            continue

        # posting_dt: в БД кладём datetime, для хэша используем каноническую строку UTC
        try:
            posting_dt_dt = parse_posting_dt(r["DateTime.Typed"])
        except Exception:
            continue

        posting_dt_norm = posting_dt_dt.astimezone(timezone.utc).isoformat()

        # payload для хэша (ТОЛЬКО сериализуемые типы)
        payload_for_hash = {
            "report_id": cfg.report_id,
            "date_from": cfg.date_from,
            "date_to": cfg.date_to,
            "department": str(r["Department"]).strip(),
            "posting_dt": posting_dt_norm,  # важно: строка
            "product_num": str(r["Product.Num"]).strip(),
            "product_name": str(r.get("Product.Name") or "").strip(),
            "transaction_type": str(r["TransactionType"]).strip(),
            "amount_out": amount_out,
            "amount_in": amount_in,
            "sum_outgoing": sum_outgoing,
            "sum_incoming": sum_incoming,
            "product_category": str(r.get("Product.Category") or "").strip(),
            "product_measure_unit": str(r.get("Product.MeasureUnit") or "").strip(),
        }

        source_hash = hashlib.sha256(
            json.dumps(payload_for_hash, sort_keys=True, ensure_ascii=False).encode()
        ).hexdigest()

        # payload для БД (posting_dt как datetime)
        payload = dict(payload_for_hash)
        payload["posting_dt"] = posting_dt_dt
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


def insert_rows(cfg: Config, rows: List[Dict[str, Any]]):
    if not rows:
        return

    sql = """
    insert into inventory_raw.olap_postings
    (report_id, date_from, date_to, department, posting_dt,
     product_num, product_name, product_category, transaction_type,
     amount_out, amount_in, sum_outgoing, sum_incoming, source_hash, loaded_at)
    values %s
    on conflict (source_hash) do nothing;
    """

    values = [
        (
            r["report_id"],
            r["date_from"],
            r["date_to"],
            r["department"],
            r["posting_dt"],  # datetime object -> timestamptz ок
            r["product_num"],
            r["product_name"],
            r["product_category"],
            r["transaction_type"],
            r["amount_out"],
            r["amount_in"],
            r["sum_outgoing"],
            r["sum_incoming"],
            r["source_hash"],
            datetime.now(timezone.utc),
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
    print(f"[period] {cfg.date_from} → {cfg.date_to} (Tue→Mon)")

    body = build_olap_request(cfg)
    resp = fetch_olap(cfg, body)

    rows = normalize(cfg, resp.get("data") or [])
    insert_rows(cfg, rows)

    print(f"[done] rows prepared: {len(rows)} (duplicates skipped by ON CONFLICT)")


if __name__ == "__main__":
    main()
