-- ОТКАЧЕНО — НЕ ПРИМЕНЯТЬ.
-- Логика пересорта и поле is_possible_resort живут в витрине inventory_mart.weekly_deviation_products_money_v2 (Neon),
-- а не в core view weekly_wrong_receipt_mirror_products. Изменения ниже делались не в той вьюхе.
-- Для отката в Neon (если миграция уже выполнялась): см. revert-resort-product-name-pairs.sql.
--
-- Ниже оставлен исходный текст миграции только для истории.
-- =============================================================================
-- Пересорт товаров: пары разных товаров по названию (один филиал, противоположные отклонения ~ по модулю).
-- Выполнить в Neon после подключения к БД.

-- 1. Таблица пар товаров по названию (пересорт: перепутали при инвентаризации)
CREATE TABLE IF NOT EXISTS inventory_core.resort_product_name_pairs (
    product_name_1 text NOT NULL,
    product_name_2 text NOT NULL,
    CONSTRAINT resort_product_name_pairs_order CHECK (product_name_1 < product_name_2),
    CONSTRAINT resort_product_name_pairs_uniq UNIQUE (product_name_1, product_name_2)
);

COMMENT ON TABLE inventory_core.resort_product_name_pairs IS 'Пары товаров по названию для детекции пересорта: если в одной инвентаризации у одного −X кг, у другого +Y кг и значения близки — пересорт.';

-- 2. Добавляем пару: Говядина мякоть ↔ Говядина лопатка (для персонала)
INSERT INTO inventory_core.resort_product_name_pairs (product_name_1, product_name_2)
VALUES (
    'Говядина лопатка (для персонала)',
    'Говядина мякоть'
)
ON CONFLICT (product_name_1, product_name_2) DO NOTHING;

-- 3. Пересоздаём view: прежняя логика (один product_num, два филиала) + новая (пара по названию, один филиал)
DROP VIEW IF EXISTS inventory_core.weekly_wrong_receipt_mirror_products;

CREATE VIEW inventory_core.weekly_wrong_receipt_mirror_products AS
WITH
-- Текущая логика: один товар (product_num), два филиала, противоположные отклонения
mirror_pairs_same_product AS (
    SELECT a.week_start,
           a.week_end,
           a.product_num,
           a.department AS department_a,
           b.department AS department_b
    FROM inventory_core.inventory_correction_clean_products a
    JOIN inventory_core.inventory_correction_clean_products b
      ON a.week_start = b.week_start
     AND a.week_end = b.week_end
     AND a.product_num = b.product_num
     AND a.department < b.department
    WHERE sign(COALESCE(a.deviation_qty_signed, 0::numeric)) = (- sign(COALESCE(b.deviation_qty_signed, 0::numeric)))
      AND sign(COALESCE(a.deviation_qty_signed, 0::numeric)) <> 0::numeric
      AND abs(COALESCE(a.deviation_qty_signed, 0::numeric)) >= 0.001
      AND abs(COALESCE(b.deviation_qty_signed, 0::numeric)) >= 0.001
      AND (abs(abs(COALESCE(a.deviation_qty_signed, 0::numeric)) - abs(COALESCE(b.deviation_qty_signed, 0::numeric))) / NULLIF(GREATEST(abs(COALESCE(a.deviation_qty_signed, 0::numeric)), abs(COALESCE(b.deviation_qty_signed, 0::numeric))), 0::numeric)) <= 0.20
),
-- Новая логика: пара разных товаров по названию, один филиал, противоположные отклонения (пересорт)
resort_pairs_by_name AS (
    SELECT a.week_start,
           a.week_end,
           a.department,
           a.product_num AS product_num_a,
           b.product_num AS product_num_b
    FROM inventory_core.inventory_correction_clean_products a
    JOIN inventory_core.inventory_correction_clean_products b
      ON a.week_start = b.week_start
     AND a.week_end = b.week_end
     AND a.department = b.department
     AND a.product_num < b.product_num
    JOIN inventory_core.resort_product_name_pairs p
      ON (    (p.product_name_1 = TRIM(COALESCE(a.product_name, '')) AND p.product_name_2 = TRIM(COALESCE(b.product_name, '')))
           OR (p.product_name_1 = TRIM(COALESCE(b.product_name, '')) AND p.product_name_2 = TRIM(COALESCE(a.product_name, '')))
          )
    WHERE sign(COALESCE(a.deviation_qty_signed, 0::numeric)) = (- sign(COALESCE(b.deviation_qty_signed, 0::numeric)))
      AND sign(COALESCE(a.deviation_qty_signed, 0::numeric)) <> 0::numeric
      AND abs(COALESCE(a.deviation_qty_signed, 0::numeric)) >= 0.001
      AND abs(COALESCE(b.deviation_qty_signed, 0::numeric)) >= 0.001
      AND (abs(abs(COALESCE(a.deviation_qty_signed, 0::numeric)) - abs(COALESCE(b.deviation_qty_signed, 0::numeric))) / NULLIF(GREATEST(abs(COALESCE(a.deviation_qty_signed, 0::numeric)), abs(COALESCE(b.deviation_qty_signed, 0::numeric))), 0::numeric)) <= 0.20
),
marked_same AS (
    SELECT week_start, week_end, department_a AS department, product_num FROM mirror_pairs_same_product
    UNION
    SELECT week_start, week_end, department_b, product_num FROM mirror_pairs_same_product
),
marked_resort AS (
    SELECT week_start, week_end, department, product_num_a AS product_num FROM resort_pairs_by_name
    UNION
    SELECT week_start, week_end, department, product_num_b FROM resort_pairs_by_name
),
marked AS (
    SELECT week_start, week_end, department, product_num FROM marked_same
    UNION
    SELECT week_start, week_end, department, product_num FROM marked_resort
)
SELECT week_start,
       week_end,
       department,
       product_num,
       true AS is_wrong_receipt_mirror
FROM marked;
