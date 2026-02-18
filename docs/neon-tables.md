# Структура таблиц Neon (PostgreSQL)

Этот файл — контекст для AI и разработки: список схем, таблиц и колонок в Neon.

**Как обновить:** локально `python scripts/dump_neon_schema.py` (нужен `.env` с NEON_*) или в GitHub Actions → workflow **Dump Neon schema** → Run workflow (берёт NEON_* из секретов и коммитит обновлённый файл). Либо вручную вставить вывод из Neon Console / DBeaver.

---

## inventory_raw

### olap_postings
Сырые проводки из iiko OLAP.
- Колонки: (вставить из дампа или описать вручную)

---

## inventory_core

(Таблицы/вьюхи core — вставить из дампа)

---

## inventory_mart

(Витрины — вставить из дампа)

---

*После первого запуска `scripts/dump_neon_schema.py` ниже будет автоматически сгенерированный список.*
