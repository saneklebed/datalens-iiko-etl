# Структура таблиц Neon (PostgreSQL)

Сгенерировано скриптом `scripts/dump_neon_schema.py`. Обновить: запустить скрипт снова (нужен .env с NEON_*).

---

## inventory_core

### deviation_norm_rules

| Колонка | Тип | NULL |
|---------|-----|------|
| rule_id | bigint | NO |
| department | text | YES |
| product_num | text | YES |
| product_category | text | YES |
| measure_unit | text | YES |
| turnover_group | text | YES |
| norm_pct | numeric | NO |
| priority | integer | NO |
| is_active | boolean | NO |
| note | text | YES |
| updated_at | timestamp with time zone | NO |


### inventory_correction_clean_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| deviation_qty_signed | numeric | YES |
| deviation_money_clean | numeric | YES |
| shortage_qty | numeric | YES |
| shortage_money | numeric | YES |
| surplus_qty | numeric | YES |
| surplus_money | numeric | YES |
| deviation_money_signed | numeric | YES |


### product_norm_effective

| Колонка | Тип | NULL |
|---------|-----|------|
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| norm_pct | numeric | YES |
| norm_note | text | YES |


### raw_inventory_missing_products

| Колонка | Тип | NULL |
|---------|-----|------|
| report_id | text | YES |
| department | text | YES |
| prev_inv_date | date | YES |
| last_inv_date | date | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| is_missing_in_last_inventory | boolean | YES |


### raw_inventory_products_count

| Колонка | Тип | NULL |
|---------|-----|------|
| report_id | text | YES |
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| inv_date | date | YES |
| products_cnt | bigint | YES |


### transactions

| Колонка | Тип | NULL |
|---------|-----|------|
| report_id | text | YES |
| date_from | date | YES |
| date_to | date | YES |
| department | text | YES |
| posting_dt | timestamp with time zone | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| transaction_type | text | YES |
| amount_out | numeric | YES |
| amount_in | numeric | YES |
| sum_outgoing | numeric | YES |
| sum_incoming | numeric | YES |
| source_hash | text | YES |
| loaded_at | timestamp with time zone | YES |
| is_movement | boolean | YES |
| is_inventory_correction | boolean | YES |
| contr_account_name | text | YES |


### transactions_products

| Колонка | Тип | NULL |
|---------|-----|------|
| report_id | text | YES |
| date_from | date | YES |
| date_to | date | YES |
| department | text | YES |
| posting_dt | timestamp with time zone | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| transaction_type | text | YES |
| amount_out | numeric | YES |
| amount_in | numeric | YES |
| sum_outgoing | numeric | YES |
| sum_incoming | numeric | YES |
| source_hash | text | YES |
| loaded_at | timestamp with time zone | YES |
| is_movement | boolean | YES |
| is_inventory_correction | boolean | YES |


### weekly_inventory_completeness_vs_dmd_last_week

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| expected_cnt | bigint | YES |
| actual_cnt | bigint | YES |
| completeness_pct | numeric | YES |


### weekly_inventory_correction

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| amount_out_sum | numeric | YES |
| amount_in_sum | numeric | YES |
| sum_outgoing_sum | numeric | YES |
| sum_incoming_sum | numeric | YES |


### weekly_inventory_correction_products_raw

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| amount_out_sum | numeric | YES |
| amount_in_sum | numeric | YES |
| sum_outgoing_sum | numeric | YES |
| sum_incoming_sum | numeric | YES |


### weekly_missing_inventory_positions_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| inv_date | date | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_qty | numeric | YES |
| is_missing_inventory_position | boolean | YES |


### weekly_missing_items_vs_dmd_last_week

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| is_missing_in_last_inventory | boolean | YES |


### weekly_movement

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_qty | numeric | YES |
| movement_money | numeric | YES |


### weekly_movement_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_qty | numeric | YES |
| movement_money | numeric | YES |


### weekly_prev_miscount_last_week_products

| Колонка | Тип | NULL |
|---------|-----|------|
| prev_week | date | YES |
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| prev_deviation_qty_signed | numeric | YES |
| deviation_qty_signed | numeric | YES |
| is_wrong_prev_inventory | boolean | YES |


### weekly_product_documents_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| transaction_type | text | YES |
| posting_dt | timestamp with time zone | YES |
| contr_account_name | text | YES |
| qty_signed | numeric | YES |
| money_signed | numeric | YES |


## inventory_mart

### sandbox_inventory_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_money | numeric | YES |
| shortage_money | numeric | YES |
| surplus_money | numeric | YES |
| deviation_money_clean | numeric | YES |
| norm_money | numeric | YES |
| norm_note | text | YES |
| allowed_loss_money | numeric | YES |
| excess_loss_money | numeric | YES |
| potential_loss_week | numeric | YES |
| potential_loss_month | numeric | YES |
| deviation_money_signed | numeric | YES |
| movement_qty | numeric | YES |
| is_wrong_prev_inventory | boolean | YES |
| prev_deviation_qty_signed | numeric | YES |
| is_missing_inventory_position | boolean | YES |
| qty_movement_qty | numeric | YES |
| qty_movement_money | numeric | YES |
| deviation_qty_signed | numeric | YES |
| qty_deviation_money_clean | numeric | YES |
| shortage_qty | numeric | YES |
| qty_shortage_money | numeric | YES |
| surplus_qty | numeric | YES |
| qty_surplus_money | numeric | YES |
| norm_pct | numeric | YES |
| fact_deviation_pct_qty | numeric | YES |
| fact_shortage_pct_qty | numeric | YES |
| fact_surplus_pct_qty | numeric | YES |
| excess_pct_qty | numeric | YES |
| deviation_pct_of_movement_money | numeric | YES |


### weekly_deviation_products_money_v2

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_money | numeric | YES |
| shortage_money | numeric | YES |
| surplus_money | numeric | YES |
| deviation_money_clean | numeric | YES |
| norm_money | numeric | YES |
| norm_note | text | YES |
| allowed_loss_money | numeric | YES |
| excess_loss_money | numeric | YES |
| potential_loss_week | numeric | YES |
| potential_loss_month | numeric | YES |
| deviation_money_signed | numeric | YES |
| movement_qty | numeric | YES |
| is_wrong_prev_inventory | boolean | YES |
| prev_deviation_qty_signed | numeric | YES |
| is_missing_inventory_position | boolean | YES |


### weekly_deviation_products_qty

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| movement_qty | numeric | YES |
| movement_money | numeric | YES |
| deviation_qty_signed | numeric | YES |
| deviation_money_clean | numeric | YES |
| shortage_qty | numeric | YES |
| shortage_money | numeric | YES |
| surplus_qty | numeric | YES |
| surplus_money | numeric | YES |
| norm_pct | numeric | YES |
| norm_note | text | YES |
| fact_deviation_pct_qty | numeric | YES |
| fact_shortage_pct_qty | numeric | YES |
| fact_surplus_pct_qty | numeric | YES |
| excess_pct_qty | numeric | YES |
| deviation_pct_of_movement_money | numeric | YES |
| allowed_loss_money | numeric | YES |
| excess_loss_money | numeric | YES |
| potential_loss_week | numeric | YES |
| potential_loss_month | numeric | YES |
| is_wrong_prev_inventory | boolean | YES |
| prev_deviation_qty_signed | numeric | YES |


### weekly_product_documents_products

| Колонка | Тип | NULL |
|---------|-----|------|
| week_start | date | YES |
| week_end | date | YES |
| department | text | YES |
| product_num | text | YES |
| product_name | text | YES |
| product_category | text | YES |
| product_measure_unit | text | YES |
| transaction_type | text | YES |
| posting_dt | timestamp with time zone | YES |
| contr_account_name | text | YES |
| qty_signed | numeric | YES |
| money_signed | numeric | YES |
| week_num | bigint | YES |
| week_label | text | YES |


## inventory_raw

### olap_postings

| Колонка | Тип | NULL |
|---------|-----|------|
| report_id | text | NO |
| date_from | date | NO |
| date_to | date | NO |
| department | text | NO |
| posting_dt | timestamp with time zone | NO |
| product_num | text | NO |
| product_name | text | YES |
| transaction_type | text | NO |
| amount_out | numeric | NO |
| sum_outgoing | numeric | NO |
| source_hash | text | NO |
| loaded_at | timestamp with time zone | NO |
| product_category | text | YES |
| amount_in | numeric | NO |
| sum_incoming | numeric | NO |
| product_measure_unit | text | YES |
| contr_account_name | text | YES |

