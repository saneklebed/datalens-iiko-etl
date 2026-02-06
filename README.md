# datalens-iiko-etl

Проект построения управленческого дашборда по инвентаризациям для анализа недостач/излишков, превышений норм отклонений и ключевых источников потерь по неделям и филиалам.

> **Главный промпт для AI** — в `.cursor/rules/project.mdc`; актуализация README/readme по правилам оттуда.

## Цель проекта

Построить управленческий дашборд по инвентаризациям, который:

- Показывает топы расхождений в qty и в деньгах
- Отсеивает мусор (микродвижения, мармелад и т.п.)
- Корректно работает с филиалами
- Позволяет видеть потенциальные потери в месяц и анализировать подозрительные позиции (ошибка предыдущей инвентаризации)
- В будущем — динамика по товарам по неделям

Всё это: по неделям, по филиалам, без задвоений, с корректной логикой инвентаризаций.

## Архитектура

```
iiko API
   ↓
ETL (Python)
   ↓
Neon (Postgres)
   ↓
DataLens (дашборды)
```

## Структура проекта

```
datalens-iiko-etl/
├── .cursor/
│   └── rules/
│       └── project.mdc          # Главный промпт для AI (alwaysApply: true)
├── .github/
│   └── workflows/
│       └── etl.yml              # GitHub Actions workflow для автоматического запуска
├── etl.py                       # Основной ETL скрипт (загрузка из iiko в Neon)
├── requirements.txt             # Python зависимости
└── README.md                    # Этот файл
```

## Описание компонентов

### `etl.py`
Основной скрипт ETL-процесса:
- Загрузка конфигурации из переменных окружения
- Аутентификация в iiko API
- Запрос данных через OLAP API за период (вторник → понедельник предыдущей недели)
- Нормализация данных и вычисление `source_hash` для защиты от дублей
- Загрузка в PostgreSQL (Neon) в таблицу `inventory_raw.olap_postings`

### `.github/workflows/etl.yml`
GitHub Actions workflow для автоматического запуска:
- Ручной запуск через `workflow_dispatch`
- Установка Python 3.11 и зависимостей
- Выполнение `etl.py` с переменными окружения из GitHub Secrets

## Структура данных в Neon (Postgres)

### 1. Сырые данные

**`inventory_raw.olap_postings`**
- Все проводки iiko (приходы/расходы)
- Флаги: `is_movement`, `is_inventory_correction`
- Поля: `quantity`, `Sum.Outgoing`, `Sum.Incoming`, `posting_dt`, `product_num`, `product_name`, `department`, `product_measure_unit`
- Защита от дублей через `source_hash` (ON CONFLICT DO NOTHING)

### 2. Core-слой (ключевой)

**`inventory_core.transactions`**
- Нормализованная лента проводок
- SIGNED-логика:
  - недостача → отрицательные значения
  - излишек → положительные
- Поля: `qty_signed`, `money_signed`, `is_inventory_correction`, `is_movement`

**`inventory_core.transactions_products`**
- Фильтр только по продуктам
- Исключены служебные позиции

### 3. Агрегации (основа аналитики)

**`inventory_core.weekly_movement_products`**
- Движение по товарам (базовая витрина)
- Поля: `week_start`, `week_end`, `department`, `product_num`, `product_name`, `product_category`, `product_measure_unit`, `movement_qty`, `movement_money`

**`inventory_core.inventory_correction_clean_products`**
- ОЧИЩЕННАЯ инвентаризация (только итоговая, `posting_dt = понедельник 23:59:59`, промежуточные игнорируются)
- Поля: `deviation_qty_signed`, `deviation_money_signed`, `shortage_money`, `surplus_money`

### 4. Финальные витрины (MONEY приведена к QTY-логике)

**`weekly_deviation_products_money`** (денежная аналитика, актуальные поля)
- `week_start`, `week_end`, `department`, `product_num`, `product_name`, `product_category`, `product_measure_unit`
- `movement_money`, `movement_qty`, `shortage_money`, `surplus_money`
- `deviation_money_signed`, `deviation_money_clean`, `norm_money`, `norm_note`, `allowed_loss_money`, `excess_loss_money`
- `potential_loss_week`, `potential_loss_month`
- ⚠️ В Neon при изменении колонок: **DROP + CREATE** (переименование столбцов не поддерживается)

**`weekly_deviation_products_qty`**
- Количественная аналитика, логика аналогичная, работает корректно

## DataLens — структура

### Датасеты

**`DS_Deviation_MONEY`**
- Источник: `weekly_deviation_products_money`
- В датасете созданы **жёстко агрегированные меры**: `SUM([movement_money])`, `SUM([deviation_money_signed])`, `SUM([excess_loss_money])`, `SUM([potential_loss_month])`
- В чартах использовать **только эти _sum поля** — иначе без выбора филиала DataLens дробит строки по департаментам. С _sum поведение MONEY = QTY.
- Агрегацию фиксировать на уровне датасета, не чарта.

**`DS_Deviation_QTY`**
- Источник: `weekly_deviation_products_qty`, работает корректно

**`Suspect_previous_inventory_miscount`**
- Подозрительные позиции (ошибка прошлой инвентаризации)
- Поля: `product_name`, `product_num`, `product_measure_unit`, `department`, `deviation_qty_signed`, `prev_deviation_qty_signed`, `abs_ratio`, `sum_two_weeks_qty`, `is_suspect_prev_miscount` (boolean)
- Пока данных за 1 инвентаризацию — `prev_*` = NULL; блок на дашборде планировался, не добавлен

### Дашборд

**«Дашбордик для анализа инвентаризаций»**

- **Глобальные селекторы:** Неделя (week_start / week_end), Филиал (department), TOP N, Товар (для будущей динамики)
- **Таблица 1 — ТОП по превышению нормы (QTY):** источник `DS_Deviation_QTY`, фильтры движение > 1, превышение > 0 — работает корректно
- **Таблица 2 — ТОП по отклонениям ($):** источник `DS_Deviation_MONEY`, только _sum поля; сортировка по «Можем терять в месяц» (по убыванию) — **обычная таблица**, поле в строках + поле сортировки + дубликат столбца справа

### Связи и алиасы

- `DS_Deviation_MONEY.department = DS_Deviation_QTY.department` — филиал фильтрует обе таблицы

### Ключевые выводы DataLens

- **Сортировка:** только обычная таблица, не сводная; поле в строках + сортировка + дубликат столбца для отображения
- **MONEY и QTY симметричны** — если DataLens «дробит» строки, почти всегда проблема агрегации (фиксировать в датасете)

## Переменные окружения

**Neon (PostgreSQL):**
- `NEON_HOST`, `NEON_DB`, `NEON_USER`, `NEON_PASSWORD`

**iiko API:**
- `IIKO_BASE_URL`, `IIKO_LOGIN`, `IIKO_PASS_SHA1`, `IIKO_VERIFY_SSL`

**Отчет:**
- `REPORT_ID` — идентификатор отчета
- `TRANSACTION_TYPES` — типы транзакций (через запятую или точку с запятой)
- `PRODUCT_TYPES` — типы продуктов (через запятую или точку с запятой)

**Опционально:**
- `RAW_DIR` — директория для сырых данных (по умолчанию `src/data/raw`)
- **Выгрузка прошлых периодов:** `DATE_FROM` и `DATE_TO` в формате `YYYY-MM-DD`. Если заданы оба — используется этот период вместо авто-недели (вторник→понедельник). Конечная дата в iiko — **исключающая** (день `date_to` не включается). При запуске через **GitHub Actions** период задаётся полями ввода при ручном запуске (Run workflow → date_from, date_to); секреты для этого не нужны.

## Локальный запуск

1. Установить зависимости: `pip install -r requirements.txt`
2. Создать `.env` файл с переменными окружения (см. выше)
3. Запустить: `python etl.py`

## Особенности

- **Идемпотентность:** защита от дублей через `source_hash` (ON CONFLICT DO NOTHING)
- **Автоматический период:** вычисление периода "вторник → понедельник" предыдущей закрытой недели
- **Обработка таймзон:** автоматическое определение и нормализация времени (UTC для БД)
- **Фильтрация:** исключение строк "Итого"/"Всего" из данных
- **Логика инвентаризаций:** учитываются только итоговые инвентаризации (понедельник 23:59:59), промежуточные игнорируются

## Текущий статус

**Сделано:**
- ✅ Убраны задвоения по инвентаризациям, только итоговые инвентаризации
- ✅ Signed-логика, симметрия MONEY и QTY, агрегация MONEY в датасете (_sum)
- ✅ Фильтр филиала (alias), сортировка по «Можем терять в месяц» (обычная таблица + поле в строках + сортировка + дубликат столбца)
- ✅ Датасет подозрительных позиций (блок на дашборде не добавлен)

**Планировалось, не сделано:**
- График динамики по товару (по неделям)
- Блок анализа: «начали работать с товаром → пошли ли недостачи вниз»
- Фильтр: цена за единицу = movement_money / movement_qty (отсекать мусорные движения)
- Блок «Подозрительные позиции прошлой инвентаризации» на дашборде
