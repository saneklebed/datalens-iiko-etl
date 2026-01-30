import os
import json
import hashlib
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone, timedelta, date
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

    # iiko
    iiko_base_url: str
    iiko_login: str
    iiko_pass_sha1: str
    iiko_verify_ssl: bool

    # Filters
    transaction_types: List[str]
    product_types: List[str]

    # Storage
    raw_dir: Path


def _env(name: str, default: Optional[str] = None) -> str:
    v = os.getenv(name, default)
    if v is None or str(v).strip() == "":
        raise RuntimeError(f"Missing env var: {name}")
    return str(v).strip()


def _env_list(name: str, default: str = "") -> List[str]:
    raw = os.getenv(name, default) or ""
    return [p.strip() for p in raw.replace(";", ",").split(",") if p.strip()]


def _parse_bool_env(name: str, default: str = "1") -> bool:
    raw = os.getenv(name, default)
    if raw is None:
        return True
    s = str(raw).strip().lower()
    return not (s in ("0", "false", "no", "off"))


# -----------------------------
# Period logic (Tue->Mon week)
# -----------------------------

def _today_utc() -> date:
    return datetime.now(timezone.utc).date()


def last_closed_week_tue_to_mon_utc() -> Tuple[str, str]:
    """
    Returns (date_from, date_to) as YYYY-MM-DD strings.
    We define weekly window as:
      start: Tuesday 00:00
      end:   next Tuesday 00:00 (exclusive)
    and print it as Tue->Mon because it covers Tue..Mon inclusive dates.

    We take "last closed" window relative to now (UTC):
      find the most recent Tuesday 00:00 that is <= now, use it as current boundary,
      then take previous boundary as start.
    """
    now = datetime.now(timezone.utc)
    # find most recent Tuesday 00:00 <= now
    # weekday: Mon=0 ... Sun=6; Tuesday=1
    days_since_tue = (now.weekday() - 1) % 7
    last_tue = (now - timedelta(days=days_since_tue)).date()
    last_tue_dt = datetime(last_tue.year, last_tue.month, last_tue.day, tzinfo=timezone.utc)

    if now < last_tue_dt:
        # should not happen, but just in case
        last_tue_dt -= timedelta(days=7)

    date_to = last_tue_dt.date()          # Tuesday 00:00
    date_from = (last_tue_dt - timedelta(days=7)).date()  # previous Tuesday 00:00

    return date_from.isoformat(), date_to.isoformat()


def load_config() -> Tuple[Config, str, str]:
    # allow local dev, but in GitHub Actions env is already set
    load_dotenv()

    verify_ssl = _parse_bool_env("IIKO_VERIFY_SSL", "1")
    raw_dir = Path(os.getenv("RAW_DIR", "src/data/raw")).resolve()

    cfg = Config(
        neon_host=_env("NEON_HOST"),
        neon_db=_env("NEON_DB"),
        neon_user=_env("NEON_USER"),
        neon_password=_env("NEON_PASSWORD"),

        report_id=_env("REPORT_ID"),

        iiko_base_url=_env("IIKO_BASE_URL").rstrip("/"),
        iiko_login=_env("IIKO_LOGIN"),
        iiko_pass_sha1=_env("IIKO_PASS_SHA1"),

        iiko_verify_ssl=verify_ssl,

        transaction_types=_env_list("TRANSACTION_TYPES"),
        product_types=_env_list("PRODUCT_TYPES"),

        raw_dir=raw_dir,
    )

    if not cfg.transaction_types:
        raise RuntimeError("TRANSACTION_TYPES is empty")
    if not cfg.product_types:
        raise RuntimeError("PRODUCT_TYPES is empty")

    date_from, date_to = last_closed_week_tue_to_mon_utc()
    return cfg, date_from, date_to


# -----------------------------
# iiko auth + OLAP
# -----------------------------

def get_iiko_key(cfg: Config) -> str:
    """
    Works like your colleague's example:
      GET {base}/api/auth?login=...&pass=...
    Here pass must be SHA1 (you confirmed manually).
    """
    url = f"{cfg.iiko_base_url}/api/auth"
    login = cfg.iiko_login.strip()
    sha1 = cfg.iiko_pass_sha1.strip().lower()

    # safe debug (no full secret)
    print(f"[iiko-auth] login={login} sha1_len={len(sha1)} mask={sha1[:4]}...{sha1[-4:]}")

    resp = requests.get(
        url,
        params={"login": login, "pass": sha1},
        verify=cfg.iiko_verify_ssl,
        timeout=30,
    )
    if resp.status_code != 200 or not resp.text.strip():
        raise RuntimeError(f"iiko auth failed: {resp.status_code} {resp.text}")
    token = resp.text.strip()
    print(f"[iiko-auth] ok token={token[:6]}...")
    return token


def build_olap_request(cfg: Config, date_from: str, date_to: str) -> Dict[str, Any]:
    dt_from = f"{date_from}T00:00:00.000"
    dt_to = f"{date_to}T00:00:00.000"

    return {
        "reportType": "TRANSACTIONS",
        "buildSummary": False,
        "groupByRowFields": [
            "Department",
            "DateSecondary.DateTimeTyped",
            "TransactionType",
            "Product.Num",
            "Product.Name",
        ],
        "groupByColFields": [],
        "aggregateFields": [
            "Amount.Out",
            "Sum.Outgoing",
        ],
        "filters": {
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


def fetch_olap(cfg: Config, body: Dict[str, Any]) -> Dict[str, Any]:
    key = get_iiko_key(cfg)
    url = f"{cfg.iiko_base_url}/resto/api/v2/reports/olap?key={key}"
    headers = {"Content-Type": "application/json; charset=utf-8"}

    resp = requests.post(
        url,
        headers=headers,
        json=body,
        verify=cfg.iiko_verify_ssl,
        timeout=180,
    )
    if resp.status_code != 200:
        raise RuntimeError(f"OLAP request failed: {resp.status_code} {resp.text[:2000]}")
    return resp.json()


def save_raw(cfg: Config, request_body: Dict[str, Any], response_json: Dict[str, Any],
             date_from: str, date_to: str) -> Tuple[Path, Path]:
    cfg.raw_dir.mkdir(parents=True, exist_ok=True)

    ts = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
    rnd = uuid.uuid4().hex[:8]
    base = f"{cfg.report_id}_{date_from}_{date_to}_{rnd}_{ts}"

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


def is_total_row(*vals: Optional[str]) -> bool:
    return any(_has_totals_marker(v) for v in vals)


def _parse_posting_dt(pdt: Any) -> datetime:
    """
    iiko usually returns ISO string; we normalize to timezone-aware datetime.
    """
    s = str(pdt).strip()
    # handle trailing Z
    s = s.replace("Z", "+00:00")
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        # assume UTC if tz missing
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def normalize_rows(cfg: Config, olap_json: Dict[str, Any],
                   date_from: str, date_to: str) -> Tuple[List[Dict[str, Any]], Dict[str, int]]:
    data = olap_json.get("data") or []
    out: List[Dict[str, Any]] = []

    skipped: Dict[str, int] = {
        "totals_rows": 0,
        "missing_required": 0,
        "bad_amount": 0,
        "bad_money": 0,
        "bad_datetime": 0,
    }

    for r in data:
        dept = r.get("Department")
        pdt = r.get("DateSecondary.DateTimeTyped")
        trt = r.get("TransactionType")
        prod_num = r.get("Product.Num")
        prod_name = r.get("Product.Name")

        amount_out_raw = r.get("Amount.Out")
        sum_outgoing_raw = r.get("Sum.Outgoing")

        # cut totals rows (any marker in key text fields)
        if is_total_row(dept, trt, prod_num, prod_name):
            skipped["totals_rows"] += 1
            continue

        # required fields
        if not dept or not pdt or not trt or not prod_num:
            skipped["missing_required"] += 1
            continue

        # parse datetime
        try:
            posting_dt = _parse_posting_dt(pdt)
        except Exception:
            skipped["bad_datetime"] += 1
            continue

        # amount
        try:
            amount_out = float(amount_out_raw) if amount_out_raw is not None else 0.0
        except Exception:
            skipped["bad_amount"] += 1
            continue

        # money
        try:
            sum_outgoing = float(sum_outgoing_raw) if sum_outgoing_raw is not None else 0.0
        except Exception:
            skipped["bad_money"] += 1
            continue

        department = str(dept).strip()
        transaction_type = str(trt).strip()
        product_num = str(prod_num).strip()
        product_name = str(prod_name).strip() if prod_name is not None else None

        payload = {
            "report_id": cfg.report_id,
            "date_from": date_from,
            "date_to": date_to,
            "department": department,
            # store normalized ISO for stable hashing
            "posting_dt": posting_dt.isoformat(),
            "product_num": product_num,
            "product_name": product_name,
            "transaction_type": transaction_type,
            "amount_out": round(amount_out, 10),
            "sum_outgoing": round(sum_outgoing, 10),
        }

        source_hash = hashlib.sha256(
            json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest()

        out.append({
            "report_id": cfg.report_id,
            "date_from": date_from,
            "date_to": date_to,
            "department": department,
            "posting_dt": posting_dt,  # datetime object for timestamptz
            "product_num": product_num,
            "product_name": product_name,
            "transaction_type": transaction_type,
            "amount_out": amount_out,
            "sum_outgoing": sum_outgoing,
            "source_hash": source_hash,
        })

    return out, skipped


# -----------------------------
# DB
# -----------------------------

def db_connect(cfg: Config):
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
        insert into raw.olap_postings
        (report_id, date_from, date_to, department, posting_dt, product_num, product_name, transaction_type,
         amount_out, sum_outgoing, source_hash, loaded_at)
        values %s
        on conflict (source_hash) do nothing;
    """

    values = [
        (
            r["report_id"],
            r["date_from"],
            r["date_to"],
            r["department"],
            r["posting_dt"],
            r["product_num"],
            r["product_name"],
            r["transaction_type"],
            r["amount_out"],
            r["sum_outgoing"],
            r["source_hash"],
            datetime.now(timezone.utc),
        )
        for r in rows
    ]

    with db_connect(cfg) as conn:
        with conn.cursor() as cur:
            cur.execute("select count(*) from raw.olap_postings;")
            before = cur.fetchone()[0]
            print(f"[db] before: {before}")

            print("[db] inserting (execute_values)...")
            execute_values(cur, sql, values, page_size=1000)
            conn.commit()

            cur.execute("select count(*) from raw.olap_postings;")
            after = cur.fetchone()[0]
            print(f"[db] after:  {after}")

    return max(0, after - before)


# -----------------------------
# Main
# -----------------------------

def main():
    cfg, date_from, date_to = load_config()

    # silence warnings if ssl verify off
    if not cfg.iiko_verify_ssl:
        try:
            import urllib3
            urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
        except Exception:
            pass

    print(f"[period] {date_from} → {date_to} (Tue→Mon)")

    body = build_olap_request(cfg, date_from, date_to)
    resp = fetch_olap(cfg, body)

    req_path, resp_path = save_raw(cfg, body, resp, date_from, date_to)
    print(f"[files] request saved:  {req_path}")
    print(f"[files] response saved: {resp_path}")

    rows, skipped = normalize_rows(cfg, resp, date_from, date_to)
    print(f"[olap] rows from API: {len(resp.get('data') or [])}")
    print(f"[prep] rows prepared: {len(rows)}")
    print(f"[prep] skipped: {skipped}")

    print("[db] connecting to Neon...")
    inserted = insert_rows(cfg, rows)
    print(f"[done] inserted rows: {inserted}")


if __name__ == "__main__":
    main()
