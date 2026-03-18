import json
import csv
import random
import asyncio
import sys
from datetime import datetime
from pathlib import Path

from telethon import TelegramClient
from telethon.errors import FloodWaitError, RPCError

BASE_DIR = Path(__file__).resolve().parent


def load_json(path: Path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def log_row(log_path: Path, row: dict):
    file_exists = log_path.exists()
    with open(log_path, "a", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=["ts", "recipient", "status", "file", "details"]
        )
        if not file_exists:
            writer.writeheader()
        writer.writerow(row)


def ts():
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


async def main():
    config = load_json(BASE_DIR / "config.json")
    recipients = load_json(BASE_DIR / "recipients.json")

    api_id = int(config["api_id"])
    api_hash = str(config["api_hash"]).strip()
    session_name = str(config.get("session_name", "orange_supply_session")).strip()

    files_dir = Path(config["files_dir"])

    # Турбо-задержки (поддерживаем float)
    min_delay = float(config.get("min_delay_sec", 0.8))
    max_delay = float(config.get("max_delay_sec", 1.6))

    if not recipients:
        print("ОШИБКА: recipients.json пустой.")
        return

    # Валидация: у каждого должен быть username/message/file, и файл должен существовать
    problems = []
    for i, r in enumerate(recipients, start=1):
        if not isinstance(r, dict):
            problems.append(f"Строка {i}: должен быть объект {{username,message,file}}")
            continue

        for key in ("username", "message", "file"):
            if key not in r or not str(r[key]).strip():
                problems.append(f"Строка {i}: нет поля {key}")

        file_name = str(r.get("file", "")).strip()
        if file_name:
            fpath = files_dir / file_name
            if not fpath.exists():
                problems.append(f"Строка {i}: файл не найден: {fpath}")

    if problems:
        print("ОШИБКА: проблемы в recipients.json / файлах:")
        for p in problems:
            print(" - " + p)
        return

    print("=== Telegram Sender (ТУРБО) ===")
    print(f"Папка файлов: {files_dir}")
    print(f"Получателей: {len(recipients)}")
    print("Режим: 1 файл -> 1 поставщику, персональный текст, файлы НЕ перемещаем.")
    print(f"Задержка между получателями: {min_delay:.2f}–{max_delay:.2f} сек")
    print("Микропаузa между текстом и файлом: 0.25 сек")
    input("Нажми Enter, чтобы НАЧАТЬ (или закрой окно, чтобы отменить)...")

    log_path = BASE_DIR / "log.csv"
    # Сессия всегда в папке скрипта — чтобы удаление session-файла здесь гарантированно сбрасывало авторизацию
    session_path = BASE_DIR / session_name
    session_file = Path(str(session_path) + ".session")
    if not session_file.exists():
        print("Сессия не найдена. При первом подключении введи номер телефона и код из Telegram.")
    client = TelegramClient(str(session_path), api_id, api_hash)

    async with client:
        me = await client.get_me()
        print(f"Залогинен как: {getattr(me, 'first_name', '')} (@{getattr(me, 'username', '')})")

        for idx, r in enumerate(recipients, start=1):
            username = str(r["username"]).strip().lstrip("@")
            message = str(r["message"]).strip()
            file_name = str(r["file"]).strip()
            file_path = files_dir / file_name

            try:
                entity = await client.get_entity(username)

                # 1) Персональный текст
                await client.send_message(entity, message)

                # 2) Микропаузa, чтобы события не "слипались"
                await asyncio.sleep(0.25)

                # 3) Один файл
                await client.send_file(entity, str(file_path))

                log_row(log_path, {
                    "ts": ts(),
                    "recipient": username,
                    "status": "OK",
                    "file": file_name,
                    "details": ""
                })
                print(f"[{idx}/{len(recipients)}] OK -> {username} : {file_name}")

            except FloodWaitError as e:
                wait_sec = int(getattr(e, "seconds", 60))
                details = f"FloodWait {wait_sec}s"
                log_row(log_path, {
                    "ts": ts(),
                    "recipient": username,
                    "status": "FLOOD_WAIT",
                    "file": file_name,
                    "details": details
                })
                print(f"[{idx}/{len(recipients)}] FLOOD_WAIT -> {username} : {details}")
                print("Стоп, чтобы не усугублять. Запусти позже.")
                return

            except RPCError as e:
                details = f"RPCError: {type(e).__name__} {str(e)}"
                log_row(log_path, {
                    "ts": ts(),
                    "recipient": username,
                    "status": "ERROR",
                    "file": file_name,
                    "details": details
                })
                print(f"[{idx}/{len(recipients)}] ERROR -> {username}: {details}")

            except Exception as e:
                details = f"Exception: {type(e).__name__} {str(e)}"
                log_row(log_path, {
                    "ts": ts(),
                    "recipient": username,
                    "status": "ERROR",
                    "file": file_name,
                    "details": details
                })
                print(f"[{idx}/{len(recipients)}] ERROR -> {username}: {details}")

            # Турбо-задержка между получателями
            await asyncio.sleep(random.uniform(min_delay, max_delay))

    print("Готово. Файлы НЕ перемещались.")
    print(f"Лог: {log_path}")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as e:
        print("ОШИБКА при запуске:", file=sys.stderr)
        print(type(e).__name__, str(e), file=sys.stderr)
        import traceback
        traceback.print_exc()
        sys.exit(1)
