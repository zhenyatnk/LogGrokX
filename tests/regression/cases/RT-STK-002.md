# RT-STK-002. Call stack: разрешение реальных символов Microsoft

| Поле | Значение |
|---|---|
| ID | `RT-STK-002` |
| Область | STK — Call stack и символы |
| Приоритет | P2 |
| Тип | Функциональный |
| Требования | `REQ-STK-02`, `REQ-STK-03`, `REQ-STK-05` |
| Профиль | `Classic` |
| Конфигурация | `testdata/config/appsettings.regression.yaml` |
| Данные | — |
| Ориентировочная длительность | 15 мин |
| Дефекты (регрессия) | [#89](https://github.com/zhenyatnk/LogGrokX/issues/89) |
| Внешние ресурсы / ручные действия | Да — при недоступности ресурса статус `BLOCKED` |

## Цель

Проверить разрешение символов через symbol server.

## Проверяемые требования

- `REQ-STK-02` — До нажатия SYM ничего не загружается; во время загрузки кадр показывает `resolving symbols…`; результат `module!Function+0x.. [file @ line]`; состояние сохраняется при прокрутке.
- `REQ-STK-03` — Окно Show call stack: кадры (модуль, функция, исходник), Copy call stack, Reload symbols, просмотр исходника с подсветкой строки, Open in editor; следует теме и акценту.
- `REQ-STK-05` — Settings → Symbols: Resolve call stacks in log messages, Symbol path (по строке на элемент), Source path, Symbol cache folder, Allow source server commands; применяется без перезапуска (#89).

## Предусловия

- Выполнена базовая подготовка окружения из [README](../README.md#подготовка-окружения) (конфигурация, чистые данные пользователя).
- Нужен доступ в интернет к msdl.microsoft.com; иначе кейс BLOCKED.

## Шаги

| № | Действие | Ожидаемый результат |
|---|---|---|
| 1 | Сгенерировать: `pwsh %RT%\tools\Get-CallStackSample.ps1 -Path $env:TEMP\lgx-callstack.log`. | Созданы лог и `lgx-callstack.log.expected.json`. |
| 2 | Settings → Symbols: Symbol path = `srv*%TEMP%\lgx-symcache*https://msdl.microsoft.com/download/symbols`, `Resolve call stacks in log messages` включено. Save (без перезапуска). | Сохранено. |
| 3 | Открыть `%TEMP%\lgx-callstack.log`; до нажатия SYM проверить папку кэша. | Папка кэша пуста — ничего не загружено до нажатия. |
| 4 | Нажать SYM. | Кадры вида `GUID,age` разрешены в `ntdll!<Function>+0x4` / `kernel32!<Function>+0x4` согласно `expected.json`; кадры без GUID — по правилам поиска (`no symbols`, если PDB нет в обычной папке). |
| 5 | Контекстное меню → `Show call stack`. | Окно `Call stack` с таблицей кадров (модуль, функция, исходник) в цвете акцента. |
| 6 | `Copy call stack`, `Get-Clipboard`. | Текст с разрешёнными кадрами. |
| 7 | `Reload symbols`. | Кэш результатов сброшен, символы ищутся заново, результат тот же. |
| 8 | Переключить тему Dark и акцент Green при открытом окне. | Окно меняет палитру сразу. |

## Критерии прохождения

- Фактический результат каждого шага совпадает с ожидаемым.
- Нет аварийного завершения, зависаний UI > 5 с и необработанных исключений в логе `%LOCALAPPDATA%\LogGrokX\`.

## Артефакты для отчёта

- Скриншот после каждого шага с проверкой UI (`step-NN.png`).
- Содержимое буфера обмена/файлов для шагов с копированием и сохранением.

## Очистка

- Удалить `%TEMP%\lgx-symcache`, очистить Symbol path.
- Закрыть открытые документы.
- Восстановить исходную конфигурацию, если она менялась (см. README).