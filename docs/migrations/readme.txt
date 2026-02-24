Миграции Neon (PostgreSQL). Выполнять вручную в клиенте БД (подключение по NEON_* из .env).

revert-resort-product-name-pairs.sql
  - ОТКАТ: если выполнялась миграция add-resort-product-name-pairs.sql. Восстанавливает исходный view inventory_core.weekly_wrong_receipt_mirror_products (только логика «один товар — два филиала»), удаляет таблицу inventory_core.resort_product_name_pairs. Выполнить в Neon при необходимости отката.

add-resort-product-name-pairs.sql
  - ОТКАЧЕНО — не применять. Логика пересорта (is_possible_resort) живёт в витрине inventory_mart.weekly_deviation_products_money_v2, не в core. См. revert-resort-product-name-pairs.sql для отката.

После любых изменений в Neon при необходимости обновить дамп: python scripts/dump_neon_ddl.py и python scripts/dump_neon_schema.py (или workflow Dump Neon schema).
