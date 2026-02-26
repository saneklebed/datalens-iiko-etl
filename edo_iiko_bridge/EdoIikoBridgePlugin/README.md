# Плагин «ЭДО ↔ iiko» для iikoChain Office

Минимальная оболочка: кнопка в меню дополнений iikoChain Office, по нажатию — сообщение. Дальше сюда можно подключать логику моста (Диадок, УПД, iiko Resto).

**Путь установки:** `C:\Program Files\iiko\iikoChain\Office\Plugins\EdoIikoBridge`

---

## Установка на любой ПК (без Visual Studio и SDK)

Так можно накатить плагин на другой компьютер — ничего не ставим, только копируем папку.

1. **Скачайте готовый плагин**  
   В репо: **Actions** → workflow **«Build iikoChain plugin (ЭДО ↔ iiko)»** → последний успешный запуск → внизу **Artifacts** → **EdoIikoBridge-Plugin** (скачивается zip).

2. **Распакуйте** архив. Внутри должны быть `EdoIikoBridge.Plugin.dll` и `Manifest.xml`.

3. **Создайте папку и скопируйте туда файлы:**
   ```
   C:\Program Files\iiko\iikoChain\Office\Plugins\EdoIikoBridge\
   ```
   Положите в неё оба файла из архива.

4. **Перезапустите iikoChain Office.**  
   В меню дополнений появится кнопка **«ЭДО ↔ iiko»**.

Сборка плагина идёт в GitHub Actions при пуше в `edo_iiko_bridge/EdoIikoBridgePlugin/` или по ручному запуску workflow. Артефакт хранится 90 дней.

---

## Сборка у себя (если нужно менять код)

Если ставите Visual Studio или .NET SDK:

1. Откройте `EdoIikoBridgePlugin.csproj` в Visual Studio (или: `dotnet build -c Release` из папки проекта).
2. Запустите `.\copy_to_chain.ps1` — файлы скопируются в `...\iikoChain\Office\Plugins\EdoIikoBridge`.
3. Перезапустите iikoChain Office.

---

## Зависимости при сборке

- .NET Framework 4.7.2 (целевой), NuGet **Resto.Front.Api.V8**.  
Контракт плагинов iikoChain Office совместим с Resto.Front.Api.
