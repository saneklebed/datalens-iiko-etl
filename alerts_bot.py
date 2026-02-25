import asyncio
import os
from collections import defaultdict
from dataclasses import dataclass
from typing import Dict, List, Tuple

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


SEP = " | "


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


def _top_neg_money_by_dept(conn, week_start: str, week_end: str, top_n: int) -> Dict[str, List[dict]]:
    sql = """
        select department, product_name, deviation_money_signed,
               coalesce(excess_loss_money, 0) as excess
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and deviation_money_signed < 0
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
        select department, product_name, deviation_money_signed,
               coalesce(excess_deviation_money, 0) as excess
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s and deviation_money_signed > 0
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
        select department, product_name, fact_deviation_pct_qty, excess_pct_qty
        from inventory_mart.weekly_deviation_products_qty
        where week_start = %s and week_end = %s and fact_deviation_pct_qty {sign} 0
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


def _block(title: str, items: List[str], empty_msg: str = "нет") -> str:
    if not items:
        return f"{title}\n  {empty_msg}"
    return title + "\n" + "\n".join(f"  • {x}" for x in items)


def _block_top_money(rows: List[dict], title: str) -> str:
    if not rows:
        return f"{title}\n  нет позиций"
    lines = [title]
    for i, r in enumerate(rows, start=1):
        name = (r.get("product_name") or "").strip()
        dev = r.get("deviation_money_signed") or 0
        excess = r.get("excess") or 0
        lines.append(f"  {i}. {name}{SEP}{dev:,.0f} ₽{SEP}сверх нормы {excess:,.0f} ₽".replace(",", " "))
    return "\n".join(lines)


def _block_top_pct(rows: List[dict], title: str) -> str:
    if not rows:
        return f"{title}\n  нет позиций"
    lines = [title]
    for i, r in enumerate(rows, start=1):
        name = (r.get("product_name") or "").strip()
        dev = r.get("fact_deviation_pct_qty")
        excess = r.get("excess_pct_qty")
        dev_pct = (dev * 100) if dev is not None else 0
        excess_pct = (excess * 100) if excess is not None else 0
        lines.append(f"  {i}. {name}{SEP}{dev_pct:.1f}%{SEP}сверх нормы {excess_pct:.1f} п.п.".replace(",", "."))
    return "\n".join(lines)


def build_report_messages_per_department(cfg: BotConfig) -> Tuple[str, str, List[str]]:
    """Возвращает (week_start, week_end, список текстов — по одному на филиал)."""
    with db_connect(cfg) as conn:
        week_start, week_end = get_last_week(conn)
        depts = get_departments(conn, week_start, week_end)
        missing = get_missing_by_department(conn, week_start, week_end)
        miscount = get_miscount_by_department(conn, week_start, week_end)
        top_neg_m = _top_neg_money_by_dept(conn, week_start, week_end, cfg.top_n)
        top_pos_m = _top_pos_money_by_dept(conn, week_start, week_end, cfg.top_n)
        top_neg_p = _top_pct_by_dept(conn, week_start, week_end, cfg.top_n, positive=False)
        top_pos_p = _top_pct_by_dept(conn, week_start, week_end, cfg.top_n, positive=True)

    messages = []
    for dept in depts:
        parts = [
            f"📅 Неделя {week_start} — {week_end}",
            "",
            f"🏪 {dept}",
            "─────────────────────",
            _block("📌 Несохранённые позиции:", missing.get(dept, [])),
            "",
            _block("⚠️ Неверно посчитанные позиции:", miscount.get(dept, [])),
            "",
            _block_top_money(top_neg_m.get(dept, []), "📉 ТОП недостач в деньгах:"),
            "",
            _block_top_money(top_pos_m.get(dept, []), "📈 ТОП излишков в деньгах:"),
            "",
            _block_top_pct(top_neg_p.get(dept, []), "📉 ТОП недостач в %:"),
            "",
            _block_top_pct(top_pos_p.get(dept, []), "📈 ТОП излишков в %:"),
        ]
        messages.append("\n".join(parts))
    return week_start, week_end, messages


def build_summary_message(week_start: str, week_end: str, summary: List[Tuple[str, float]]) -> str:
    if not summary:
        return f"📊 Сводка за {week_start} — {week_end}\nНет данных по филиалам."
    lines = [
        f"📊 Сводка за неделю {week_start} — {week_end}",
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

