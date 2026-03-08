# Поиск API сохранения прайс-листа поставщика iiko

## Цель
Найти метод для **добавления/обновления** строк прайс-листа поставщика в iiko (аналог GET `api/suppliers/{code}/pricelist`, но на запись).

## Что уже есть в проекте
- **Чтение:** GET `/resto/api/suppliers/{supplierIdOrCode}/pricelist` — возвращает XML с элементами `supplierPriceListItemDto` (nativeProduct, supplierProductCode/Num, container и т.д.).
- **Импорт накладной:** POST `api/documents/import/incomingInvoice` — успешно используется.

## Результат поиска (март 2025)
- В **открытой документации** (ru.iiko.help, api.iiko.ru, api-ru.iiko.services) описанного метода **записи** прайс-листа поставщика **не найдено**.
- Страницы справки (suppliers, iikoserver-api) подгружаются динамически («Loading…»), полный список эндпоинтов по запросу не получен.
- Поиск по названиям DTO (`supplierPriceListItemDto`, «pricelist POST», «добавить привязку») в открытых источниках и репозиториях не дал упоминаний метода сохранения.

## Рекомендации
1. **Уточнить у iiko:** написать в техподдержку (api@iiko.ru) или через личный кабинет: есть ли метод добавления/обновления прайс-листа поставщика (POST/PUT или import), URL и формат тела/ответа.
2. **Пока реализовать автопривязку без записи в справочник:** подбор товара и фасовки по номенклатуре iiko (GET entities/products/list), подстановка привязки только в текущий документ (в памяти и в XML накладной). В интерфейсе можно показывать подсказку: «Товар подобран; привязку в справочник iiko при необходимости добавьте вручную».

## Ссылки
- Документация поставщики: https://ru.iiko.help/articles/api-documentations/suppliers  
- iikoServer API: https://ru.iiko.help/articles/api-documentations/iikoserver-api  
- Техподдержка API: api@iiko.ru  
