-- Добавляет в витрину inventory_mart.weekly_deviation_products_qty
-- флаг is_missing_inventory_position (несохранённые позиции) по аналогии
-- с витриной weekly_deviation_products_money_v2.
--
-- Логика:
--   - join к inventory_core.weekly_missing_inventory_positions_products;
--   - новая колонка is_missing_inventory_position в конце SELECT.

CREATE OR REPLACE VIEW inventory_mart.weekly_deviation_products_qty AS
WITH m AS (
    SELECT
        w.week_start,
        w.week_end,
        w.department,
        w.product_num,
        max(w.product_name)         AS product_name,
        max(w.product_category)     AS product_category,
        max(w.product_measure_unit) AS product_measure_unit,
        w.movement_qty,
        w.movement_money
    FROM inventory_core.weekly_movement_products w
    GROUP BY
        w.week_start,
        w.week_end,
        w.department,
        w.product_num,
        w.movement_qty,
        w.movement_money
), c AS (
    SELECT
        ic.week_start,
        ic.week_end,
        ic.department,
        ic.product_num,
        ic.deviation_qty_signed,
        ic.deviation_money_clean,
        ic.shortage_qty,
        ic.shortage_money,
        ic.surplus_qty,
        ic.surplus_money
    FROM inventory_core.inventory_correction_clean_products ic
), n AS (
    SELECT
        pn.department,
        pn.product_num,
        pn.norm_pct,
        pn.norm_note,
        pn.product_name,
        pn.product_category,
        pn.product_measure_unit
    FROM inventory_core.product_norm_effective pn
), qc AS (
    SELECT
        q.week_start,
        q.department,
        q.product_num,
        q.prev_deviation_qty_signed,
        q.is_wrong_prev_inventory
    FROM inventory_core.weekly_prev_miscount_last_week_products q
)
SELECT
    COALESCE(m.week_start, c.week_start)                AS week_start,
    COALESCE(m.week_end, c.week_end)                    AS week_end,
    COALESCE(m.department, c.department)                AS department,
    COALESCE(m.product_num, c.product_num)              AS product_num,
    COALESCE(m.product_name, n.product_name)            AS product_name,
    COALESCE(m.product_category, n.product_category)    AS product_category,
    COALESCE(m.product_measure_unit, n.product_measure_unit)
                                                       AS product_measure_unit,
    COALESCE(m.movement_qty, 0::numeric)                AS movement_qty,
    COALESCE(m.movement_money, 0::numeric)              AS movement_money,
    COALESCE(c.deviation_qty_signed, 0::numeric)        AS deviation_qty_signed,
    COALESCE(c.deviation_money_clean, 0::numeric)       AS deviation_money_clean,
    COALESCE(c.shortage_qty, 0::numeric)                AS shortage_qty,
    COALESCE(c.shortage_money, 0::numeric)              AS shortage_money,
    COALESCE(c.surplus_qty, 0::numeric)                 AS surplus_qty,
    COALESCE(c.surplus_money, 0::numeric)               AS surplus_money,
    COALESCE(n.norm_pct, 0.02)                          AS norm_pct,
    n.norm_note,
    CASE
        WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric
            THEN NULL::numeric
        ELSE COALESCE(c.deviation_qty_signed, 0::numeric)
             / NULLIF(m.movement_qty, 0::numeric)
    END                                                 AS fact_deviation_pct_qty,
    CASE
        WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric
            THEN NULL::numeric
        ELSE COALESCE(c.shortage_qty, 0::numeric)
             / NULLIF(m.movement_qty, 0::numeric)
    END                                                 AS fact_shortage_pct_qty,
    CASE
        WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric
            THEN NULL::numeric
        ELSE COALESCE(c.surplus_qty, 0::numeric)
             / NULLIF(m.movement_qty, 0::numeric)
    END                                                 AS fact_surplus_pct_qty,
    CASE
        WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric
            THEN NULL::numeric
        ELSE GREATEST(
            0::numeric,
            abs(COALESCE(c.deviation_qty_signed, 0::numeric)
                / NULLIF(m.movement_qty, 0::numeric))
            - COALESCE(n.norm_pct, 0.02)
        )
    END                                                 AS excess_pct_qty,
    CASE
        WHEN COALESCE(m.movement_money, 0::numeric) = 0::numeric
            THEN NULL::numeric
        ELSE COALESCE(c.deviation_money_clean, 0::numeric)
             / NULLIF(m.movement_money, 0::numeric)
    END                                                 AS deviation_pct_of_movement_money,
    COALESCE(m.movement_money, 0::numeric)
        * COALESCE(n.norm_pct, 0.02)                    AS allowed_loss_money,
    CASE
        WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
            THEN 0::numeric
        ELSE GREATEST(
            0::numeric,
            COALESCE(c.shortage_money, 0::numeric)
                - COALESCE(m.movement_money, 0::numeric)
                  * COALESCE(n.norm_pct, 0.02)
        )
    END                                                 AS excess_loss_money,
    CASE
        WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
            THEN 0::numeric
        ELSE GREATEST(
            0::numeric,
            COALESCE(c.shortage_money, 0::numeric)
                - COALESCE(m.movement_money, 0::numeric)
                  * COALESCE(n.norm_pct, 0.02)
        )
    END                                                 AS potential_loss_week,
    CASE
        WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
            THEN 0::numeric
        ELSE GREATEST(
            0::numeric,
            COALESCE(c.shortage_money, 0::numeric)
                - COALESCE(m.movement_money, 0::numeric)
                  * COALESCE(n.norm_pct, 0.02)
        ) * 4::numeric
    END                                                 AS potential_loss_month,
    COALESCE(qc.is_wrong_prev_inventory, false)         AS is_wrong_prev_inventory,
    qc.prev_deviation_qty_signed,
    COALESCE(wr.wrong_receipt_type = 'wrong_branch'::text, false)
                                                       AS is_wrong_receipt_mirror,
    COALESCE(wr.is_suspicious_receipt_vs_shortage, false)
                                                       AS is_suspicious_receipt_vs_shortage,
    wr.wrong_receipt_type,
    CASE
        WHEN wr.wrong_receipt_type = 'wrong_branch'::text
            THEN 'Приёмка перепутана между филиалами'::text
        WHEN wr.wrong_receipt_type = 'suspicious_receipt'::text
            THEN 'Вероятно неверно приняли'::text
        ELSE NULL::text
    END                                                 AS wrong_receipt_reason,
    COALESCE(r.is_possible_resort, false)               AS is_possible_resort,
    COALESCE(mi.is_missing_inventory_position, false)   AS is_missing_inventory_position
FROM m
FULL JOIN c
    ON m.week_start = c.week_start
   AND m.week_end   = c.week_end
   AND m.department = c.department
   AND m.product_num = c.product_num
LEFT JOIN n
    ON n.department = COALESCE(m.department, c.department)
   AND n.product_num = COALESCE(m.product_num, c.product_num)
LEFT JOIN qc
    ON qc.week_start = COALESCE(m.week_start, c.week_start)
   AND qc.department = COALESCE(m.department, c.department)
   AND qc.product_num = COALESCE(m.product_num, c.product_num)
LEFT JOIN inventory_core.weekly_wrong_receipt_type_products wr
    ON wr.week_start = COALESCE(m.week_start, c.week_start)
   AND wr.week_end   = COALESCE(m.week_end, c.week_end)
   AND wr.department = COALESCE(m.department, c.department)
   AND wr.product_num = COALESCE(m.product_num, c.product_num)
LEFT JOIN inventory_core.weekly_possible_resort_products r
    ON r.week_start = COALESCE(m.week_start, c.week_start)
   AND r.week_end   = COALESCE(m.week_end, c.week_end)
   AND r.department = COALESCE(m.department, c.department)
   AND r.product_num = COALESCE(m.product_num, c.product_num)
LEFT JOIN inventory_core.weekly_missing_inventory_positions_products mi
    ON mi.week_start = COALESCE(m.week_start, c.week_start)
   AND mi.week_end   = COALESCE(m.week_end, c.week_end)
   AND mi.department = COALESCE(m.department, c.department)
   AND mi.product_num = COALESCE(m.product_num, c.product_num);

