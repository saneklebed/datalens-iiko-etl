-- Таблица списаний в дашборде: показывать в т.ч. списания с типом «Порча».
-- Движение (оборот за неделю и т.п.) по-прежнему считается БЕЗ Порчи — фильтр остаётся в inventory_core.transactions.
-- Здесь меняем только inventory_core.weekly_product_documents_products: base читает из olap_postings (без фильтра по contr_account_name).
-- Выполнить в Neon один раз.

CREATE OR REPLACE VIEW inventory_core.weekly_product_documents_products AS
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
    FROM inventory_raw.olap_postings t
    WHERE t.product_category = 'Продукты'::text
      AND t.transaction_type = ANY (ARRAY['WRITEOFF'::text, 'PRODUCTION'::text, 'OUTGOING_INVOICE'::text, 'INVOICE'::text])
),
wk AS (
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
FROM wk;
