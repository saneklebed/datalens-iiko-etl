-- Добавить пары для пересорта: Лосось ↔ Лосось копченый; Пармезан крошка ↔ Пармезан брусок.
-- Таблица: inventory_core.resort_product_pairs (product_num_1, product_num_2).
-- product_num_1 < product_num_2 (лексикографически).
-- Выполнить в Neon один раз.

-- Лосось 0525 ↔ Лосось копченый 0535
INSERT INTO inventory_core.resort_product_pairs (product_num_1, product_num_2)
SELECT '0525', '0535'
WHERE NOT EXISTS (
  SELECT 1 FROM inventory_core.resort_product_pairs p
  WHERE (p.product_num_1 = '0525' AND p.product_num_2 = '0535')
     OR (p.product_num_1 = '0535' AND p.product_num_2 = '0525')
);

-- Пармезан крошка 0585 ↔ Пармезан брусок 45700042963
INSERT INTO inventory_core.resort_product_pairs (product_num_1, product_num_2)
SELECT '0585', '45700042963'
WHERE NOT EXISTS (
  SELECT 1 FROM inventory_core.resort_product_pairs p
  WHERE (p.product_num_1 = '0585' AND p.product_num_2 = '45700042963')
     OR (p.product_num_1 = '45700042963' AND p.product_num_2 = '0585')
);
