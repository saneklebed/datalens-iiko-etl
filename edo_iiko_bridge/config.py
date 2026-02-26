"""Загрузка настроек из переменных окружения."""
import os
from dataclasses import dataclass
from pathlib import Path

from dotenv import load_dotenv

load_dotenv()


@dataclass
class DiadocConfig:
    api_key: str
    login: str
    password: str


@dataclass
class IikoRestoConfig:
    """iiko Server: базовый URL (без /resto), логин и SHA1-хэш пароля — те же переменные, что в ETL."""
    base_url: str
    login: str
    password_sha1: str
    verify_ssl: bool = True


@dataclass
class Config:
    diadoc: DiadocConfig
    iiko: IikoRestoConfig
    mapping_file: Path

    @classmethod
    def from_env(cls) -> "Config":
        def req(name: str) -> str:
            v = os.getenv(name)
            if not v or not str(v).strip():
                raise RuntimeError(f"Не задана переменная окружения: {name}")
            return str(v).strip()

        def opt(name: str, default: str) -> str:
            return str(os.getenv(name) or default).strip()

        return cls(
            diadoc=DiadocConfig(
                api_key=req("DIADOC_API_KEY"),
                login=req("DIADOC_LOGIN"),
                password=req("DIADOC_PASSWORD"),
            ),
            iiko=IikoRestoConfig(
                base_url=req("IIKO_BASE_URL").rstrip("/"),
                login=req("IIKO_LOGIN"),
                password_sha1=req("IIKO_PASS_SHA1"),
                verify_ssl=os.getenv("IIKO_VERIFY_SSL", "1").strip() not in ("0", "false", "False"),
            ),
            mapping_file=Path(opt("MAPPING_FILE", "./mapping.json")),
        )
