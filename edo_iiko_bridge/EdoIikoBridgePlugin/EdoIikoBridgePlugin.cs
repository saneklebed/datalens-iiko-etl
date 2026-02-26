using System;
using Resto.Front.Api.Attributes;

namespace EdoIikoBridge.Plugin
{
    /// <summary>
    /// Плагин-оболочка моста ЭДО ↔ iiko для iikoChain Office.
    /// Добавляет пункт «ЭДО ↔ iiko» в меню дополнений; по нажатию показывается сообщение.
    /// </summary>
    [PluginLicenseModuleId(21005108)]
    public sealed class EdoIikoBridgePlugin : IFrontPlugin
    {
        private IDisposable _menuButtonSubscription;

        public EdoIikoBridgePlugin()
        {
            PluginContext.Log.Info("EdoIikoBridgePlugin: инициализация");

            _menuButtonSubscription = PluginContext.Operations.AddButtonToPluginsMenu(
                "ЭДО ↔ iiko",
                (vm, printer) =>
                {
                    try
                    {
                        PluginContext.Operations.AddWarningMessage("Мост ЭДО ↔ iiko. Оболочка установлена — здесь будет работа с накладными из Диадока и сопоставление с iiko.");
                    }
                    catch (Exception ex)
                    {
                        PluginContext.Log.Error("EdoIikoBridgePlugin: ошибка при нажатии", ex);
                    }
                });

            PluginContext.Log.Info("EdoIikoBridgePlugin: запущен, кнопка в меню дополнений");
        }

        public void Dispose()
        {
            _menuButtonSubscription?.Dispose();
            _menuButtonSubscription = null;
            PluginContext.Log.Info("EdoIikoBridgePlugin: остановлен");
        }
    }
}
