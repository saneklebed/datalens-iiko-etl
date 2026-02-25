import asyncio
import os
from dataclasses import dataclass
from typing import List, Tuple

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

    top_n: int = 10


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
        top_n=_int_optional("ALERTS_TOP_N", 10),
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


def get_counts(conn, week_start: str, week_end: str) -> Tuple[int, int]:
    sql_miscount = """
        select count(*) as cnt
        from inventory_mart.weekly_deviation_products_qty
        where week_start = %s and week_end = %s
          and is_wrong_prev_inventory;
    """
    sql_missing = """
        select count(*) as cnt
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s
          and is_missing_inventory_position;
    """
    with conn.cursor() as cur:
        cur.execute(sql_miscount, (week_start, week_end))
        miscount = cur.fetchone()["cnt"]
        cur.execute(sql_missing, (week_start, week_end))
        missing = cur.fetchone()["cnt"]
    return int(miscount), int(missing)


def get_top_negative_money(conn, week_start: str, week_end: str, top_n: int) -> List[dict]:
    sql = """
        select department,
               product_num,
               product_name,
               deviation_money_signed,
               excess_loss_money,
               excess_deviation_money
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s
          and deviation_money_signed < 0
        order by excess_loss_money desc nulls last
        limit %s;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end, top_n))
        return list(cur.fetchall())


def get_top_positive_money(conn, week_start: str, week_end: str, top_n: int) -> List[dict]:
    sql = """
        select department,
               product_num,
               product_name,
               deviation_money_signed,
               excess_deviation_money
        from inventory_mart.weekly_deviation_products_money_v2
        where week_start = %s and week_end = %s
          and deviation_money_signed > 0
        order by excess_deviation_money desc nulls last
        limit %s;
    """
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end, top_n))
        return list(cur.fetchall())


def get_top_pct(conn, week_start: str, week_end: str, top_n: int, positive: bool) -> List[dict]:
    sql = """
        select department,
               product_num,
               product_name,
               fact_deviation_pct_qty,
               excess_pct_qty
        from inventory_mart.weekly_deviation_products_qty
        where week_start = %s and week_end = %s
          and fact_deviation_pct_qty {sign} 0
        order by excess_pct_qty desc nulls last
        limit %s;
    """.format(sign=">" if positive else "<")
    with conn.cursor() as cur:
        cur.execute(sql, (week_start, week_end, top_n))
        return list(cur.fetchall())


def format_top_money(rows: List[dict], title: str) -> str:
    if not rows:
        return f"{title}:\n  нет позиций\n"
    lines = [f"{title}:"]
    for i, r in enumerate(rows, start=1):
        dept = r["department"]
        name = r["product_name"]
        num = r["product_num"]
        dev = r["deviation_money_signed"]
        excess = r.get("excess_loss_money") or r.get("excess_deviation_money")
        lines.append(f"{i}) {dept} / {name} ({num}): {dev:.0f} ₽, превышение нормы {excess:.0f} ₽")
    return "\n".join(lines) + "\n"


def format_top_pct(rows: List[dict], title: str) -> str:
    if not rows:
        return f"{title}:\n  нет позиций\n"
    lines = [f"{title}:"]
    for i, r in enumerate(rows, start=1):
        dept = r["department"]
        name = r["product_name"]
        num = r["product_num"]
        dev = r["fact_deviation_pct_qty"] * 100 if r["fact_deviation_pct_qty"] is not None else 0
        excess = r["excess_pct_qty"] * 100 if r["excess_pct_qty"] is not None else 0
        lines.append(f"{i}) {dept} / {name} ({num}): {dev:.1f}% (сверх нормы {excess:.1f} п.п.)")
    return "\n".join(lines) + "\n"


def build_report_text(cfg: BotConfig) -> str:
    with db_connect(cfg) as conn:
        week_start, week_end = get_last_week(conn)
        miscount_cnt, missing_cnt = get_counts(conn, week_start, week_end)
        top_neg_money = get_top_negative_money(conn, week_start, week_end, cfg.top_n)
        top_pos_money = get_top_positive_money(conn, week_start, week_end, cfg.top_n)
        top_neg_pct = get_top_pct(conn, week_start, week_end, cfg.top_n, positive=False)
        top_pos_pct = get_top_pct(conn, week_start, week_end, cfg.top_n, positive=True)

    parts = [
        f"Неделя {week_start} — {week_end}",
        "",
        f"Неверно посчитанные позиции неделю назад: {miscount_cnt}",
        f"Несохранённые позиции: {missing_cnt}",
        "",
        format_top_money(top_neg_money, "ТОП по деньгам (недостачи)"),
        format_top_money(top_pos_money, "ТОП по деньгам (излишки)"),
        format_top_pct(top_neg_pct, "ТОП по отклонениям в % (недостачи)"),
        format_top_pct(top_pos_pct, "ТОП по отклонениям в % (излишки)"),
    ]
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
        # Режим по умолчанию: один раз собрать отчёт и отправить в указанный чат
        from telegram import Bot
        from telegram.error import ChatMigrated

        text = build_report_text(cfg)
        bot = Bot(token=cfg.telegram_token)

        async def send():
            try:
                await bot.send_message(chat_id=cfg.allowed_chat_id, text=text)
            except ChatMigrated as e:
                # Группа переехала в супергруппу — новый chat_id
                await bot.send_message(chat_id=e.new_chat_id, text=text)
                print(
                    f"Чат переехал в супергруппу. Обнови секрет TELEGRAM_CHAT_ID на: {e.new_chat_id}"
                )

        asyncio.run(send())


if __name__ == "__main__":
    main()

