"""Заглушка клиента iiko Resto API (номенклатура/прайс, приходы). Реализация — далее по плану ЭДО ↔ iiko."""
from __future__ import annotations

from edo_iiko_bridge.config import IikoRestoConfig


class IikoRestoClient:
    """Клиент iiko Server REST (Suppliers_price и др.). Пока без реализации."""

    def __init__(self, config: IikoRestoConfig) -> None:
        self._config = config

    # TODO: методы для номенклатуры, прайса, создания прихода по накладной
    # См. docs/edo-iiko-edidoc-reverse-summary.md и docs/edo-iiko-integration-plan.md


__all__ = ["IikoRestoClient"]
