"""Тесты конфигурации моста (без обращения к API)."""
from pathlib import Path

import pytest

from edo_iiko_bridge.config import Config, DiadocConfig, IikoRestoConfig


def test_from_env_raises_when_diadoc_key_missing(monkeypatch):
    monkeypatch.setenv("DIADOC_API_KEY", "")
    monkeypatch.setenv("DIADOC_LOGIN", "u")
    monkeypatch.setenv("DIADOC_PASSWORD", "p")
    monkeypatch.setenv("IIKO_BASE_URL", "https://iiko.example")
    monkeypatch.setenv("IIKO_LOGIN", "i")
    monkeypatch.setenv("IIKO_PASS_SHA1", "abc")
    with pytest.raises(RuntimeError, match="DIADOC_API_KEY"):
        Config.from_env()


def test_from_env_raises_when_iiko_base_url_missing(monkeypatch):
    monkeypatch.setenv("DIADOC_API_KEY", "key")
    monkeypatch.setenv("DIADOC_LOGIN", "u")
    monkeypatch.setenv("DIADOC_PASSWORD", "p")
    monkeypatch.setenv("IIKO_BASE_URL", "")
    monkeypatch.setenv("IIKO_LOGIN", "i")
    monkeypatch.setenv("IIKO_PASS_SHA1", "abc")
    with pytest.raises(RuntimeError, match="IIKO_BASE_URL"):
        Config.from_env()


def test_from_env_builds_config_when_all_required_set(monkeypatch):
    monkeypatch.setenv("DIADOC_API_KEY", "my-key")
    monkeypatch.setenv("DIADOC_LOGIN", "user")
    monkeypatch.setenv("DIADOC_PASSWORD", "pass")
    monkeypatch.setenv("IIKO_BASE_URL", "https://iiko.example/")
    monkeypatch.setenv("IIKO_LOGIN", "iiko-user")
    monkeypatch.setenv("IIKO_PASS_SHA1", "deadbeef")
    cfg = Config.from_env()
    assert isinstance(cfg.diadoc, DiadocConfig)
    assert cfg.diadoc.api_key == "my-key"
    assert cfg.diadoc.login == "user"
    assert cfg.diadoc.password == "pass"
    assert isinstance(cfg.iiko, IikoRestoConfig)
    assert cfg.iiko.base_url == "https://iiko.example"
    assert cfg.iiko.login == "iiko-user"
    assert cfg.mapping_file == Path("./mapping.json")


def test_from_env_mapping_file_override(monkeypatch):
    monkeypatch.setenv("DIADOC_API_KEY", "k")
    monkeypatch.setenv("DIADOC_LOGIN", "u")
    monkeypatch.setenv("DIADOC_PASSWORD", "p")
    monkeypatch.setenv("IIKO_BASE_URL", "https://x")
    monkeypatch.setenv("IIKO_LOGIN", "i")
    monkeypatch.setenv("IIKO_PASS_SHA1", "s")
    monkeypatch.setenv("MAPPING_FILE", "/tmp/map.json")
    cfg = Config.from_env()
    assert cfg.mapping_file == Path("/tmp/map.json")


def test_from_env_verify_ssl_false(monkeypatch):
    monkeypatch.setenv("DIADOC_API_KEY", "k")
    monkeypatch.setenv("DIADOC_LOGIN", "u")
    monkeypatch.setenv("DIADOC_PASSWORD", "p")
    monkeypatch.setenv("IIKO_BASE_URL", "https://x")
    monkeypatch.setenv("IIKO_LOGIN", "i")
    monkeypatch.setenv("IIKO_PASS_SHA1", "s")
    monkeypatch.setenv("IIKO_VERIFY_SSL", "0")
    cfg = Config.from_env()
    assert cfg.iiko.verify_ssl is False
