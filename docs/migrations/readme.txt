Миграции Neon (PostgreSQL). Выполнять вручную в клиенте БД (подключение по NEON_* из .env).

revert-resort-product-name-pairs.sql
  - ОТКАТ: если выполнялась миграция add-resort-product-name-pairs.sql. Восстанавливает исходный view inventory_core.weekly_wrong_receipt_mirror_products (только логика «один товар — два филиала»), удаляет таблицу inventory_core.resort_product_name_pairs. Выполнить в Neon при необходимости отката.

add-resort-product-name-pairs.sql
  - ОТКАЧЕНО — не применять. Логика пересорта (is_possible_resort) живёт в витрине inventory_mart.weekly_deviation_products_money_v2, не в core. См. revert-resort-product-name-pairs.sql для отката.

add-resort-pair-beef.sql
  - Добавляет пару для пересорта: Говядина мякоть ↔ Говядина лопатка (для персонала). INSERT в inventory_core.resort_product_pairs (product_num_1, product_num_2); артикулы подставляются по названиям из inventory_correction_clean_products. Выполнить в Neon один раз.

resort-threshold-25pct.sql
  - Меняет порог пересорта во view weekly_possible_resort_products с 10% на 25%: пара помечается is_possible_resort, если модули отклонений отличаются не более чем на 25%. Выполнить в Neon один раз.

weekly_product_documents_include_spoilage.sql
  - Таблица списаний в дашборде (датасет weekly_product_documents_products): показывать в т.ч. списания с типом «Порча». Движение (оборот за неделю) по-прежнему считается без Порчи — фильтр остаётся в inventory_core.transactions; view weekly_product_documents_products переведён на чтение base из olap_postings (без фильтра по contr_account_name). Выполнить в Neon один раз.

После любых изменений в Neon при необходимости обновить дамп: python scripts/dump_neon_ddl.py и python scripts/dump_neon_schema.py (или workflow Dump Neon schema).
