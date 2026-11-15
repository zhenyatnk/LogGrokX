# RT-SET-004. Автоисправление настроек

| Поле | Значение |
|---|---|
| ID | `RT-SET-004` |
| Область | SET — Настройки |
| Приоритет | P1 |
| Тип | Регрессия дефекта |
| Требования | `REQ-SET-04`, `REQ-BIG-03`, `REQ-DMP-01` |
| Профиль | `Classic` |
| Конфигурация | `testdata/config/appsettings.regression.yaml` |
| Данные | — |
| Ориентировочная длительность | 10 мин |
| Дефекты (регрессия) | [#91](https://github.com/zhenyatnk/LogGrokX/issues/91) |
| Внешние ресурсы / ручные действия | Нет |

## Цель

Регрессия #91: автоисправление проблем настроек.

## Проверяемые требования

- `REQ-SET-04` — Проблемы при Save перечисляются; доступно автоисправление `Fix all and save`, отдельное `Apply this fix`, переход к полю по ссылке; невалидный regex формата требует ручного исправления (#91).
- `REQ-BIG-03` — Валидация BigLineSize: `Big line size must be a positive integer.`; изменение применяется после сохранения.
- `REQ-DMP-01` — Debug → Enable crash dumps / Max dumps count (положительное целое) настраивает WER LocalDumps для LogGrokX.exe (через UAC), отключение удаляет ключ; требуется перезапуск.

## Предусловия

- Выполнена базовая подготовка окружения из [README](../README.md#подготовка-окружения) (конфигурация, чистые данные пользователя).

## Шаги

| № | Действие | Ожидаемый результат |
|---|---|---|
| 1 | Settings → View: Big line size = `0`; Debug: Max dumps count = `-1`; Profiles: переименовать `Venn` в `Classic` (дубликат); Colors: в профиле Classic задать цвет `NotAColor`. Нажать `Save`. | Сохранение не выполнено; панель `Errors can be fixed automatically` с перечнем: `Line length threshold will be reset to the default value (…).`, `Maximum number of dumps will be reset to the default value (…).`, `Duplicate profile "Classic" will be renamed to "…".`, `Profile "Classic": invalid color "NotAColor" will be reset to the default.` |
| 2 | Кликнуть ссылку проблемы Big line size. | Открыта вкладка View, фокус в поле Big line size. |
| 3 | Кликнуть одно предложенное исправление (`Apply this fix`) для Max dumps count. | Исправлено только оно, списки обновились, фокус на исправленном поле. |
| 4 | Нажать `Fix all and save`. | Все исправления применены и сохранены; YAML валиден. |
| 5 | В Log formats задать невалидный regex `^(?<Time>[unclosed`, Save. | `Errors require manual correction` / `A log format has an invalid regular expression.`; автоисправления нет, `Fix the errors first`. |
| 6 | Отменить изменения (Cancel). | Конфигурация не изменена. |

## Критерии прохождения

- Фактический результат каждого шага совпадает с ожидаемым.
- Нет аварийного завершения, зависаний UI > 5 с и необработанных исключений в логе `%LOCALAPPDATA%\LogGrokX\`.

## Артефакты для отчёта

- Скриншот после каждого шага с проверкой UI (`step-NN.png`).
- Содержимое буфера обмена/файлов для шагов с копированием и сохранением.

## Очистка

- Восстановить `appsettings.regression.yaml`.
- Закрыть открытые документы.
- Восстановить исходную конфигурацию, если она менялась (см. README).