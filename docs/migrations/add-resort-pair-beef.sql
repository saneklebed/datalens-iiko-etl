-- Добавить пару для пересорта: Говядина мякоть ↔ Говядина лопатка (для персонала).
-- Таблица: inventory_core.resort_product_pairs (product_num_1, product_num_2).
-- Формат данных: product_name слева, product_num справа (как даёт пользователь).
-- Выполнить в Neon один раз.

-- Говядина мякоть — 45700042794; Говядина лопатка (для персонала) — 2313231233122312313
-- Ограничение resort_pair_order: product_num_1 < product_num_2 как текст → '2313...' < '4570...'
INSERT INTO inventory_core.resort_product_pairs (product_num_1, product_num_2)
SELECT '2313231233122312313', '45700042794'
WHERE NOT EXISTS (
  SELECT 1 FROM inventory_core.resort_product_pairs p
  WHERE (p.product_num_1 = '2313231233122312313' AND p.product_num_2 = '45700042794')
     OR (p.product_num_1 = '45700042794' AND p.product_num_2 = '2313231233122312313')
);
