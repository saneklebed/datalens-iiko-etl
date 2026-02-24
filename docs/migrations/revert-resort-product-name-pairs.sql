-- ОТКАТ миграции add-resort-product-name-pairs.sql
-- Применять в Neon, если миграция add-resort-product-name-pairs.sql уже была выполнена.
-- Восстанавливает исходный view weekly_wrong_receipt_mirror_products (только логика «один товар — два филиала»)
-- и удаляет таблицу resort_product_name_pairs.

DROP VIEW IF EXISTS inventory_core.weekly_wrong_receipt_mirror_products;

CREATE VIEW inventory_core.weekly_wrong_receipt_mirror_products AS
WITH mirror_pairs AS (
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
marked AS (
    SELECT week_start, week_end, department_a AS department, product_num FROM mirror_pairs
    UNION
    SELECT week_start, week_end, department_b, product_num FROM mirror_pairs
)
SELECT week_start,
       week_end,
       department,
       product_num,
       true AS is_wrong_receipt_mirror
FROM marked;

DROP TABLE IF EXISTS inventory_core.resort_product_name_pairs;
