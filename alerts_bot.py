import asyncio
import os
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Tuple

import psycopg2
from psycopg2.extras import DictCursor
from dotenv import load_dotenv
from telegram import Update
from telegram.ext import Application, CommandHandler, ContextTypes


@dataclass
class BotConfig:
    neon_host: str
    neon_db: str
    neon_user: str
    neon_password: str

    telegram_token: str
    allowed_chat_id: int

    top_n: int = 5


def _env(name: str) -> str:
    v = os.getenv(name)
    if not v or not str(v).strip():
        raise RuntimeError(f"Missing env var: {name}")
    return str(v).strip()


def _int_optional(name: str, default: int) -> int:
    v = os.getenv(name)
    if v is None or not str(v).strip():
        return default
    return int(str(v).strip())


def load_config() -> BotConfig:
    load_dotenv()

    return BotConfig(
        neon_host=_env("NEON_HOST"),
        neon_db=_env("NEON_DB"),
        neon_user=_env("NEON_USER"),
        neon_password=_env("NEON_PASSWORD"),
        telegram_token=_env("TELEGRAM_BOT_TOKEN"),
        allowed_chat_id=int(_env("TELEGRAM_CHAT_ID")),
        top_n=_int_optional("ALERTS_TOP_N", 5),
    )


def db_connect(cfg: BotConfig):
    return psycopg2.connect(
        host=cfg.neon_host,
        dbname=cfg.neon_db,
        user=cfg.neon_user,
        password=cfg.neon_password,
        sslmode="require",
        cursor_factory=DictCursor,
    )


def get_last_week(conn) -> Tuple[str, str]:
    sql = """
        select week_start, week_end
        from inventory_mart.weekly_deviation_products_money_v2
        order by week_start desc
        limit 1;
    """
    with conn.cursor() as cur:
        cur.execute(sql)
        row = cur.fetchone()
    if not row:
        raise RuntimeError("Не найдены данные в weekly_deviation_products_money_v2")
    return str(row["week_start"]), str(row["week_end"])


def week_end_to_display_end(week_end: str) -> str:
    """Последний день недели в БД — невключающий; для отображения показываем -1 день."""
    d = datetime.strptime(week_end, "%Y-%m-%d").date()
    return (d - timedelta(days=1)).strftime("%Y-%m-%d")


SEP = " | "

# Порог: приход считается «мелким» (дозаказ), если < 15% от недельного движения.
RECEIPT_SMALL_PCT_OF_MOVEMENT = 0.15

# Скоропорт (овощи) — приезжают 3 раза в неделю. Для них несколько приходов за неделю норма.
# Заполни названия товаров (product_name) или оставь пустым; потом можно вынести в env/конфиг.
VEGETABLE_PRODUCT_NAMES: set = set()

# Списание (WRITEOFF): алармим, если списание за неделю >= этого % от движения по товару.
WRITEOFF_ALARM_PCT_OF_MOVEMENT = 0.30


def get_departments(conn, week_start: str, week_end: str) -> List[str]:
    sql = """
        select distinct department
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s
        order by 1;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        return [r["department"] for r in cur.fetchall()]


def get_missing_by_department(conn, week_start: str, week_end: str) -> Dict[str, List[str]]:
    sql = """
        select department, product_name
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and is_missing_inventory_position
        order by department, product_name;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            out[r["department"]].append(r["product_name"] or "")
        return dict(out)


def get_miscount_by_department(conn, week_start: str, week_end: str) -> Dict[str, List[str]]:
    sql = """
        select department, product_name
        from inventory_mart.weekly_deviation_products_qty
        where week_start = %s and week_end = %s and is_wrong_prev_inventory
        order by department, product_name;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            out[r["department"]].append(r["product_name"] or "")
        return dict(out)


def get_resort_by_department(conn, week_start: str, week_end: str) -> Dict[str, List[str]]:
    sql = """
        select department, product_name
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and is_possible_resort
        order by department, product_name;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            out[r["department"]].append(r["product_name"] or "")
        return dict(out)


def _top_neg_money_by_dept(conn, week_start: str, week_end: str, top_n: int) -> Dict[str, List[dict]]:
    sql = """
        select department, product_num, product_name, deviation_money_signed,
               coalesce(allowed_loss_money, 0) as norm,
               coalesce(excess_loss_money, 0) as excess
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and deviation_money_signed < 0
          and (is_possible_resort is null or is_possible_resort = false)
        order by department, excess_loss_money desc nulls last;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            dept = r["department"]
            if len(out[dept]) < top_n:
                out[dept].append(r)
        return dict(out)


def _top_pos_money_by_dept(conn, week_start: str, week_end: str, top_n: int) -> Dict[str, List[dict]]:
    sql = """
        select department, product_num, product_name, deviation_money_signed,
               coalesce(allowed_loss_money, 0) as norm,
               coalesce(excess_deviation_money, 0) as excess
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and deviation_money_signed > 0
          and (is_possible_resort is null or is_possible_resort = false)
        order by department, excess_deviation_money desc nulls last;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            dept = r["department"]
            if len(out[dept]) < top_n:
                out[dept].append(r)
        return dict(out)


def _top_pct_by_dept(
    conn, week_start: str, week_end: str, top_n: int, positive: bool
) -> Dict[str, List[dict]]:
    sign = ">" if positive else "<"
    sql = f"""
        select department, product_name, fact_deviation_pct_qty, norm_pct, excess_pct_qty
        from inventory_mart.weekly_deviation_products_qty
        where week_start = %s and week_end = %s and fact_deviation_pct_qty {sign} 0
          and (is_possible_resort is null or is_possible_resort = false)
        order by department, excess_pct_qty desc nulls last;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        out = defaultdict(list)
        for r in cur.fetchall():
            dept = r["department"]
            if len(out[dept]) < top_n:
                out[dept].append(r)
        return dict(out)


def get_summary_money_by_department(conn, week_start: str, week_end: str) -> List[Tuple[str, float]]:
    sql = """
        select department, sum(deviation_money_signed) as total
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s
        group by department
        order by 1;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end))
        return [(r["department"], float(r["total"] or 0)) for r in cur.fetchall()]


def get_movement_qty_for_products(
    conn, week_start: str, week_end: str, department: str, product_nums: List[str]
) -> Dict[str, float]:
    """Недельное движение (qty) по товарам для проверки задублированного прихода."""
    if not product_nums:
        return {}
    sql = """
        select product_num, movement_qty
        from inventory_core.weekly_movement_products
        where week_start = %s and week_end = %s and department = %s and product_num = ANY(%s);
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end, department, product_nums))
        return {
            r["product_num"]: float(r["movement_qty"] or 0)
            for r in cur.fetchall()
        }


def get_top_writeoffs_by_department(
    conn,
    week_start: str,
    week_end: str,
    top_n: int,
    pct_threshold: float = WRITEOFF_ALARM_PCT_OF_MOVEMENT,
) -> Dict[str, List[dict]]:
    """
    По каждому филиалу — топ списаний (WRITEOFF) за неделю, где списание >= pct_threshold
    от недельного движения.     Исключаем товары, по которым не было продаж (SESSION_WRITEOFF) —
    только списания (WRITEOFF), без продаж (фритюрное масло, говядина лопатка для персонала и т.п.).
    Возвращает { department: [ {product_name, writeoff_qty, writeoff_money, product_measure_unit}, ... ] }.
    """
    sql = """
        WITH w AS (
            SELECT department, product_num,
                   max(product_name) AS product_name,
                   max(product_measure_unit) AS product_measure_unit,
                   sum(abs(qty_signed)) AS writeoff_qty,
                   sum(abs(money_signed)) AS writeoff_money
            FROM inventory_mart.weekly_product_documents_products
            WHERE week_start = %s AND week_end = %s AND transaction_type = 'WRITEOFF'
            GROUP BY department, product_num
        ),
        m AS (
            SELECT department, product_num, movement_qty
            FROM inventory_core.weekly_movement_products
            WHERE week_start = %s AND week_end = %s
        ),
        has_sales AS (
            SELECT date_from AS week_start, date_to AS week_end, department, product_num
            FROM inventory_core.transactions_products
            WHERE date_from = %s AND date_to = %s
              AND UPPER(TRIM(transaction_type)) = 'SESSION_WRITEOFF'
            GROUP BY date_from, date_to, department, product_num
            HAVING (COALESCE(sum(amount_out), 0) + COALESCE(sum(amount_in), 0)) > 0
        ),
        filtered AS (
            SELECT w.department, w.product_num, w.product_name, w.product_measure_unit,
                   w.writeoff_qty, w.writeoff_money, m.movement_qty
            FROM w
            JOIN m ON m.department = w.department AND m.product_num = w.product_num
            JOIN has_sales s ON s.department = w.department AND s.product_num = w.product_num
            WHERE m.movement_qty > 0
              AND w.writeoff_qty >= %s * m.movement_qty
        ),
        ranked AS (
            SELECT department, product_name, product_measure_unit, writeoff_qty, writeoff_money,
                   row_number() OVER (PARTITION BY department ORDER BY writeoff_money DESC) AS rn
            FROM filtered
        )
        SELECT department, product_name, product_measure_unit, writeoff_qty, writeoff_money
        FROM ranked
        WHERE rn <= %s
        ORDER BY department, writeoff_money DESC;
    """
    with conn.cursor() as cur:
        cur.execute(
            sql,
            (week_start, week_end, week_start, week_end, week_start, week_end, pct_threshold, top_n),
        )
        out = defaultdict(list)
        for r in cur.fetchall():
            out[r["department"]].append(
                {
                    "product_name": (r["product_name"] or "").strip() or "—",
                    "writeoff_qty": float(r["writeoff_qty"] or 0),
                    "writeoff_money": float(r["writeoff_money"] or 0),
                    "product_measure_unit": (r["product_measure_unit"] or "ед.").strip(),
                }
            )
        return dict(out)


def get_receipts_for_products(
    conn, week_start: str, week_end: str, department: str, product_nums: List[str]
) -> Dict[str, List[dict]]:
    """По каждому product_num из списка — приходы (INVOICE) за неделю на филиале.
    Возвращает: { product_num: [ {posting_dt, contr_account_name, qty_signed, money_signed}, ... ] }.
    """
    if not product_nums:
        return {}
    sql = """
        select product_num, posting_dt, contr_account_name, qty_signed, money_signed,
               product_measure_unit
        from inventory_mart.weekly_product_documents_products
        where week_start = %s and week_end = %s and department = %s
          and transaction_type = 'INVOICE' and product_num = ANY(%s)
        order by product_num, posting_dt;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end, department, product_nums))
        out = defaultdict(list)
        for r in cur.fetchall():
            out[r["product_num"]].append(
                {
                    "posting_dt": r["posting_dt"],
                    "contr_account_name": r["contr_account_name"] or "",
                    "qty_signed": r["qty_signed"],
                    "money_signed": r["money_signed"],
                    "product_measure_unit": (r.get("product_measure_unit") or "ед.").strip(),
                }
            )
        return dict(out)


def _block(title: str, items: List[str], empty_msg: str = "нет") -> str:
    if not items:
        return f"{title}\n  {empty_msg}"
    return title + "\n" + "\n".join(f"  • {x}" for x in items)


def _block_top_writeoffs(rows: List[dict]) -> str:
    """Топ списаний: название | qty | деньги. Без артикулов."""
    pct = int(WRITEOFF_ALARM_PCT_OF_MOVEMENT * 100)
    if not rows:
        return f"📋 Топ списаний на этой неделе:\n  нет позиций со списанием ≥ {pct}% от движения"
    lines = [f"📋 Топ списаний на этой неделе (≥ {pct}% от движения):"]
    for r in rows:
        name = r.get("product_name") or "—"
        qty = r.get("writeoff_qty") or 0
        money = r.get("writeoff_money") or 0
        unit = r.get("product_measure_unit") or "ед."
        lines.append(f"  {name}{SEP}{qty:.2f} {unit}{SEP}{money:,.0f} ₽".replace(",", " "))
    return "\n".join(lines)


def _format_receipt_line(receipts: List[dict]) -> str:
    """Одна строка по приходам (для излишков): «Приходы: да, 12.02 Поставщик +5 кг» или «Приходы: нет»."""
    if not receipts:
        return "Приходы: нет"
    parts = []
    for rec in receipts[:5]:
        dt = rec.get("posting_dt")
        date_str = dt.strftime("%d.%m") if hasattr(dt, "strftime") else str(dt)[:10]
        contr = (rec.get("contr_account_name") or "").strip() or "—"
        qty = rec.get("qty_signed") or 0
        unit = rec.get("product_measure_unit") or "ед."
        parts.append(f"{date_str} {contr} {qty:+.2f} {unit}")
    if len(receipts) > 5:
        parts.append(f"... ещё {len(receipts) - 5}")
    return "Приходы: да, " + "; ".join(parts)


def _is_possible_duplicate_receipt(
    receipts: List[dict], movement_qty: float, product_name: str
) -> bool:
    """
    Задублированный приход: 2+ прихода, при этом каждый приход >= 15% от недельного движения
    (нет «мелкого» дозаказа). Скоропорт (овощи) пока не исключаем — список VEGETABLE_PRODUCT_NAMES
    можно заполнить, тогда для них 3 прихода в неделю будут нормой.
    """
    if movement_qty is None or movement_qty <= 0 or len(receipts) < 2:
        return False
    if product_name and product_name.strip() in VEGETABLE_PRODUCT_NAMES:
        # Овощи приезжают 3 раза в неделю — несколько приходов норма.
        return False
    threshold = RECEIPT_SMALL_PCT_OF_MOVEMENT * movement_qty
    qtys = [abs(float(rec.get("qty_signed") or 0)) for rec in receipts]
    # Если хотя бы один приход «мелкий» (< 15% движения) — считаем дозаказ, не дубль.
    if any(q < threshold for q in qtys):
        return False
    # Два и более «крупных» прихода при недостаче — возможный дубль накладной.
    return True


def _format_receipt_line_shortage(
    receipts: List[dict],
    movement_qty: float,
    product_name: str,
) -> str:
    """
    Строка по приходам для ТОП-5 недостач: приходы + пометка о возможном задублированном приходе.
    В сообщениях только product_name, артикулы не выводим.
    """
    if not receipts:
        return "Приходы: нет"
    if _is_possible_duplicate_receipt(receipts, movement_qty, product_name):
        return "⚠️ Возможный задублированный приход (2+ крупных прихода при недостаче)"
    return _format_receipt_line(receipts)


def _block_top_money(
    rows: List[dict],
    title: str,
    receipts_by_product_num: Optional[Dict[str, List[dict]]] = None,
    movement_by_product_num: Optional[Dict[str, float]] = None,
    is_shortage_block: bool = False,
) -> str:
    """В сообщениях только product_name, артикулы не выводим."""
    if not rows:
        return f"{title}\n  нет позиций"
    lines = [title]
    for i, r in enumerate(rows, start=1):
        name = (r.get("product_name") or "").strip() or "—"
        dev = r.get("deviation_money_signed") or 0
        norm = r.get("norm") or 0
        excess = r.get("excess") or 0
        lines.append(
            f"  {i}. {name}{SEP}{dev:,.0f} ₽{SEP}норма {norm:,.0f} ₽{SEP}превышение нормы {excess:,.0f} ₽".replace(
                ",", " "
            )
        )
        if receipts_by_product_num is not None:
            pnum = r.get("product_num")
            recs = (receipts_by_product_num.get(pnum) or []) if pnum else []
            mov = (movement_by_product_num or {}).get(pnum) if movement_by_product_num else None
            if is_shortage_block and mov is not None:
                lines.append(f"      {_format_receipt_line_shortage(recs, mov, name)}")
            else:
                lines.append(f"      {_format_receipt_line(recs)}")
    return "\n".join(lines)


def _block_top_pct(rows: List[dict], title: str) -> str:
    if not rows:
        return f"{title}\n  нет позиций"
    lines = [title]
    for i, r in enumerate(rows, start=1):
        name = (r.get("product_name") or "").strip()
        dev = r.get("fact_deviation_pct_qty")
        norm = r.get("norm_pct")
        excess = r.get("excess_pct_qty")
        dev_pct = (dev * 100) if dev is not None else 0
        norm_pct = (norm * 100) if norm is not None else 0
        excess_pct = (excess * 100) if excess is not None else 0
        lines.append(
            f"  {i}. {name}{SEP}{dev_pct:.1f}%{SEP}норма {norm_pct:.1f}%{SEP}превышение нормы {excess_pct:.1f}%".replace(
                ",", "."
            )
        )
    return "\n".join(lines)


def build_report_messages_per_department(cfg: BotConfig) -> Tuple[str, str, List[str]]:
    """Возвращает (week_start, week_end, список текстов — по одному на филиал)."""
    with db_connect(cfg) as conn:
        week_start, week_end = get_last_week(conn)
        depts = get_departments(conn, week_start, week_end)
        missing = get_missing_by_department(conn, week_start, week_end)
        miscount = get_miscount_by_department(conn, week_start, week_end)
        resort = get_resort_by_department(conn, week_start, week_end)
        top_neg_m = _top_neg_money_by_dept(conn, week_start, week_end, cfg.top_n)
        top_pos_m = _top_pos_money_by_dept(conn, week_start, week_end, cfg.top_n)
        top_neg_p = _top_pct_by_dept(conn, week_start, week_end, cfg.top_n, positive=False)
        top_pos_p = _top_pct_by_dept(conn, week_start, week_end, cfg.top_n, positive=True)

        # Приходы и движение по ТОП-5 (недостачи + излишки) для проверки накладных
        receipts_by_dept: Dict[str, Dict[str, List[dict]]] = {}
        movement_by_dept: Dict[str, Dict[str, float]] = {}
        for dept in depts:
            product_nums = []
            for r in (top_neg_m.get(dept, []) or []) + (top_pos_m.get(dept, []) or []):
                pnum = r.get("product_num")
                if pnum and pnum not in product_nums:
                    product_nums.append(pnum)
            receipts_by_dept[dept] = get_receipts_for_products(
                conn, week_start, week_end, dept, product_nums
            )
            # Движение нужно только для ТОП-5 недостач (проверка задублированного прихода)
            shortage_nums = [r.get("product_num") for r in (top_neg_m.get(dept, []) or []) if r.get("product_num")]
            movement_by_dept[dept] = get_movement_qty_for_products(
                conn, week_start, week_end, dept, shortage_nums
            ) if shortage_nums else {}

        # Топ списаний по всем товарам: списание >= 15% от движения
        top_writeoffs = get_top_writeoffs_by_department(
            conn, week_start, week_end, cfg.top_n, WRITEOFF_ALARM_PCT_OF_MOVEMENT
        )

    display_end = week_end_to_display_end(week_end)
    messages = []
    for dept in depts:
        receipts = receipts_by_dept.get(dept, {})
        movement = movement_by_dept.get(dept, {})
        neg_rows = top_neg_m.get(dept, [])
        pos_rows = top_pos_m.get(dept, [])

        # По ТОП-5 недостач: возможный задублированный приход (только названия, без артикулов)
        duplicate_names = []
        for r in neg_rows:
            pnum = r.get("product_num")
            name = (r.get("product_name") or "").strip() or "—"
            recs = receipts.get(pnum, []) if pnum else []
            mov = movement.get(pnum) if pnum else None
            if _is_possible_duplicate_receipt(recs, mov or 0, name):
                duplicate_names.append(name)
        no_receipt_names = []
        for r in neg_rows:
            pnum = r.get("product_num")
            if pnum and not receipts.get(pnum):
                name = (r.get("product_name") or "").strip()
                if name:
                    no_receipt_names.append(name)
        if duplicate_names:
            receipt_summary = f"⚠️ Возможный задублированный приход по ТОП-5 недостач: {', '.join(duplicate_names[:10])}{'…' if len(duplicate_names) > 10 else ''}"
        elif no_receipt_names:
            receipt_summary = f"ℹ️ По позициям приходов за неделю не было — проверьте накладные: {', '.join(no_receipt_names[:10])}{'…' if len(no_receipt_names) > 10 else ''}"
        else:
            receipt_summary = "✅ Проверка приходов по ТОП-5 недостач: задублированных приходов не выявлено."

        parts = [
            f"📅 Неделя {week_start} — {display_end}",
            "",
            f"🏪 {dept}",
            "─────────────────────",
            _block("📌 Несохранённые позиции:", missing.get(dept, [])),
            "",
            _block("⚠️ Неверно посчитанные позиции неделю назад:", miscount.get(dept, [])),
            "",
            _block("🔄 Позиции с пересортом:", resort.get(dept, [])),
            "",
            _block_top_writeoffs(top_writeoffs.get(dept, [])),
            "",
            _block_top_money(neg_rows, "📉 ТОП недостач в деньгах:"),
            "",
            _block_top_money(pos_rows, "📈 ТОП излишков в деньгах:"),
            "",
            receipt_summary,
            "",
            _block_top_pct(top_neg_p.get(dept, []), "📉 ТОП недостач в %:"),
            "",
            _block_top_pct(top_pos_p.get(dept, []), "📈 ТОП излишков в %:"),
        ]
        messages.append("\n".join(parts))
    return week_start, week_end, messages


def build_summary_message(week_start: str, week_end: str, summary: List[Tuple[str, float]]) -> str:
    display_end = week_end_to_display_end(week_end)
    if not summary:
        return f"📊 Сводка за {week_start} — {display_end}\nНет данных по филиалам."
    lines = [
        f"📊 Сводка за неделю {week_start} — {display_end}",
        "сумма недостач + излишков по филиалам:",
        "",
    ]
    for dept, total in summary:
        lines.append(f"  {dept}{SEP}{total:,.0f} ₽".replace(",", " "))
    return "\n".join(lines)


def build_report_text(cfg: BotConfig) -> str:
    """Один большой текст (для обратной совместимости /week в режиме bot)."""
    week_start, week_end, messages = build_report_messages_per_department(cfg)
    with db_connect(cfg) as conn:
        summary = get_summary_money_by_department(conn, week_start, week_end)
    parts = ["\n\n".join(messages), "", build_summary_message(week_start, week_end, summary)]
    return "\n".join(parts)


async def start(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    cfg: BotConfig = context.application.bot_data["cfg"]
    if update.effective_chat and update.effective_chat.id != cfg.allowed_chat_id:
        await update.message.reply_text("Доступ к этому боту ограничен.")
        return
    await update.message.reply_text("Привет! Команда /week покажет сводку по последней неделе.")


async def week(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    cfg: BotConfig = context.application.bot_data["cfg"]
    if update.effective_chat and update.effective_chat.id != cfg.allowed_chat_id:
        await update.message.reply_text("Доступ к этому боту ограничен.")
        return
    try:
        text = build_report_text(cfg)
    except Exception as e:
        await update.message.reply_text(f"Ошибка при формировании отчёта: {e}")
        return
    await update.message.reply_text(text)


def main() -> None:
    cfg = load_config()
    mode = os.getenv("ALERTS_MODE", "once").lower()

    if mode == "bot":
        # Долгоживущий режим: Telegram‑бот с polling (для локального запуска)
        app = Application.builder().token(cfg.telegram_token).build()
        app.bot_data["cfg"] = cfg
        app.add_handler(CommandHandler("start", start))
        app.add_handler(CommandHandler("week", week))
        app.run_polling()
    else:
        # Режим по умолчанию: по одному сообщению на филиал, затем сводка
        from telegram import Bot
        from telegram.error import ChatMigrated

        week_start, week_end, dept_messages = build_report_messages_per_department(cfg)
        with db_connect(cfg) as conn:
            summary = get_summary_money_by_department(conn, week_start, week_end)
        summary_text = build_summary_message(week_start, week_end, summary)
        bot = Bot(token=cfg.telegram_token)

        async def send():
            chat_id = cfg.allowed_chat_id
            try:
                for msg in dept_messages:
                    await bot.send_message(chat_id=chat_id, text=msg)
                await bot.send_message(chat_id=chat_id, text=summary_text)
            except ChatMigrated as e:
                chat_id = e.new_chat_id
                print(
                    f"Чат переехал в супергруппу. Обнови секрет TELEGRAM_CHAT_ID на: {chat_id}"
                )
                for msg in dept_messages:
                    await bot.send_message(chat_id=chat_id, text=msg)
                await bot.send_message(chat_id=chat_id, text=summary_text)

        asyncio.run(send())


if __name__ == "__main__":
    main()

