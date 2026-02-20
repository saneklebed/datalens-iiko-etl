-- Автогенерация: DDL объектов Neon для схем inventory_raw, inventory_core, inventory_mart
-- Источник: scripts/dump_neon_ddl.py

-- === TABLES (approximate CREATE TABLE from information_schema) ===

CREATE TABLE inventory_core.deviation_norm_rules (
    rule_id bigint NOT NULL,
    department text,
    product_num text,
    product_category text,
    measure_unit text,
    turnover_group text,
    norm_pct numeric NOT NULL,
    priority integer NOT NULL,
    is_active boolean NOT NULL,
    note text,
    updated_at timestamp with time zone NOT NULL
);

CREATE TABLE inventory_core.inventory_correction_clean_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    deviation_qty_signed numeric,
    deviation_money_clean numeric,
    shortage_qty numeric,
    shortage_money numeric,
    surplus_qty numeric,
    surplus_money numeric,
    deviation_money_signed numeric
);

CREATE TABLE inventory_core.product_norm_effective (
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    norm_pct numeric,
    norm_note text
);

CREATE TABLE inventory_core.raw_inventory_missing_products (
    report_id text,
    department text,
    prev_inv_date date,
    last_inv_date date,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    is_missing_in_last_inventory boolean
);

CREATE TABLE inventory_core.raw_inventory_products_count (
    report_id text,
    week_start date,
    week_end date,
    department text,
    inv_date date,
    products_cnt bigint
);

CREATE TABLE inventory_core.transactions (
    report_id text,
    date_from date,
    date_to date,
    department text,
    posting_dt timestamp with time zone,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    transaction_type text,
    amount_out numeric,
    amount_in numeric,
    sum_outgoing numeric,
    sum_incoming numeric,
    source_hash text,
    loaded_at timestamp with time zone,
    is_movement boolean,
    is_inventory_correction boolean,
    contr_account_name text
);

CREATE TABLE inventory_core.transactions_products (
    report_id text,
    date_from date,
    date_to date,
    department text,
    posting_dt timestamp with time zone,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    transaction_type text,
    amount_out numeric,
    amount_in numeric,
    sum_outgoing numeric,
    sum_incoming numeric,
    source_hash text,
    loaded_at timestamp with time zone,
    is_movement boolean,
    is_inventory_correction boolean
);

CREATE TABLE inventory_core.weekly_inventory_completeness_vs_dmd_last_week (
    week_start date,
    week_end date,
    department text,
    expected_cnt bigint,
    actual_cnt bigint,
    completeness_pct numeric
);

CREATE TABLE inventory_core.weekly_inventory_correction (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    amount_out_sum numeric,
    amount_in_sum numeric,
    sum_outgoing_sum numeric,
    sum_incoming_sum numeric
);

CREATE TABLE inventory_core.weekly_inventory_correction_products_raw (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    amount_out_sum numeric,
    amount_in_sum numeric,
    sum_outgoing_sum numeric,
    sum_incoming_sum numeric
);

CREATE TABLE inventory_core.weekly_missing_inventory_positions_products (
    week_start date,
    week_end date,
    department text,
    inv_date date,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_qty numeric,
    is_missing_inventory_position boolean
);

CREATE TABLE inventory_core.weekly_missing_items_vs_dmd_last_week (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    is_missing_in_last_inventory boolean
);

CREATE TABLE inventory_core.weekly_movement (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_qty numeric,
    movement_money numeric
);

CREATE TABLE inventory_core.weekly_movement_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_qty numeric,
    movement_money numeric
);

CREATE TABLE inventory_core.weekly_prev_miscount_last_week_products (
    prev_week date,
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    prev_deviation_qty_signed numeric,
    deviation_qty_signed numeric,
    is_wrong_prev_inventory boolean
);

CREATE TABLE inventory_core.weekly_product_documents_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    transaction_type text,
    posting_dt timestamp with time zone,
    contr_account_name text,
    qty_signed numeric,
    money_signed numeric
);

CREATE TABLE inventory_core.weekly_wrong_receipt_mirror_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    is_wrong_receipt_mirror boolean
);

CREATE TABLE inventory_mart.sandbox_inventory_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_money numeric,
    shortage_money numeric,
    surplus_money numeric,
    deviation_money_clean numeric,
    norm_money numeric,
    norm_note text,
    allowed_loss_money numeric,
    excess_loss_money numeric,
    potential_loss_week numeric,
    potential_loss_month numeric,
    deviation_money_signed numeric,
    movement_qty numeric,
    is_wrong_prev_inventory boolean,
    prev_deviation_qty_signed numeric,
    is_missing_inventory_position boolean,
    is_wrong_receipt_mirror boolean,
    qty_movement_qty numeric,
    qty_movement_money numeric,
    deviation_qty_signed numeric,
    qty_deviation_money_clean numeric,
    shortage_qty numeric,
    qty_shortage_money numeric,
    surplus_qty numeric,
    qty_surplus_money numeric,
    norm_pct numeric,
    fact_deviation_pct_qty numeric,
    fact_shortage_pct_qty numeric,
    fact_surplus_pct_qty numeric,
    excess_pct_qty numeric,
    deviation_pct_of_movement_money numeric
);

CREATE TABLE inventory_mart.weekly_deviation_products_money_v2 (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_money numeric,
    shortage_money numeric,
    surplus_money numeric,
    deviation_money_clean numeric,
    norm_money numeric,
    norm_note text,
    allowed_loss_money numeric,
    excess_loss_money numeric,
    potential_loss_week numeric,
    potential_loss_month numeric,
    deviation_money_signed numeric,
    movement_qty numeric,
    is_wrong_prev_inventory boolean,
    prev_deviation_qty_signed numeric,
    is_missing_inventory_position boolean,
    is_wrong_receipt_mirror boolean
);

CREATE TABLE inventory_mart.weekly_deviation_products_qty (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    movement_qty numeric,
    movement_money numeric,
    deviation_qty_signed numeric,
    deviation_money_clean numeric,
    shortage_qty numeric,
    shortage_money numeric,
    surplus_qty numeric,
    surplus_money numeric,
    norm_pct numeric,
    norm_note text,
    fact_deviation_pct_qty numeric,
    fact_shortage_pct_qty numeric,
    fact_surplus_pct_qty numeric,
    excess_pct_qty numeric,
    deviation_pct_of_movement_money numeric,
    allowed_loss_money numeric,
    excess_loss_money numeric,
    potential_loss_week numeric,
    potential_loss_month numeric,
    is_wrong_prev_inventory boolean,
    prev_deviation_qty_signed numeric,
    is_wrong_receipt_mirror boolean
);

CREATE TABLE inventory_mart.weekly_product_documents_products (
    week_start date,
    week_end date,
    department text,
    product_num text,
    product_name text,
    product_category text,
    product_measure_unit text,
    transaction_type text,
    posting_dt timestamp with time zone,
    contr_account_name text,
    qty_signed numeric,
    money_signed numeric,
    week_num bigint,
    week_label text
);

CREATE TABLE inventory_raw.olap_postings (
    report_id text NOT NULL,
    date_from date NOT NULL,
    date_to date NOT NULL,
    department text NOT NULL,
    posting_dt timestamp with time zone NOT NULL,
    product_num text NOT NULL,
    product_name text,
    transaction_type text NOT NULL,
    amount_out numeric NOT NULL,
    sum_outgoing numeric NOT NULL,
    source_hash text NOT NULL,
    loaded_at timestamp with time zone NOT NULL,
    product_category text,
    amount_in numeric NOT NULL,
    sum_incoming numeric NOT NULL,
    product_measure_unit text,
    contr_account_name text
);

-- === VIEWS / MATERIALIZED VIEWS ===

CREATE VIEW inventory_core.inventory_correction_clean_products AS
 WITH agg AS (
         SELECT transactions_products.date_from AS week_start,
            transactions_products.date_to AS week_end,
            transactions_products.department,
            transactions_products.product_num,
            max(transactions_products.product_name) AS product_name,
            max(transactions_products.product_category) AS product_category,
            max(transactions_products.product_measure_unit) AS product_measure_unit,
            sum(transactions_products.amount_out) AS qty_out,
            sum(transactions_products.amount_in) AS qty_in,
            sum(transactions_products.sum_outgoing) AS money_out,
            sum(transactions_products.sum_incoming) AS money_in
           FROM inventory_core.transactions_products
          WHERE transactions_products.is_inventory_correction
          GROUP BY transactions_products.date_from, transactions_products.date_to, transactions_products.department, transactions_products.product_num
        ), calc AS (
         SELECT agg.week_start,
            agg.week_end,
            agg.department,
            agg.product_num,
            agg.product_name,
            agg.product_category,
            agg.product_measure_unit,
            agg.qty_in - agg.qty_out AS deviation_qty_signed,
            GREATEST(0::numeric, agg.qty_out - agg.qty_in) AS shortage_qty,
            GREATEST(0::numeric, agg.qty_in - agg.qty_out) AS surplus_qty,
                CASE
                    WHEN agg.qty_out > agg.qty_in THEN agg.money_out
                    ELSE 0::numeric
                END AS shortage_money,
                CASE
                    WHEN agg.qty_in > agg.qty_out THEN agg.money_in
                    ELSE 0::numeric
                END AS surplus_money
           FROM agg
        )
 SELECT week_start,
    week_end,
    department,
    product_num,
    product_name,
    product_category,
    product_measure_unit,
    deviation_qty_signed,
        CASE
            WHEN deviation_qty_signed < 0::numeric THEN shortage_money
            WHEN deviation_qty_signed > 0::numeric THEN surplus_money
            ELSE 0::numeric
        END AS deviation_money_clean,
    shortage_qty,
    shortage_money,
    surplus_qty,
    surplus_money,
        CASE
            WHEN deviation_qty_signed < 0::numeric THEN - shortage_money
            WHEN deviation_qty_signed > 0::numeric THEN surplus_money
            ELSE 0::numeric
        END AS deviation_money_signed
   FROM calc
;

CREATE VIEW inventory_core.product_norm_effective AS
 WITH base AS (
         SELECT t.department,
            t.product_num,
            max(t.product_name) AS product_name,
            max(t.product_category) AS product_category,
            max(t.product_measure_unit) AS product_measure_unit
           FROM inventory_core.transactions_products t
          GROUP BY t.department, t.product_num
        ), picked_rule AS (
         SELECT b_1.department,
            b_1.product_num,
            r.norm_pct,
            r.note,
            r.priority
           FROM base b_1
             LEFT JOIN LATERAL ( SELECT r_1.rule_id,
                    r_1.department,
                    r_1.product_num,
                    r_1.product_category,
                    r_1.measure_unit,
                    r_1.turnover_group,
                    r_1.norm_pct,
                    r_1.priority,
                    r_1.is_active,
                    r_1.note,
                    r_1.updated_at
                   FROM inventory_core.deviation_norm_rules r_1
                  WHERE r_1.is_active = true AND (r_1.department IS NULL OR r_1.department = b_1.department) AND (r_1.product_num IS NULL OR r_1.product_num = b_1.product_num)
                  ORDER BY (
                        CASE
                            WHEN r_1.product_num IS NOT NULL THEN 0
                            ELSE 1
                        END), (
                        CASE
                            WHEN r_1.department IS NOT NULL THEN 0
                            ELSE 1
                        END), r_1.priority, r_1.updated_at DESC
                 LIMIT 1) r ON true
        )
 SELECT b.department,
    b.product_num,
    b.product_name,
    b.product_category,
    b.product_measure_unit,
    COALESCE(pr.norm_pct,
        CASE
            WHEN lower(TRIM(BOTH FROM COALESCE(b.product_measure_unit, ''::text))) = 'кг'::text THEN 0.05
            ELSE 0.02
        END) AS norm_pct,
    pr.note AS norm_note
   FROM base b
     LEFT JOIN picked_rule pr ON pr.department = b.department AND pr.product_num = b.product_num
;

CREATE VIEW inventory_core.raw_inventory_missing_products AS
 WITH inv_days AS (
         SELECT olap_postings.report_id,
            olap_postings.department,
            olap_postings.posting_dt::date AS inv_date,
            row_number() OVER (PARTITION BY olap_postings.report_id, olap_postings.department ORDER BY (olap_postings.posting_dt::date) DESC) AS rn
           FROM inventory_raw.olap_postings
          WHERE olap_postings.transaction_type = 'INVENTORY_CORRECTION'::text AND olap_postings.product_category = 'Продукты'::text
          GROUP BY olap_postings.report_id, olap_postings.department, (olap_postings.posting_dt::date)
        ), pairs AS (
         SELECT inv_days.report_id,
            inv_days.department,
            max(
                CASE
                    WHEN inv_days.rn = 1 THEN inv_days.inv_date
                    ELSE NULL::date
                END) AS last_inv_date,
            max(
                CASE
                    WHEN inv_days.rn = 2 THEN inv_days.inv_date
                    ELSE NULL::date
                END) AS prev_inv_date
           FROM inv_days
          GROUP BY inv_days.report_id, inv_days.department
        ), prev_items AS (
         SELECT DISTINCT p.report_id,
            p.department,
            p.product_num,
            p.product_name,
            p.product_category,
            p.product_measure_unit
           FROM inventory_raw.olap_postings p
             JOIN pairs d_1 ON d_1.report_id = p.report_id AND d_1.department = p.department AND p.posting_dt::date = d_1.prev_inv_date
          WHERE p.transaction_type = 'INVENTORY_CORRECTION'::text AND p.product_category = 'Продукты'::text
        ), last_items AS (
         SELECT DISTINCT p.report_id,
            p.department,
            p.product_num
           FROM inventory_raw.olap_postings p
             JOIN pairs d_1 ON d_1.report_id = p.report_id AND d_1.department = p.department AND p.posting_dt::date = d_1.last_inv_date
          WHERE p.transaction_type = 'INVENTORY_CORRECTION'::text AND p.product_category = 'Продукты'::text
        )
 SELECT pr.report_id,
    pr.department,
    d.prev_inv_date,
    d.last_inv_date,
    pr.product_num,
    pr.product_name,
    pr.product_category,
    pr.product_measure_unit,
    true AS is_missing_in_last_inventory
   FROM prev_items pr
     JOIN pairs d ON d.report_id = pr.report_id AND d.department = pr.department
     LEFT JOIN last_items l ON l.report_id = pr.report_id AND l.department = pr.department AND l.product_num = pr.product_num
  WHERE l.product_num IS NULL
;

CREATE VIEW inventory_core.raw_inventory_products_count AS
 WITH inv_days AS (
         SELECT olap_postings.report_id,
            olap_postings.date_from,
            olap_postings.date_to,
            olap_postings.department,
            max(olap_postings.posting_dt::date) AS inv_date
           FROM inventory_raw.olap_postings
          WHERE olap_postings.transaction_type = 'INVENTORY_CORRECTION'::text AND olap_postings.product_category = 'Продукты'::text
          GROUP BY olap_postings.report_id, olap_postings.date_from, olap_postings.date_to, olap_postings.department
        ), inv_items AS (
         SELECT DISTINCT p.report_id,
            p.date_from,
            p.date_to,
            p.department,
            p.product_num
           FROM inventory_raw.olap_postings p
             JOIN inv_days d_1 ON d_1.report_id = p.report_id AND d_1.date_from = p.date_from AND d_1.date_to = p.date_to AND d_1.department = p.department AND p.posting_dt::date = d_1.inv_date
          WHERE p.transaction_type = 'INVENTORY_CORRECTION'::text AND p.product_category = 'Продукты'::text
        )
 SELECT d.report_id,
    d.date_from AS week_start,
    d.date_to AS week_end,
    d.department,
    d.inv_date,
    count(*) AS products_cnt
   FROM inv_days d
     JOIN inv_items i ON i.report_id = d.report_id AND i.date_from = d.date_from AND i.date_to = d.date_to AND i.department = d.department
  GROUP BY d.report_id, d.date_from, d.date_to, d.department, d.inv_date
;

CREATE VIEW inventory_core.transactions AS
 SELECT report_id,
    date_from,
    date_to,
    department,
    posting_dt,
    product_num,
    product_name,
    product_category,
    product_measure_unit,
    transaction_type,
    amount_out,
    amount_in,
    sum_outgoing,
    sum_incoming,
    source_hash,
    loaded_at,
    transaction_type = ANY (ARRAY['SESSION_WRITEOFF'::text, 'WRITEOFF'::text, 'PRODUCTION'::text, 'OUTGOING_INVOICE'::text]) AS is_movement,
    transaction_type = 'INVENTORY_CORRECTION'::text AND posting_dt = max(posting_dt) OVER (PARTITION BY date_from, date_to, department, product_num) AS is_inventory_correction,
    contr_account_name
   FROM inventory_raw.olap_postings
  WHERE COALESCE(contr_account_name, ''::text) <> 'Порча'::text
;

CREATE VIEW inventory_core.transactions_products AS
 SELECT report_id,
    date_from,
    date_to,
    department,
    posting_dt,
    product_num,
    product_name,
    product_category,
    product_measure_unit,
    transaction_type,
    amount_out,
    amount_in,
    sum_outgoing,
    sum_incoming,
    source_hash,
    loaded_at,
    is_movement,
    is_inventory_correction
   FROM inventory_core.transactions
  WHERE product_category = 'Продукты'::text
;

CREATE VIEW inventory_core.weekly_inventory_completeness_vs_dmd_last_week AS
 WITH weeks AS (
         SELECT DISTINCT inventory_correction_clean_products.week_start
           FROM inventory_core.inventory_correction_clean_products
          ORDER BY inventory_correction_clean_products.week_start DESC
         LIMIT 1
        ), w AS (
         SELECT max(weeks.week_start) AS last_week
           FROM weeks
        ), dmd AS (
         SELECT DISTINCT c.product_num
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.last_week
          WHERE c.department = 'Домодедово'::text
        ), expected AS (
         SELECT n.department,
            d.product_num
           FROM inventory_core.product_norm_effective n
             JOIN dmd d ON d.product_num = n.product_num
          WHERE n.department <> 'Домодедово'::text
        ), actual AS (
         SELECT DISTINCT c.department,
            c.product_num
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.last_week
        )
 SELECT w.last_week AS week_start,
    (w.last_week + '7 days'::interval)::date AS week_end,
    e.department,
    count(*) AS expected_cnt,
    count(a.product_num) AS actual_cnt,
    count(a.product_num)::numeric / NULLIF(count(*)::numeric, 0::numeric) AS completeness_pct
   FROM expected e
     LEFT JOIN actual a ON a.department = e.department AND a.product_num = e.product_num
     CROSS JOIN w
  GROUP BY w.last_week, e.department
;

CREATE VIEW inventory_core.weekly_inventory_correction AS
 SELECT date_from AS week_start,
    date_to AS week_end,
    department,
    product_num,
    max(product_name) AS product_name,
    max(product_category) AS product_category,
    max(product_measure_unit) AS product_measure_unit,
    sum(amount_out) AS amount_out_sum,
    sum(amount_in) AS amount_in_sum,
    sum(sum_outgoing) AS sum_outgoing_sum,
    sum(sum_incoming) AS sum_incoming_sum
   FROM inventory_core.transactions
  WHERE is_inventory_correction
  GROUP BY date_from, date_to, department, product_num
;

CREATE VIEW inventory_core.weekly_inventory_correction_products_raw AS
 SELECT date_from AS week_start,
    date_to AS week_end,
    department,
    product_num,
    max(product_name) AS product_name,
    max(product_category) AS product_category,
    max(product_measure_unit) AS product_measure_unit,
    sum(amount_out) AS amount_out_sum,
    sum(amount_in) AS amount_in_sum,
    sum(sum_outgoing) AS sum_outgoing_sum,
    sum(sum_incoming) AS sum_incoming_sum
   FROM inventory_core.transactions_products
  WHERE is_inventory_correction
  GROUP BY date_from, date_to, department, product_num
;

CREATE VIEW inventory_core.weekly_missing_inventory_positions_products AS
 WITH inv_day AS (
         SELECT olap_postings.date_from,
            olap_postings.date_to,
            olap_postings.department,
            max(olap_postings.posting_dt::date) AS inv_date
           FROM inventory_raw.olap_postings
          WHERE olap_postings.transaction_type = 'INVENTORY_CORRECTION'::text AND olap_postings.product_category = 'Продукты'::text
          GROUP BY olap_postings.date_from, olap_postings.date_to, olap_postings.department
        ), inv_items AS (
         SELECT DISTINCT p.date_from,
            p.date_to,
            p.department,
            p.product_num
           FROM inventory_raw.olap_postings p
             JOIN inv_day d_1 ON d_1.date_from = p.date_from AND d_1.date_to = p.date_to AND d_1.department = p.department AND p.posting_dt::date = d_1.inv_date
          WHERE p.transaction_type = 'INVENTORY_CORRECTION'::text AND p.product_category = 'Продукты'::text
        ), mv AS (
         SELECT weekly_movement_products.week_start,
            weekly_movement_products.week_end,
            weekly_movement_products.department,
            weekly_movement_products.product_num,
            max(weekly_movement_products.product_name) AS product_name,
            max(weekly_movement_products.product_category) AS product_category,
            max(weekly_movement_products.product_measure_unit) AS product_measure_unit,
            sum(weekly_movement_products.movement_qty) AS movement_qty
           FROM inventory_core.weekly_movement_products
          WHERE weekly_movement_products.product_category = 'Продукты'::text
          GROUP BY weekly_movement_products.week_start, weekly_movement_products.week_end, weekly_movement_products.department, weekly_movement_products.product_num
        )
 SELECT mv.week_start,
    mv.week_end,
    mv.department,
    d.inv_date,
    mv.product_num,
    mv.product_name,
    mv.product_category,
    mv.product_measure_unit,
    mv.movement_qty,
    i.product_num IS NULL AS is_missing_inventory_position
   FROM mv
     JOIN inv_day d ON d.date_from = mv.week_start AND d.date_to = mv.week_end AND d.department = mv.department
     LEFT JOIN inv_items i ON i.date_from = mv.week_start AND i.date_to = mv.week_end AND i.department = mv.department AND i.product_num = mv.product_num
  WHERE mv.movement_qty > 0::numeric
;

CREATE VIEW inventory_core.weekly_missing_items_vs_dmd_last_week AS
 WITH weeks AS (
         SELECT DISTINCT inventory_correction_clean_products.week_start
           FROM inventory_core.inventory_correction_clean_products
          ORDER BY inventory_correction_clean_products.week_start DESC
         LIMIT 1
        ), w AS (
         SELECT max(weeks.week_start) AS last_week
           FROM weeks
        ), dmd AS (
         SELECT DISTINCT c.product_num,
            c.product_name,
            c.product_category,
            c.product_measure_unit
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.last_week
          WHERE c.department = 'Домодедово'::text
        ), expected AS (
         SELECT n.department,
            d.product_num,
            COALESCE(n.product_name, d.product_name) AS product_name,
            COALESCE(n.product_category, d.product_category) AS product_category,
            COALESCE(n.product_measure_unit, d.product_measure_unit) AS product_measure_unit
           FROM inventory_core.product_norm_effective n
             JOIN dmd d ON d.product_num = n.product_num
        ), actual AS (
         SELECT DISTINCT c.department,
            c.product_num
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.last_week
        )
 SELECT w.last_week AS week_start,
    (w.last_week + '7 days'::interval)::date AS week_end,
    e.department,
    e.product_num,
    e.product_name,
    e.product_category,
    e.product_measure_unit,
    true AS is_missing_in_last_inventory
   FROM expected e
     LEFT JOIN actual a ON a.department = e.department AND a.product_num = e.product_num
     CROSS JOIN w
  WHERE a.product_num IS NULL AND e.department <> 'Домодедово'::text
;

CREATE VIEW inventory_core.weekly_movement AS
 SELECT date_from AS week_start,
    date_to AS week_end,
    department,
    product_num,
    max(product_name) AS product_name,
    max(product_category) AS product_category,
    max(product_measure_unit) AS product_measure_unit,
    sum(amount_out) AS movement_qty,
    sum(sum_outgoing) AS movement_money
   FROM inventory_core.transactions
  WHERE is_movement
  GROUP BY date_from, date_to, department, product_num
;

CREATE VIEW inventory_core.weekly_movement_products AS
 SELECT date_from AS week_start,
    date_to AS week_end,
    department,
    product_num,
    max(product_name) AS product_name,
    max(product_category) AS product_category,
    max(product_measure_unit) AS product_measure_unit,
    sum(amount_out) AS movement_qty,
    sum(sum_outgoing) AS movement_money
   FROM inventory_core.transactions_products
  WHERE is_movement
  GROUP BY date_from, date_to, department, product_num
;

CREATE VIEW inventory_core.weekly_prev_miscount_last_week_products AS
 WITH weeks AS (
         SELECT DISTINCT inventory_correction_clean_products.week_start
           FROM inventory_core.inventory_correction_clean_products
          ORDER BY inventory_correction_clean_products.week_start DESC
         LIMIT 2
        ), w AS (
         SELECT max(weeks.week_start) AS last_week,
            min(weeks.week_start) AS prev_week
           FROM weeks
        ), cur AS (
         SELECT c.week_start,
            c.department,
            c.product_num,
            c.product_name,
            c.product_category,
            c.product_measure_unit,
            c.deviation_qty_signed AS cur_dev
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.last_week
        ), prev AS (
         SELECT c.week_start,
            c.department,
            c.product_num,
            c.deviation_qty_signed AS prev_dev
           FROM inventory_core.inventory_correction_clean_products c
             JOIN w w_1 ON c.week_start = w_1.prev_week
        )
 SELECT w.prev_week,
    w.last_week AS week_start,
    (w.last_week + '7 days'::interval)::date AS week_end,
    cur.department,
    cur.product_num,
    cur.product_name,
    cur.product_category,
    cur.product_measure_unit,
    prev.prev_dev AS prev_deviation_qty_signed,
    cur.cur_dev AS deviation_qty_signed,
    prev.prev_dev IS NOT NULL AND abs(prev.prev_dev) >= 0.5 AND abs(cur.cur_dev) >= 0.5 AND sign(prev.prev_dev) <> 0::numeric AND sign(cur.cur_dev) <> 0::numeric AND sign(prev.prev_dev) <> sign(cur.cur_dev) AND abs(cur.cur_dev) >= (abs(prev.prev_dev) * 0.6) AND abs(cur.cur_dev) <= (abs(prev.prev_dev) * 1.4) AS is_wrong_prev_inventory
   FROM cur
     JOIN prev ON prev.department = cur.department AND prev.product_num = cur.product_num
     CROSS JOIN w
;

CREATE VIEW inventory_core.weekly_product_documents_products AS
 WITH base AS (
         SELECT t.department,
            t.posting_dt,
            t.product_num,
            t.product_name,
            t.product_category,
            t.product_measure_unit,
            t.transaction_type,
            t.contr_account_name,
            t.amount_out,
            t.amount_in,
            t.sum_outgoing,
            t.sum_incoming
           FROM inventory_core.transactions t
          WHERE t.product_category = 'Продукты'::text AND (t.transaction_type = ANY (ARRAY['WRITEOFF'::text, 'PRODUCTION'::text, 'OUTGOING_INVOICE'::text, 'INVOICE'::text]))
        ), wk AS (
         SELECT base.department,
            base.posting_dt,
            base.product_num,
            base.product_name,
            base.product_category,
            base.product_measure_unit,
            base.transaction_type,
            base.contr_account_name,
            base.amount_out,
            base.amount_in,
            base.sum_outgoing,
            base.sum_incoming,
            (date_trunc('day'::text, base.posting_dt) - ((EXTRACT(dow FROM base.posting_dt)::integer - 2 + 7) % 7)::double precision * '1 day'::interval)::date AS week_start
           FROM base
        )
 SELECT week_start,
    (week_start + '7 days'::interval)::date AS week_end,
    department,
    product_num,
    product_name,
    product_category,
    product_measure_unit,
    transaction_type,
    posting_dt,
    contr_account_name,
    COALESCE(amount_in, 0::numeric) - COALESCE(amount_out, 0::numeric) AS qty_signed,
        CASE
            WHEN COALESCE(sum_outgoing, 0::numeric) > 0::numeric THEN - COALESCE(sum_outgoing, 0::numeric)
            WHEN COALESCE(sum_incoming, 0::numeric) > 0::numeric THEN COALESCE(sum_incoming, 0::numeric)
            ELSE 0::numeric
        END AS money_signed
   FROM wk
;

CREATE VIEW inventory_core.weekly_wrong_receipt_mirror_products AS
 WITH mirror_pairs AS (
         SELECT a.week_start,
            a.week_end,
            a.product_num,
            a.department AS department_a,
            b.department AS department_b
           FROM inventory_core.inventory_correction_clean_products a
             JOIN inventory_core.inventory_correction_clean_products b ON a.week_start = b.week_start AND a.week_end = b.week_end AND a.product_num = b.product_num AND a.department < b.department
          WHERE sign(COALESCE(a.deviation_qty_signed, 0::numeric)) = (- sign(COALESCE(b.deviation_qty_signed, 0::numeric))) AND sign(COALESCE(a.deviation_qty_signed, 0::numeric)) <> 0::numeric AND abs(COALESCE(a.deviation_qty_signed, 0::numeric)) >= 0.001 AND abs(COALESCE(b.deviation_qty_signed, 0::numeric)) >= 0.001 AND (abs(abs(COALESCE(a.deviation_qty_signed, 0::numeric)) - abs(COALESCE(b.deviation_qty_signed, 0::numeric))) / NULLIF(GREATEST(abs(COALESCE(a.deviation_qty_signed, 0::numeric)), abs(COALESCE(b.deviation_qty_signed, 0::numeric))), 0::numeric)) <= 0.20
        ), marked AS (
         SELECT mirror_pairs.week_start,
            mirror_pairs.week_end,
            mirror_pairs.department_a AS department,
            mirror_pairs.product_num
           FROM mirror_pairs
        UNION
         SELECT mirror_pairs.week_start,
            mirror_pairs.week_end,
            mirror_pairs.department_b,
            mirror_pairs.product_num
           FROM mirror_pairs
        )
 SELECT week_start,
    week_end,
    department,
    product_num,
    true AS is_wrong_receipt_mirror
   FROM marked
;

CREATE VIEW inventory_mart.sandbox_inventory_products AS
 SELECT COALESCE(m.week_start, q.week_start) AS week_start,
    COALESCE(m.week_end, q.week_end) AS week_end,
    COALESCE(m.department, q.department) AS department,
    COALESCE(m.product_num, q.product_num) AS product_num,
    COALESCE(m.product_name, q.product_name) AS product_name,
    COALESCE(m.product_category, q.product_category) AS product_category,
    COALESCE(m.product_measure_unit, q.product_measure_unit) AS product_measure_unit,
    m.movement_money,
    m.shortage_money,
    m.surplus_money,
    m.deviation_money_clean,
    m.norm_money,
    m.norm_note,
    m.allowed_loss_money,
    m.excess_loss_money,
    m.potential_loss_week,
    m.potential_loss_month,
    m.deviation_money_signed,
    m.movement_qty,
    m.is_wrong_prev_inventory,
    m.prev_deviation_qty_signed,
    m.is_missing_inventory_position,
    COALESCE(m.is_wrong_receipt_mirror, q.is_wrong_receipt_mirror) AS is_wrong_receipt_mirror,
    q.movement_qty AS qty_movement_qty,
    q.movement_money AS qty_movement_money,
    q.deviation_qty_signed,
    q.deviation_money_clean AS qty_deviation_money_clean,
    q.shortage_qty,
    q.shortage_money AS qty_shortage_money,
    q.surplus_qty,
    q.surplus_money AS qty_surplus_money,
    q.norm_pct,
    q.fact_deviation_pct_qty,
    q.fact_shortage_pct_qty,
    q.fact_surplus_pct_qty,
    q.excess_pct_qty,
    q.deviation_pct_of_movement_money
   FROM inventory_mart.weekly_deviation_products_money_v2 m
     FULL JOIN inventory_mart.weekly_deviation_products_qty q ON q.week_start = m.week_start AND q.week_end = m.week_end AND q.department = m.department AND q.product_num = m.product_num
;

CREATE VIEW inventory_mart.weekly_deviation_products_money_v2 AS
 WITH m AS (
         SELECT w.week_start,
            w.week_end,
            w.department,
            w.product_num,
            max(w.product_name) AS product_name,
            max(w.product_category) AS product_category,
            max(w.product_measure_unit) AS product_measure_unit,
            w.movement_qty,
            w.movement_money
           FROM inventory_core.weekly_movement_products w
          GROUP BY w.week_start, w.week_end, w.department, w.product_num, w.movement_qty, w.movement_money
        ), c AS (
         SELECT ic.week_start,
            ic.week_end,
            ic.department,
            ic.product_num,
            ic.deviation_money_clean,
            ic.deviation_money_signed,
            ic.shortage_money,
            ic.surplus_money
           FROM inventory_core.inventory_correction_clean_products ic
        ), n AS (
         SELECT pn.department,
            pn.product_num,
            pn.norm_pct,
            pn.norm_note,
            pn.product_name,
            pn.product_category,
            pn.product_measure_unit
           FROM inventory_core.product_norm_effective pn
        ), base AS (
         SELECT COALESCE(m.week_start, c.week_start) AS week_start,
            COALESCE(m.week_end, c.week_end) AS week_end,
            COALESCE(m.department, c.department) AS department,
            COALESCE(m.product_num, c.product_num) AS product_num,
            COALESCE(m.product_name, n.product_name) AS product_name,
            COALESCE(m.product_category, n.product_category) AS product_category,
            COALESCE(m.product_measure_unit, n.product_measure_unit) AS product_measure_unit,
            COALESCE(m.movement_money, 0::numeric) AS movement_money,
            COALESCE(c.shortage_money, 0::numeric) AS shortage_money,
            COALESCE(c.surplus_money, 0::numeric) AS surplus_money,
            COALESCE(c.deviation_money_clean, 0::numeric) AS deviation_money_clean,
            COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02) AS norm_money,
            n.norm_note,
            COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02) AS allowed_loss_money,
                CASE
                    WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
                    ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02))
                END AS excess_loss_money,
                CASE
                    WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
                    ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02))
                END AS potential_loss_week,
                CASE
                    WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
                    ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02)) * 4::numeric
                END AS potential_loss_month,
            COALESCE(c.deviation_money_signed, 0::numeric) AS deviation_money_signed,
            COALESCE(m.movement_qty, 0::numeric) AS movement_qty
           FROM m
             FULL JOIN c ON m.week_start = c.week_start AND m.week_end = c.week_end AND m.department = c.department AND m.product_num = c.product_num
             LEFT JOIN n ON n.department = COALESCE(m.department, c.department) AND n.product_num = COALESCE(m.product_num, c.product_num)
        ), qc_prev AS (
         SELECT q_1.week_start,
            q_1.week_end,
            q_1.department,
            q_1.product_num,
            q_1.is_wrong_prev_inventory,
            q_1.prev_deviation_qty_signed
           FROM inventory_core.weekly_prev_miscount_last_week_products q_1
        )
 SELECT b.week_start,
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
    COALESCE(q.is_wrong_prev_inventory, false) AS is_wrong_prev_inventory,
    COALESCE(q.prev_deviation_qty_signed, 0::numeric) AS prev_deviation_qty_signed,
    COALESCE(mi.is_missing_inventory_position, false) AS is_missing_inventory_position,
    COALESCE(wr.is_wrong_receipt_mirror, false) AS is_wrong_receipt_mirror
   FROM base b
     LEFT JOIN qc_prev q ON q.week_start = b.week_start AND q.week_end = b.week_end AND q.department = b.department AND q.product_num = b.product_num
     LEFT JOIN inventory_core.weekly_missing_inventory_positions_products mi ON mi.week_start = b.week_start AND mi.week_end = b.week_end AND mi.department = b.department AND mi.product_num = b.product_num
     LEFT JOIN inventory_core.weekly_wrong_receipt_mirror_products wr ON wr.week_start = b.week_start AND wr.week_end = b.week_end AND wr.department = b.department AND wr.product_num = b.product_num
;

CREATE VIEW inventory_mart.weekly_deviation_products_qty AS
 WITH m AS (
         SELECT weekly_movement_products.week_start,
            weekly_movement_products.week_end,
            weekly_movement_products.department,
            weekly_movement_products.product_num,
            max(weekly_movement_products.product_name) AS product_name,
            max(weekly_movement_products.product_category) AS product_category,
            max(weekly_movement_products.product_measure_unit) AS product_measure_unit,
            weekly_movement_products.movement_qty,
            weekly_movement_products.movement_money
           FROM inventory_core.weekly_movement_products
          GROUP BY weekly_movement_products.week_start, weekly_movement_products.week_end, weekly_movement_products.department, weekly_movement_products.product_num, weekly_movement_products.movement_qty, weekly_movement_products.movement_money
        ), c AS (
         SELECT inventory_correction_clean_products.week_start,
            inventory_correction_clean_products.week_end,
            inventory_correction_clean_products.department,
            inventory_correction_clean_products.product_num,
            inventory_correction_clean_products.deviation_qty_signed,
            inventory_correction_clean_products.deviation_money_clean,
            inventory_correction_clean_products.shortage_qty,
            inventory_correction_clean_products.shortage_money,
            inventory_correction_clean_products.surplus_qty,
            inventory_correction_clean_products.surplus_money
           FROM inventory_core.inventory_correction_clean_products
        ), n AS (
         SELECT product_norm_effective.department,
            product_norm_effective.product_num,
            product_norm_effective.norm_pct,
            product_norm_effective.norm_note,
            product_norm_effective.product_name,
            product_norm_effective.product_category,
            product_norm_effective.product_measure_unit
           FROM inventory_core.product_norm_effective
        ), qc AS (
         SELECT weekly_prev_miscount_last_week_products.week_start,
            weekly_prev_miscount_last_week_products.department,
            weekly_prev_miscount_last_week_products.product_num,
            weekly_prev_miscount_last_week_products.prev_deviation_qty_signed,
            weekly_prev_miscount_last_week_products.is_wrong_prev_inventory
           FROM inventory_core.weekly_prev_miscount_last_week_products
        )
 SELECT COALESCE(m.week_start, c.week_start) AS week_start,
    COALESCE(m.week_end, c.week_end) AS week_end,
    COALESCE(m.department, c.department) AS department,
    COALESCE(m.product_num, c.product_num) AS product_num,
    COALESCE(m.product_name, n.product_name) AS product_name,
    COALESCE(m.product_category, n.product_category) AS product_category,
    COALESCE(m.product_measure_unit, n.product_measure_unit) AS product_measure_unit,
    COALESCE(m.movement_qty, 0::numeric) AS movement_qty,
    COALESCE(m.movement_money, 0::numeric) AS movement_money,
    COALESCE(c.deviation_qty_signed, 0::numeric) AS deviation_qty_signed,
    COALESCE(c.deviation_money_clean, 0::numeric) AS deviation_money_clean,
    COALESCE(c.shortage_qty, 0::numeric) AS shortage_qty,
    COALESCE(c.shortage_money, 0::numeric) AS shortage_money,
    COALESCE(c.surplus_qty, 0::numeric) AS surplus_qty,
    COALESCE(c.surplus_money, 0::numeric) AS surplus_money,
    COALESCE(n.norm_pct, 0.02) AS norm_pct,
    n.norm_note,
        CASE
            WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric THEN NULL::numeric
            ELSE COALESCE(c.deviation_qty_signed, 0::numeric) / NULLIF(m.movement_qty, 0::numeric)
        END AS fact_deviation_pct_qty,
        CASE
            WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric THEN NULL::numeric
            ELSE COALESCE(c.shortage_qty, 0::numeric) / NULLIF(m.movement_qty, 0::numeric)
        END AS fact_shortage_pct_qty,
        CASE
            WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric THEN NULL::numeric
            ELSE COALESCE(c.surplus_qty, 0::numeric) / NULLIF(m.movement_qty, 0::numeric)
        END AS fact_surplus_pct_qty,
        CASE
            WHEN COALESCE(m.movement_qty, 0::numeric) = 0::numeric THEN NULL::numeric
            ELSE GREATEST(0::numeric, abs(COALESCE(c.deviation_qty_signed, 0::numeric) / NULLIF(m.movement_qty, 0::numeric)) - COALESCE(n.norm_pct, 0.02))
        END AS excess_pct_qty,
        CASE
            WHEN COALESCE(m.movement_money, 0::numeric) = 0::numeric THEN NULL::numeric
            ELSE COALESCE(c.deviation_money_clean, 0::numeric) / NULLIF(m.movement_money, 0::numeric)
        END AS deviation_pct_of_movement_money,
    COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02) AS allowed_loss_money,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
            ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02))
        END AS excess_loss_money,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
            ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02))
        END AS potential_loss_week,
        CASE
            WHEN COALESCE(c.shortage_money, 0::numeric) <= 0::numeric THEN 0::numeric
            ELSE GREATEST(0::numeric, COALESCE(c.shortage_money, 0::numeric) - COALESCE(m.movement_money, 0::numeric) * COALESCE(n.norm_pct, 0.02)) * 4::numeric
        END AS potential_loss_month,
    COALESCE(qc.is_wrong_prev_inventory, false) AS is_wrong_prev_inventory,
    qc.prev_deviation_qty_signed,
    COALESCE(wr.is_wrong_receipt_mirror, false) AS is_wrong_receipt_mirror
   FROM m
     FULL JOIN c ON m.week_start = c.week_start AND m.week_end = c.week_end AND m.department = c.department AND m.product_num = c.product_num
     LEFT JOIN n ON n.department = COALESCE(m.department, c.department) AND n.product_num = COALESCE(m.product_num, c.product_num)
     LEFT JOIN qc ON qc.week_start = COALESCE(m.week_start, c.week_start) AND qc.department = COALESCE(m.department, c.department) AND qc.product_num = COALESCE(m.product_num, c.product_num)
     LEFT JOIN inventory_core.weekly_wrong_receipt_mirror_products wr ON wr.week_start = COALESCE(m.week_start, c.week_start) AND wr.week_end = COALESCE(m.week_end, c.week_end) AND wr.department = COALESCE(m.department, c.department) AND wr.product_num = COALESCE(m.product_num, c.product_num)
;

CREATE VIEW inventory_mart.weekly_product_documents_products AS
 WITH base AS (
         SELECT weekly_product_documents_products.week_start,
            weekly_product_documents_products.week_end,
            weekly_product_documents_products.department,
            weekly_product_documents_products.product_num,
            weekly_product_documents_products.product_name,
            weekly_product_documents_products.product_category,
            weekly_product_documents_products.product_measure_unit,
            weekly_product_documents_products.transaction_type,
            weekly_product_documents_products.posting_dt,
            weekly_product_documents_products.contr_account_name,
            weekly_product_documents_products.qty_signed,
            weekly_product_documents_products.money_signed
           FROM inventory_core.weekly_product_documents_products
        ), weeks AS (
         SELECT DISTINCT base.week_start,
            base.week_end
           FROM base
        ), labels AS (
         SELECT weeks.week_start,
            weeks.week_end,
            dense_rank() OVER (ORDER BY weeks.week_start) AS week_num,
            ((((('Н'::text || dense_rank() OVER (ORDER BY weeks.week_start)::text) || ' ('::text) || to_char(weeks.week_start::timestamp with time zone, 'DD.MM'::text)) || '–'::text) || to_char((weeks.week_end - 1)::timestamp with time zone, 'DD.MM'::text)) || ')'::text AS week_label
           FROM weeks
        )
 SELECT b.week_start,
    b.week_end,
    b.department,
    b.product_num,
    b.product_name,
    b.product_category,
    b.product_measure_unit,
    b.transaction_type,
    b.posting_dt,
    b.contr_account_name,
    b.qty_signed,
    b.money_signed,
    l.week_num,
    l.week_label
   FROM base b
     JOIN labels l ON l.week_start = b.week_start AND l.week_end = b.week_end
;
