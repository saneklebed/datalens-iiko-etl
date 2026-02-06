# .github/

Папка с конфигурацией GitHub Actions.

## Содержимое

### `workflows/etl.yml`
GitHub Actions workflow для автоматического запуска ETL-процесса.

**Триггер:** ручной запуск через `workflow_dispatch`

**Что делает:**
1. Устанавливает Python 3.11
2. Устанавливает зависимости из `requirements.txt`
3. Запускает `etl.py` с переменными окружения из GitHub Secrets

**Секреты (GitHub Secrets):**
- Все переменные окружения из основного README (NEON_*, IIKO_*, REPORT_ID, TRANSACTION_TYPES, PRODUCT_TYPES, RAW_DIR)

**Примечание:** Workflow настроен на ручной запуск, чтобы контролировать момент выполнения ETL.
