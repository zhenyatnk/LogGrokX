# RT-SUP-001. Окно Support

| Поле | Значение |
|---|---|
| ID | `RT-SUP-001` |
| Область | SUP — Окно поддержки |
| Приоритет | P2 |
| Тип | Функциональный |
| Требования | `REQ-SUP-01` |
| Профиль | `Classic` |
| Конфигурация | `testdata/config/appsettings.regression.yaml` |
| Данные | — |
| Ориентировочная длительность | 5 мин |
| Дефекты (регрессия) | — |
| Внешние ресурсы / ручные действия | Нет |

## Цель

Проверить окно поддержки.

## Проверяемые требования

- `REQ-SUP-01` — Окно Support (`?`): Version, Commit, Branch, Runtime, OS, Architecture; Check for updates, Copy diagnostics, Open logs folder, Download the latest release, Report an issue, Source code on GitHub.

## Предусловия

- Выполнена базовая подготовка окружения из [README](../README.md#подготовка-окружения) (конфигурация, чистые данные пользователя).
- Нужен доступ к github.com для ссылок и проверки обновлений (иначе соответствующие шаги BLOCKED).

## Шаги

| № | Действие | Ожидаемый результат |
|---|---|---|
| 1 | Нажать `?` в заголовке. | Окно `Support`: Version (= `LogGrokX.exe --version`), Commit, Branch, Runtime (.NET), OS, Architecture (x64/x86). |
| 2 | `Copy diagnostics`, `Get-Clipboard`. | Текст содержит все поля диагностики. |
| 3 | `Open logs folder`. | Открыт Проводник `%LOCALAPPDATA%\LogGrokX\`. |
| 4 | `Download the latest release`, `Report an issue`, `Source code on GitHub`. | Открываются в браузере: `…/releases/latest`, `…/issues`, репозиторий `zhenyatnk/LogGrokX`. |
| 5 | `Check for updates` (UpdateMode disabled). | Ручная проверка работает: `Checking for updates...`, затем `You have the latest version.` или `Version {0} is available.`/диалог обновления. |

## Критерии прохождения

- Фактический результат каждого шага совпадает с ожидаемым.
- Нет аварийного завершения, зависаний UI > 5 с и необработанных исключений в логе `%LOCALAPPDATA%\LogGrokX\`.

## Артефакты для отчёта

- Скриншот после каждого шага с проверкой UI (`step-NN.png`).
- Содержимое буфера обмена/файлов для шагов с копированием и сохранением.

## Очистка

- Закрыть открытые документы.
- Восстановить исходную конфигурацию, если она менялась (см. README).