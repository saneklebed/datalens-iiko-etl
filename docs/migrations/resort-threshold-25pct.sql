-- Порог пересорта: с 10% на 25%.
-- Пара помечается is_possible_resort, если модули отклонений отличаются не более чем на 25%.
-- Выполнить в Neon один раз. Используем CREATE OR REPLACE, чтобы не трогать зависимые вьюхи.

CREATE OR REPLACE VIEW inventory_core.weekly_possible_resort_products AS
WITH pairs_match AS (
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
    JOIN inventory_core.resort_product_pairs p
      ON (p.product_num_1 = a.product_num AND p.product_num_2 = b.product_num)
      OR (p.product_num_1 = b.product_num AND p.product_num_2 = a.product_num)
    WHERE sign(COALESCE(a.deviation_qty_signed, 0::numeric)) = (- sign(COALESCE(b.deviation_qty_signed, 0::numeric)))
      AND sign(COALESCE(a.deviation_qty_signed, 0::numeric)) <> 0::numeric
      AND abs(COALESCE(a.deviation_qty_signed, 0::numeric)) >= 0.001
      AND abs(COALESCE(b.deviation_qty_signed, 0::numeric)) >= 0.001
      AND (abs(abs(COALESCE(a.deviation_qty_signed, 0::numeric)) - abs(COALESCE(b.deviation_qty_signed, 0::numeric))) / NULLIF(GREATEST(abs(COALESCE(a.deviation_qty_signed, 0::numeric)), abs(COALESCE(b.deviation_qty_signed, 0::numeric))), 0::numeric)) <= 0.25
),
marked AS (
    SELECT week_start, week_end, department, product_num_a AS product_num FROM pairs_match
    UNION
    SELECT week_start, week_end, department, product_num_b FROM pairs_match
)
SELECT week_start,
       week_end,
       department,
       product_num,
       true AS is_possible_resort
FROM marked;
