# RT-STK-001. Call stack: распознавание и ошибки без символов

| Поле | Значение |
|---|---|
| ID | `RT-STK-001` |
| Область | STK — Call stack и символы |
| Приоритет | P1 |
| Тип | Функциональный |
| Требования | `REQ-STK-01`, `REQ-STK-02`, `REQ-STK-04` |
| Профиль | `Classic` |
| Конфигурация | `testdata/config/appsettings.regression.yaml` |
| Данные | `%RT%\testdata\callstack\callstack-synthetic.log` |
| Ориентировочная длительность | 5 мин |
| Дефекты (регрессия) | [#89](https://github.com/zhenyatnk/LogGrokX/issues/89) |
| Внешние ресурсы / ручные действия | Нет |

## Цель

Проверить распознавание стеков и ошибки.

## Проверяемые требования

- `REQ-STK-01` — Распознаются кадры `N# module!GUID,age!RVA`, `N# module!RVA`, `N# module+0xRVA`; у ячейки появляется SYM под BIN и пункт Resolve call stack.
- `REQ-STK-02` — До нажатия SYM ничего не загружается; во время загрузки кадр показывает `resolving symbols…`; результат `module!Function+0x.. [file @ line]`; состояние сохраняется при прокрутке.
- `REQ-STK-04` — Ошибки: `no symbols`, `symbol not found`, `Symbol path is not configured: …`; попытки поиска PDB в подсказке.

## Предусловия

- Выполнена базовая подготовка окружения из [README](../README.md#подготовка-окружения) (конфигурация, чистые данные пользователя).

## Шаги

| № | Действие | Ожидаемый результат |
|---|---|---|
| 1 | Settings → Symbols: Symbol path пустой, переменная `_NT_SYMBOL_PATH` не задана. Открыть `callstack\callstack-synthetic.log`. | `[K01]`, `[K03]`: в Message есть кнопка `SYM` (под BIN); у `[K02]` (`0# is just text`) — нет. |
| 2 | Контекстное меню `[K01]`: есть `Resolve call stack`, `Show call stack`. | Пункты присутствуют. |
| 3 | Нажать `SYM` у `[K01]`. | Кадры показывают `resolving symbols…`, затем сообщение `Symbol path is not configured: set it in Settings → Symbols or in _NT_SYMBOL_PATH` или `no symbols`/`symbol not found` для каждого кадра; аварии нет. |
| 4 | Задать Symbol path = `%TEMP%\lgx-empty-symbols` (пустая папка), Save; нажать SYM повторно. | `no symbols` для `test_.dll`, `missing_module.dll`; в подсказке функции — список попыток поиска PDB. |
| 5 | Прокрутить и вернуться. | Состояние SYM сохраняется. |

## Критерии прохождения

- Фактический результат каждого шага совпадает с ожидаемым.
- Нет аварийного завершения, зависаний UI > 5 с и необработанных исключений в логе `%LOCALAPPDATA%\LogGrokX\`.

## Артефакты для отчёта

- Скриншот после каждого шага с проверкой UI (`step-NN.png`).
- Содержимое буфера обмена/файлов для шагов с копированием и сохранением.

## Очистка

- Очистить Symbol path.
- Закрыть открытые документы.
- Восстановить исходную конфигурацию, если она менялась (см. README).