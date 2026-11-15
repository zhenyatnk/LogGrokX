"""Данные регресс-кейсов LogGrokX. Каждый модуль области определяет REQS и CASES."""
from __future__ import annotations

from dataclasses import dataclass, field

PRIORITIES = ("P1", "P2", "P3")
TYPES = ("Функциональный", "Краевой", "Негативный", "Регрессия дефекта", "Нефункциональный")


@dataclass
class Case:
    id: str
    title: str
    priority: str
    type: str
    reqs: list[str]
    steps: list[tuple[str, str]]
    goal: str
    data: list[str] = field(default_factory=list)
    pre: list[str] = field(default_factory=list)
    profile: str = "Classic"
    config: str = "testdata/config/appsettings.regression.yaml"
    duration: str = "5 мин"
    issues: list[int] = field(default_factory=list)
    passcrit: list[str] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)
    artifacts: list[str] = field(default_factory=list)
    cleanup: list[str] = field(default_factory=list)
    manual: bool = False  # требует внешних ресурсов/прав (см. README, раздел BLOCKED)


def C(id, title, priority, type, reqs, steps, goal, **kw) -> Case:
    return Case(id, title, priority, type, list(reqs), list(steps), goal, **kw)
