-- Добавляет в витрину inventory_mart.weekly_deviation_products_money_v2
-- показатель excess_deviation_money: превышение нормы отклонений в деньгах
-- с учётом и недостач, и излишков (по модулю отклонения).
--
-- Логика:
--  - allowed_loss_money = movement_money * norm_pct (как и раньше);
--  - deviation_money_clean — отклонение в деньгах со знаком;
--  - excess_deviation_money = max(0, |deviation_money_clean| - allowed_loss_money).
--
-- Старое поле excess_loss_money остаётся как было (только для недостач).

CREATE OR REPLACE VIEW inventory_mart.weekly_deviation_products_money_v2 AS
WITH m AS (
    SELECT
        w.week_start,
        w.week_end,
        w.department,
        w.product_num,
        max(w.product_name)          AS product_name,
        max(w.product_category)      AS product_category,
        max(w.product_measure_unit)  AS product_measure_unit,
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
        ic.deviation_money_clean,
        ic.deviation_money_signed,
        ic.shortage_money,
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
), base AS (
    SELECT
        COALESCE(m.week_start, c.week_start)                     AS week_start,
        COALESCE(m.week_end, c.week_end)                         AS week_end,
        COALESCE(m.department, c.department)                     AS department,
        COALESCE(m.product_num, c.product_num)                   AS product_num,
        COALESCE(m.product_name, n.product_name)                 AS product_name,
        COALESCE(m.product_category, n.product_category)         AS product_category,
        COALESCE(m.product_measure_unit, n.product_measure_unit) AS product_measure_unit,
        COALESCE(m.movement_money, 0::numeric)                   AS movement_money,
        COALESCE(c.shortage_money, 0::numeric)                   AS shortage_money,
        COALESCE(c.surplus_money, 0::numeric)                    AS surplus_money,
        COALESCE(c.deviation_money_clean, 0::numeric)            AS deviation_money_clean,
        COALESCE(m.movement_money, 0::numeric)
            * COALESCE(n.norm_pct, 0.02)                         AS norm_money,
        n.norm_note,
        COALESCE(m.movement_money, 0::numeric)
            * COALESCE(n.norm_pct, 0.02)                         AS allowed_loss_money,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
                THEN 0::numeric
            ELSE GREATEST(
                0::numeric,
                COALESCE(c.shortage_money, 0::numeric)
                    - COALESCE(m.movement_money, 0::numeric)
                      * COALESCE(n.norm_pct, 0.02)
            )
        END                                                      AS excess_loss_money,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
                THEN 0::numeric
            ELSE GREATEST(
                0::numeric,
                COALESCE(c.shortage_money, 0::numeric)
                    - COALESCE(m.movement_money, 0::numeric)
                      * COALESCE(n.norm_pct, 0.02)
            )
        END                                                      AS potential_loss_week,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric
                THEN 0::numeric
            ELSE GREATEST(
                0::numeric,
                COALESCE(c.shortage_money, 0::numeric)
                    - COALESCE(m.movement_money, 0::numeric)
                      * COALESCE(n.norm_pct, 0.02)
            ) * 4::numeric
        END                                                      AS potential_loss_month,
        COALESCE(c.deviation_money_signed, 0::numeric)           AS deviation_money_signed,
        COALESCE(m.movement_qty, 0::numeric)                     AS movement_qty
    FROM m
    FULL JOIN c
        ON m.week_start = c.week_start
       AND m.week_end   = c.week_end
       AND m.department = c.department
       AND m.product_num = c.product_num
    LEFT JOIN n
        ON n.department = COALESCE(m.department, c.department)
       AND n.product_num = COALESCE(m.product_num, c.product_num)
), qc_prev AS (
    SELECT
        q.week_start,
        q.week_end,
        q.department,
        q.product_num,
        q.is_wrong_prev_inventory,
        q.prev_deviation_qty_signed
    FROM inventory_core.weekly_prev_miscount_last_week_products q
)
SELECT
    b.week_start,
    b.week_end,
    b.department,
    b.product_num,
    b.product_name,
    b.product_category,
    b.product_measure_unit,
    b.movement_money,
    b.shortage_money,
    b.surplus_money,
    b.deviation_money_clean,
    b.norm_money,
    b.norm_note,
    b.allowed_loss_money,
    b.excess_loss_money,
    b.potential_loss_week,
    b.potential_loss_month,
    b.deviation_money_signed,
    b.movement_qty,
    COALESCE(q.is_wrong_prev_inventory, false)          AS is_wrong_prev_inventory,
    COALESCE(q.prev_deviation_qty_signed, 0::numeric)   AS prev_deviation_qty_signed,
    COALESCE(mi.is_missing_inventory_position, false)   AS is_missing_inventory_position,
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
    END                                                AS wrong_receipt_reason,
    COALESCE(r.is_possible_resort, false)              AS is_possible_resort,
    -- Новый показатель: превышение нормы в деньгах по модулю отклонения
    CASE
        WHEN abs(b.deviation_money_clean) <= b.allowed_loss_money
            THEN 0::numeric
        ELSE GREATEST(
            0::numeric,
            abs(b.deviation_money_clean) - b.allowed_loss_money
        )
    END AS excess_deviation_money
FROM base b
LEFT JOIN qc_prev q
    ON q.week_start = b.week_start
   AND q.week_end   = b.week_end
   AND q.department = b.department
   AND q.product_num = b.product_num
LEFT JOIN inventory_core.weekly_missing_inventory_positions_products mi
    ON mi.week_start = b.week_start
   AND mi.week_end   = b.week_end
   AND mi.department = b.department
   AND mi.product_num = b.product_num
LEFT JOIN inventory_core.weekly_wrong_receipt_type_products wr
    ON wr.week_start = b.week_start
   AND wr.week_end   = b.week_end
   AND wr.department = b.department
   AND wr.product_num = b.product_num
LEFT JOIN inventory_core.weekly_possible_resort_products r
    ON r.week_start = b.week_start
   AND r.week_end   = b.week_end
   AND r.department = b.department
   AND r.product_num = b.product_num;

