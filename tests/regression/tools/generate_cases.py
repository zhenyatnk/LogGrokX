#!/usr/bin/env python3
"""Генерирует tests/regression/cases/RT-*.md и tests/regression/COVERAGE.md из tools/casedata.

Запуск: python tests/regression/tools/generate_cases.py [--check]
--check — только проверить покрытие и согласованность без записи файлов.
"""
from __future__ import annotations

import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))

from casedata import PRIORITIES, TYPES  # noqa: E402
from casedata import a_launch_docs, b_formats_filters, c_search, d_payloads, e_view, f_app  # noqa: E402

MODULES = [a_launch_docs, b_formats_filters, c_search, d_payloads, e_view, f_app]

AREAS = {
    "LCH": "Запуск и единственный экземпляр",
    "DOC": "Открытие и управление документами",
    "ENC": "Кодировки и окончания строк",
    "LRG": "Большие и изменяющиеся файлы",
    "FMT": "Форматы логов и автоопределение",
    "IDX": "Колонки и индексация полей",
    "FLT": "Фильтрация",
    "TML": "Фильтр времени и таймлайн",
    "SRCH": "Поиск",
    "SAV": "Сохранённые поиски",
    "ALT": "Alt+клик: построение поиска",
    "NAV": "Навигация",
    "ZM": "Масштаб текста",
    "MRK": "Помеченные строки",
    "JSN": "JSON/XML: форматирование и сворачивание",
    "B64": "Base64, PEM, сертификаты и ключи",
    "HEX": "HEX в текст",
    "TRN": "Трансформации",
    "XOR": "XOR-маскированные логи",
    "BIG": "Большие строки",
    "CLR": "Правила цвета",
    "THR": "Группировка по потокам",
    "STK": "Call stack и символы",
    "VM": "Маппинг значений",
    "GRD": "Сетка: копирование и массовые операции",
    "SET": "Настройки",
    "LNG": "Языки интерфейса",
    "PRF": "Профили конфигурации",
    "THM": "Темы и акцентный цвет",
    "DMP": "Аварийные дампы",
    "SUP": "Окно поддержки",
    "MRG": "Объединённый просмотр файлов",
    "UPD": "Автоматические обновления",
    "INS": "Установщик",
}

# Все закрытые issue репозитория zhenyatnk/LogGrokX, которые должны иметь регресс-кейс.
CLOSED_ISSUES = {
    21: "Точка mark выравнивается по центру по вертикали",
    22: "Горизонтальное скроллирование вторым колесом мыши",
    23: "При наведении на timeline показывать время",
    27: "Предложение обновиться при открытии, если есть новые версии",
    29: "Проблемы с проверкой обновления через прокси",
    31: "Перенос строки с табуляцией в сообщении воспринимается как неформатированный",
    33: "При копировании строка обрезается тремя точками",
    35: "Форматирование base64 строки",
    36: "Проверять обновление при первом запуске в новом дне",
    38: "Раскрытие JSON «тормозит»",
    40: "Частые ошибки проверки обновления 403 (rate limit exceeded)",
    43: "Нельзя выделить несколько строк в поисковой выдаче",
    48: "Форматирование HEX строки",
    49: "Распознавание XML и форматирование как JSON",
    53: "Отключение детектирования BIN данных в настройках",
    56: "Изменение отображения режима подсветки потоков",
    58: "Маппинг значений",
    65: "Ctrl+F не работает сразу после старта",
    67: "Размер полосы прокрутки по вертикали меняется при прокрутке",
    73: "Утечка памяти при закрытии загружающегося лога",
    75: "BIN не раскрывает сертификат, если HEX->PEM",
    77: "hex(base64)->pem в JSON раскрывать по полям",
    78: "Состояние BIN теряется после прокрутки",
    89: "Отображение имён функций для call stack",
    91: "Автоисправление проблем appsettings при сохранении",
    95: "Дефекты окна сохранённых поисков",
    97: "Добавление слова в строку поиска по клику",
    99: "Отображение в заголовке информации о новой версии",
    103: "Alt+клик не захватывает Windows-путь целиком",
    104: "Alt+клик по внешней квадратной скобке не создаёт поиск",
    106: "DetectHex=false из YAML не применяется к открытому hex.log",
    107: "UTF-16LE без BOM распознаётся как однобайтовый текст",
    108: "Японский текст отображается квадратами",
    109: "TimeField не учитывает регистр имени поля",
    110: "Decode HEX изменяет имя свойства JSON",
    111: "JSON открывается развёрнутым вместо исходного свёрнутого состояния",
    112: "Открытие файла 1 ГБ потребляет почти столько же памяти, сколько занимает файл",
    113: "Переключение на Venn останавливает загрузку venn.log",
    114: "Неприменимый профиль применяется как plain text без диалога и без сохранения Classic",
    115: "Hot reload не применяет ручные изменения appsettings.yaml",
    116: "Запуск с некорректным YAML завершается необработанным InvalidDataException",
    117: "Regex с якорем `$` не находит строки с CRLF",
    118: "Начальный поиск в Group by thread ограничен первой строкой",
    119: "Preview применяет только выбранный список маппинга",
    120: "Режим Wrap не разбивает длинные строки на визуальные строки",
    121: "Правило цвета в конце списка не применяется к открытому документу",
    122: "Файл нулевой длины отображается как одна пустая запись и строка таймлайна",
    123: "Copy Full Path возвращает относительный путь из командной строки",
    124: "В заголовке отсутствует кнопка Toggle light / dark theme (as designed)",
    125: "Пустой элемент value mapping не вызывает ошибку валидации",
    126: "Открытый лог блокируется для переименования и удаления (as designed)",
    131: "Аварийное завершение при выходе на Windows 10 (0xC0000602)",
    135: "Скрытие столбцов",
    154: "Сломался поиск (в 1.7.9): фильтр по потоку скрывал результаты",
}

ISSUE_URL = "https://github.com/zhenyatnk/LogGrokX/issues/{}"
ID_RE = re.compile(r"^RT-([A-Z0-9]+)-(\d{3})$")
DEFAULT_PRE = ["Выполнена базовая подготовка окружения из [README](../README.md#подготовка-окружения) (конфигурация, чистые данные пользователя)."]
DEFAULT_PASS = [
    "Фактический результат каждого шага совпадает с ожидаемым.",
    "Нет аварийного завершения, зависаний UI > 5 с и необработанных исключений в логе `%LOCALAPPDATA%\\LogGrokX\\`.",
]
DEFAULT_ART = ["Скриншот после каждого шага с проверкой UI (`step-NN.png`).", "Содержимое буфера обмена/файлов для шагов с копированием и сохранением."]
DEFAULT_CLEANUP = ["Закрыть открытые документы.", "Восстановить исходную конфигурацию, если она менялась (см. README)."]


def load():
    reqs, cases = {}, []
    for m in MODULES:
        for k, v in m.REQS.items():
            if k in reqs:
                raise SystemExit(f"Дублирующееся требование {k}")
            reqs[k] = v
        cases.extend(m.CASES)
    return reqs, cases


def validate(reqs, cases):
    errors = []
    ids = Counter(c.id for c in cases)
    errors += [f"Дублирующийся ID {i}" for i, n in ids.items() if n > 1]
    covered = defaultdict(list)
    issues = defaultdict(list)
    for c in cases:
        m = ID_RE.match(c.id)
        if not m or m.group(1) not in AREAS:
            errors.append(f"{c.id}: неверный формат ID или неизвестная область")
        if c.priority not in PRIORITIES:
            errors.append(f"{c.id}: приоритет {c.priority}")
        if c.type not in TYPES:
            errors.append(f"{c.id}: тип {c.type}")
        if not c.steps:
            errors.append(f"{c.id}: нет шагов")
        for a, e in c.steps:
            if not a.strip() or not e.strip():
                errors.append(f"{c.id}: пустое действие/ожидание")
        if not c.reqs:
            errors.append(f"{c.id}: нет требований")
        for r in c.reqs:
            if r not in reqs:
                errors.append(f"{c.id}: неизвестное требование {r}")
            covered[r].append(c.id)
        for i in c.issues:
            if i not in CLOSED_ISSUES:
                errors.append(f"{c.id}: issue #{i} нет в списке закрытых")
            issues[i].append(c.id)
        if c.type == "Регрессия дефекта" and not c.issues:
            errors.append(f"{c.id}: регрессия дефекта без ссылки на issue")
    errors += [f"Требование {r} не покрыто" for r in reqs if r not in covered]
    errors += [f"Issue #{i} не покрыт" for i in CLOSED_ISSUES if i not in issues]
    # Каждая область должна иметь хотя бы одно требование и один кейс.
    areas_req = {r.split("-")[1] for r in reqs}
    areas_case = {ID_RE.match(c.id).group(1) for c in cases if ID_RE.match(c.id)}
    for a in AREAS:
        if a not in areas_case:
            errors.append(f"Область {a} без кейсов")
    for a in areas_req - set(AREAS):
        errors.append(f"Требование области {a} без описания области")
    return errors, covered, issues


def esc(s: str) -> str:
    return s.replace("|", "\\|").replace("\n", "<br>")


def bullets(items):
    return "\n".join(f"- {i}" for i in items)


def render_case(c, reqs) -> str:
    area = ID_RE.match(c.id).group(1)
    issues = ", ".join(f"[#{i}]({ISSUE_URL.format(i)})" for i in c.issues) or "—"
    data = "<br>".join(f"`{d}`" for d in c.data) or "—"
    out = [f"# {c.id}. {c.title}\n"]
    out.append("| Поле | Значение |\n|---|---|")
    rows = [
        ("ID", f"`{c.id}`"),
        ("Область", f"{area} — {AREAS[area]}"),
        ("Приоритет", c.priority),
        ("Тип", c.type),
        ("Требования", ", ".join(f"`{r}`" for r in c.reqs)),
        ("Профиль", f"`{c.profile}`"),
        ("Конфигурация", f"`{c.config}`"),
        ("Данные", data),
        ("Ориентировочная длительность", c.duration),
        ("Дефекты (регрессия)", issues),
        ("Внешние ресурсы / ручные действия", "Да — при недоступности ресурса статус `BLOCKED`" if c.manual else "Нет"),
    ]
    out += [f"| {k} | {esc(v)} |" for k, v in rows]
    out.append("\n## Цель\n")
    out.append(c.goal)
    out.append("\n## Проверяемые требования\n")
    out.append(bullets([f"`{r}` — {reqs[r]}" for r in c.reqs]))
    out.append("\n## Предусловия\n")
    out.append(bullets(DEFAULT_PRE + list(c.pre) + ([f"Активный профиль: `{c.profile}`."] if c.profile != "Classic" else [])))
    out.append("\n## Шаги\n")
    out.append("| № | Действие | Ожидаемый результат |\n|---|---|---|")
    out += [f"| {n} | {esc(a)} | {esc(e)} |" for n, (a, e) in enumerate(c.steps, 1)]
    out.append("\n## Критерии прохождения\n")
    out.append(bullets(list(c.passcrit) + DEFAULT_PASS))
    if c.notes:
        out.append("\n## Краевые случаи и примечания\n")
        out.append(bullets(c.notes))
    out.append("\n## Артефакты для отчёта\n")
    out.append(bullets([f"`{a}`" for a in c.artifacts] + DEFAULT_ART if c.artifacts else DEFAULT_ART))
    out.append("\n## Очистка\n")
    out.append(bullets(list(c.cleanup) + DEFAULT_CLEANUP))
    return "\n".join(out)


def render_coverage(reqs, cases, covered, issues) -> str:
    by_id = {c.id: c for c in cases}
    link = lambda i: f"[{i}](cases/{i}.md)"  # noqa: E731
    out = ["# Матрица покрытия регресс-набора LogGrokX\n",
           "Файл сгенерирован `tools/generate_cases.py`; не редактировать вручную.\n"]
    pr = Counter(c.priority for c in cases)
    ty = Counter(c.type for c in cases)
    out.append("## Сводка\n")
    out.append(f"- Тест-кейсов: **{len(cases)}** (P1: {pr['P1']}, P2: {pr['P2']}, P3: {pr['P3']}).")
    out.append(f"- Требований: **{len(reqs)}**, покрыто: **{len(covered)}** ({100 * len(covered) // len(reqs)}%).")
    out.append(f"- Закрытых дефектов с регресс-кейсом: **{len(issues)} из {len(CLOSED_ISSUES)}**.")
    out.append(f"- Кейсов с внешними ресурсами/ручными действиями: **{sum(c.manual for c in cases)}**.")
    out.append("- По типам: " + ", ".join(f"{t} — {ty[t]}" for t in TYPES) + ".\n")
    out.append("## Области\n")
    out.append("| Область | Описание | Требований | Кейсов | Кейсы |\n|---|---|---|---|---|")
    for a, title in AREAS.items():
        ac = [c.id for c in cases if c.id.startswith(f"RT-{a}-")]
        ar = [r for r in reqs if r.split("-")[1] == a]
        out.append(f"| {a} | {title} | {len(ar)} | {len(ac)} | {', '.join(link(i) for i in ac)} |")
    out.append("\n## Требования → кейсы\n")
    out.append("| Требование | Описание | Кейсы |\n|---|---|---|")
    for r, d in reqs.items():
        out.append(f"| `{r}` | {esc(d)} | {', '.join(link(i) for i in covered[r])} |")
    out.append("\n## Дефекты → кейсы\n")
    out.append("| Issue | Название | Кейсы |\n|---|---|---|")
    for i, t in sorted(CLOSED_ISSUES.items()):
        out.append(f"| [#{i}]({ISSUE_URL.format(i)}) | {esc(t)} | {', '.join(link(x) for x in issues[i])} |")
    out.append("\n## Список кейсов\n")
    out.append("| ID | Название | Приоритет | Тип |\n|---|---|---|---|")
    for c in sorted(cases, key=lambda c: c.id):
        out.append(f"| {link(c.id)} | {esc(c.title)} | {c.priority} | {c.type} |")
    _ = by_id
    return "\n".join(out) + "\n"


def main():
    reqs, cases = load()
    errors, covered, issues = validate(reqs, cases)
    if errors:
        print("\n".join(errors))
        raise SystemExit(1)
    if "--check" in sys.argv:
        print(f"OK: {len(cases)} кейсов, {len(reqs)} требований")
        return
    cases_dir = ROOT / "cases"
    cases_dir.mkdir(exist_ok=True)
    for old in cases_dir.glob("RT-*.md"):
        old.unlink()
    for c in cases:
        (cases_dir / f"{c.id}.md").write_text(render_case(c, reqs), encoding="utf-8", newline="\n")
    (ROOT / "COVERAGE.md").write_text(render_coverage(reqs, cases, covered, issues), encoding="utf-8", newline="\n")
    print(f"Сгенерировано {len(cases)} кейсов, {len(reqs)} требований, issues {len(issues)}")


if __name__ == "__main__":
    main()
