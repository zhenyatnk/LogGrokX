# Матрица покрытия регресс-набора LogGrokX

Файл сгенерирован `tools/generate_cases.py`; не редактировать вручную.

## Сводка

- Тест-кейсов: **106** (P1: 61, P2: 39, P3: 6).
- Требований: **159**, покрыто: **159** (100%).
- Закрытых дефектов с регресс-кейсом: **28 из 28**.
- Кейсов с внешними ресурсами/ручными действиями: **7**.
- По типам: Функциональный — 58, Краевой — 15, Негативный — 8, Регрессия дефекта — 20, Нефункциональный — 5.

## Области

| Область | Описание | Требований | Кейсов | Кейсы |
|---|---|---|---|---|
| LCH | Запуск и единственный экземпляр | 8 | 7 | [RT-LCH-001](cases/RT-LCH-001.md), [RT-LCH-002](cases/RT-LCH-002.md), [RT-LCH-003](cases/RT-LCH-003.md), [RT-LCH-004](cases/RT-LCH-004.md), [RT-LCH-005](cases/RT-LCH-005.md), [RT-LCH-006](cases/RT-LCH-006.md), [RT-LCH-007](cases/RT-LCH-007.md) |
| DOC | Открытие и управление документами | 7 | 5 | [RT-DOC-001](cases/RT-DOC-001.md), [RT-DOC-002](cases/RT-DOC-002.md), [RT-DOC-003](cases/RT-DOC-003.md), [RT-DOC-004](cases/RT-DOC-004.md), [RT-DOC-005](cases/RT-DOC-005.md) |
| ENC | Кодировки и окончания строк | 4 | 3 | [RT-ENC-001](cases/RT-ENC-001.md), [RT-ENC-002](cases/RT-ENC-002.md), [RT-ENC-003](cases/RT-ENC-003.md) |
| LRG | Большие и изменяющиеся файлы | 6 | 5 | [RT-LRG-001](cases/RT-LRG-001.md), [RT-LRG-002](cases/RT-LRG-002.md), [RT-LRG-003](cases/RT-LRG-003.md), [RT-LRG-004](cases/RT-LRG-004.md), [RT-LRG-005](cases/RT-LRG-005.md) |
| FMT | Форматы логов и автоопределение | 8 | 7 | [RT-FMT-001](cases/RT-FMT-001.md), [RT-FMT-002](cases/RT-FMT-002.md), [RT-FMT-003](cases/RT-FMT-003.md), [RT-FMT-004](cases/RT-FMT-004.md), [RT-FMT-005](cases/RT-FMT-005.md), [RT-FMT-006](cases/RT-FMT-006.md), [RT-FMT-007](cases/RT-FMT-007.md) |
| IDX | Колонки и индексация полей | 3 | 1 | [RT-IDX-001](cases/RT-IDX-001.md) |
| FLT | Фильтрация | 7 | 3 | [RT-FLT-001](cases/RT-FLT-001.md), [RT-FLT-002](cases/RT-FLT-002.md), [RT-FLT-003](cases/RT-FLT-003.md) |
| TML | Фильтр времени и таймлайн | 7 | 5 | [RT-TML-001](cases/RT-TML-001.md), [RT-TML-002](cases/RT-TML-002.md), [RT-TML-003](cases/RT-TML-003.md), [RT-TML-004](cases/RT-TML-004.md), [RT-TML-005](cases/RT-TML-005.md) |
| SRCH | Поиск | 12 | 9 | [RT-SRCH-001](cases/RT-SRCH-001.md), [RT-SRCH-002](cases/RT-SRCH-002.md), [RT-SRCH-003](cases/RT-SRCH-003.md), [RT-SRCH-004](cases/RT-SRCH-004.md), [RT-SRCH-005](cases/RT-SRCH-005.md), [RT-SRCH-006](cases/RT-SRCH-006.md), [RT-SRCH-007](cases/RT-SRCH-007.md), [RT-SRCH-008](cases/RT-SRCH-008.md), [RT-SRCH-009](cases/RT-SRCH-009.md) |
| SAV | Сохранённые поиски | 5 | 3 | [RT-SAV-001](cases/RT-SAV-001.md), [RT-SAV-002](cases/RT-SAV-002.md), [RT-SAV-003](cases/RT-SAV-003.md) |
| ALT | Alt+клик: построение поиска | 5 | 3 | [RT-ALT-001](cases/RT-ALT-001.md), [RT-ALT-002](cases/RT-ALT-002.md), [RT-ALT-003](cases/RT-ALT-003.md) |
| NAV | Навигация | 4 | 3 | [RT-NAV-001](cases/RT-NAV-001.md), [RT-NAV-002](cases/RT-NAV-002.md), [RT-NAV-003](cases/RT-NAV-003.md) |
| ZM | Масштаб текста | 2 | 1 | [RT-ZM-001](cases/RT-ZM-001.md) |
| MRK | Помеченные строки | 4 | 2 | [RT-MRK-001](cases/RT-MRK-001.md), [RT-MRK-002](cases/RT-MRK-002.md) |
| JSN | JSON/XML: форматирование и сворачивание | 8 | 5 | [RT-JSN-001](cases/RT-JSN-001.md), [RT-JSN-002](cases/RT-JSN-002.md), [RT-JSN-003](cases/RT-JSN-003.md), [RT-JSN-004](cases/RT-JSN-004.md), [RT-JSN-005](cases/RT-JSN-005.md) |
| B64 | Base64, PEM, сертификаты и ключи | 10 | 8 | [RT-B64-001](cases/RT-B64-001.md), [RT-B64-002](cases/RT-B64-002.md), [RT-B64-003](cases/RT-B64-003.md), [RT-B64-004](cases/RT-B64-004.md), [RT-B64-005](cases/RT-B64-005.md), [RT-B64-006](cases/RT-B64-006.md), [RT-B64-007](cases/RT-B64-007.md), [RT-B64-008](cases/RT-B64-008.md) |
| HEX | HEX в текст | 4 | 2 | [RT-HEX-001](cases/RT-HEX-001.md), [RT-HEX-002](cases/RT-HEX-002.md) |
| TRN | Трансформации | 2 | 1 | [RT-TRN-001](cases/RT-TRN-001.md) |
| XOR | XOR-маскированные логи | 2 | 1 | [RT-XOR-001](cases/RT-XOR-001.md) |
| BIG | Большие строки | 3 | 2 | [RT-BIG-001](cases/RT-BIG-001.md), [RT-BIG-002](cases/RT-BIG-002.md) |
| CLR | Правила цвета | 3 | 2 | [RT-CLR-001](cases/RT-CLR-001.md), [RT-CLR-002](cases/RT-CLR-002.md) |
| THR | Группировка по потокам | 3 | 2 | [RT-THR-001](cases/RT-THR-001.md), [RT-THR-002](cases/RT-THR-002.md) |
| STK | Call stack и символы | 5 | 3 | [RT-STK-001](cases/RT-STK-001.md), [RT-STK-002](cases/RT-STK-002.md), [RT-STK-003](cases/RT-STK-003.md) |
| VM | Маппинг значений | 6 | 3 | [RT-VM-001](cases/RT-VM-001.md), [RT-VM-002](cases/RT-VM-002.md), [RT-VM-003](cases/RT-VM-003.md) |
| GRD | Сетка: копирование и массовые операции | 5 | 3 | [RT-GRD-001](cases/RT-GRD-001.md), [RT-GRD-002](cases/RT-GRD-002.md), [RT-GRD-003](cases/RT-GRD-003.md) |
| SET | Настройки | 5 | 4 | [RT-SET-001](cases/RT-SET-001.md), [RT-SET-002](cases/RT-SET-002.md), [RT-SET-003](cases/RT-SET-003.md), [RT-SET-004](cases/RT-SET-004.md) |
| LNG | Языки интерфейса | 2 | 1 | [RT-LNG-001](cases/RT-LNG-001.md) |
| PRF | Профили конфигурации | 5 | 3 | [RT-PRF-001](cases/RT-PRF-001.md), [RT-PRF-002](cases/RT-PRF-002.md), [RT-PRF-003](cases/RT-PRF-003.md) |
| THM | Темы и акцентный цвет | 2 | 1 | [RT-THM-001](cases/RT-THM-001.md) |
| DMP | Аварийные дампы | 1 | 1 | [RT-DMP-001](cases/RT-DMP-001.md) |
| SUP | Окно поддержки | 1 | 1 | [RT-SUP-001](cases/RT-SUP-001.md) |
| MRG | Объединённый просмотр файлов | 3 | 2 | [RT-MRG-001](cases/RT-MRG-001.md), [RT-MRG-002](cases/RT-MRG-002.md) |
| UPD | Автоматические обновления | 5 | 3 | [RT-UPD-001](cases/RT-UPD-001.md), [RT-UPD-002](cases/RT-UPD-002.md), [RT-UPD-003](cases/RT-UPD-003.md) |
| INS | Установщик | 2 | 1 | [RT-INS-001](cases/RT-INS-001.md) |

## Требования → кейсы

| Требование | Описание | Кейсы |
|---|---|---|
| `REQ-LCH-01` | Запуск без аргументов показывает главное окно с экраном приветствия (Explore your logs, Open log file, подсказка Ctrl+O / drag and drop). | [RT-LCH-001](cases/RT-LCH-001.md) |
| `REQ-LCH-02` | Пути из командной строки открываются как документы при запуске (несколько файлов). | [RT-LCH-002](cases/RT-LCH-002.md) |
| `REQ-LCH-03` | Относительные пути командной строки разрешаются относительно текущего каталога. | [RT-LCH-003](cases/RT-LCH-003.md) |
| `REQ-LCH-04` | Несуществующий путь в командной строке / при открытии игнорируется без аварии. | [RT-LCH-004](cases/RT-LCH-004.md) |
| `REQ-LCH-05` | Единственный экземпляр: повторный запуск с путями передаёт их в уже запущенное окно. | [RT-LCH-005](cases/RT-LCH-005.md) |
| `REQ-LCH-06` | Ключ --version печатает версию и завершает процесс без открытия окна. | [RT-LCH-006](cases/RT-LCH-006.md) |
| `REQ-LCH-07` | Сочетания клавиш поиска работают сразу после старта, до первого клика по окну (#65). | [RT-LCH-007](cases/RT-LCH-007.md) |
| `REQ-LCH-08` | Повторный запуск без аргументов не открывает второе окно и не влияет на первое. | [RT-LCH-005](cases/RT-LCH-005.md) |
| `REQ-DOC-01` | Открытие файлов через кнопку Open / Ctrl+O, мультивыбор в диалоге, фильтры диалога (All Files, Log files, Text files). | [RT-DOC-001](cases/RT-DOC-001.md) |
| `REQ-DOC-02` | Drag & drop одного или нескольких файлов, а также папки (рекурсивно все файлы). | [RT-DOC-002](cases/RT-DOC-002.md) |
| `REQ-DOC-03` | Повторное открытие уже открытого файла (сравнение полного пути без учёта регистра) активирует существующую вкладку. | [RT-LCH-005](cases/RT-LCH-005.md) |
| `REQ-DOC-04` | Контекстное меню вкладки: Copy Filename, Copy Full Path, Open Containing Folder. | [RT-DOC-003](cases/RT-DOC-003.md) |
| `REQ-DOC-05` | Вкладки AvalonDock: переключение, перестановка, закрытие, плавающее/закреплённое состояние без потери состояния документа. | [RT-DOC-004](cases/RT-DOC-004.md) |
| `REQ-DOC-06` | Закрытие вкладки во время загрузки/индексации не приводит к ошибке и освобождает файл (#73). | [RT-DOC-005](cases/RT-DOC-005.md) |
| `REQ-DOC-07` | Пути с пробелами и не-ASCII символами открываются корректно, имя вкладки отображается полностью/сокращённо без искажений. | [RT-LCH-003](cases/RT-LCH-003.md) |
| `REQ-ENC-01` | Определение кодировки: UTF-8 с BOM и без, UTF-16LE с BOM и без BOM, UTF-16BE с BOM. | [RT-ENC-001](cases/RT-ENC-001.md) |
| `REQ-ENC-02` | Смешанные окончания строк (CRLF, LF, CR) и отсутствие перевода строки в конце файла. | [RT-ENC-002](cases/RT-ENC-002.md) |
| `REQ-ENC-03` | Пустой файл и файл только из пустых строк открываются без ошибок. | [RT-ENC-003](cases/RT-ENC-003.md) |
| `REQ-ENC-04` | Двоичный (не текстовый) файл открывается без аварии. | [RT-ENC-003](cases/RT-ENC-003.md) |
| `REQ-LRG-01` | Большие файлы (≥1 ГБ) открываются потоково: интерфейс отзывчив во время индексации, отображается прогресс. | [RT-LRG-001](cases/RT-LRG-001.md) |
| `REQ-LRG-02` | Поиск и фильтрация работают на большом файле и могут быть прерваны/изменены во время выполнения. | [RT-LRG-002](cases/RT-LRG-002.md) |
| `REQ-LRG-03` | Файл, открытый другим процессом на запись (FileShare.ReadWrite/Read), открывается для чтения. | [RT-LRG-003](cases/RT-LRG-003.md) |
| `REQ-LRG-04` | Файл, заблокированный эксклюзивно, не приводит к аварии приложения. | [RT-LRG-004](cases/RT-LRG-004.md) |
| `REQ-LRG-05` | Изменение файла после открытия (дописывание) не ломает отображение уже прочитанной части. | [RT-LRG-003](cases/RT-LRG-003.md) |
| `REQ-LRG-06` | Удаление/переименование файла на диске при открытом документе не приводит к аварии. | [RT-DOC-005](cases/RT-DOC-005.md), [RT-LRG-005](cases/RT-LRG-005.md) |
| `REQ-FMT-01` | Формат лога описывается регулярным выражением с именованными группами; поля становятся колонками. | [RT-FMT-001](cases/RT-FMT-001.md), [RT-FMT-005](cases/RT-FMT-005.md), [RT-FMT-007](cases/RT-FMT-007.md) |
| `REQ-FMT-02` | Автоопределение формата по первым строкам файла (до 256 строк / 1 МБ): выбирается формат с наибольшей долей совпадений. | [RT-FMT-003](cases/RT-FMT-003.md), [RT-FMT-007](cases/RT-FMT-007.md) |
| `REQ-FMT-03` | Порог автоопределения: доля совпадений должна быть больше 5%, иначе файл открывается как текст (вся строка — одно поле). | [RT-FMT-003](cases/RT-FMT-003.md) |
| `REQ-FMT-04` | При равной доле совпадений выбирается формат, стоящий раньше в списке. | [RT-FMT-004](cases/RT-FMT-004.md) |
| `REQ-FMT-05` | Строки, не подходящие под формат, присоединяются к предыдущей записи (многострочные записи). | [RT-FMT-002](cases/RT-FMT-002.md) |
| `REQ-FMT-06` | Синтаксис групп `(?<Name>…)` и `(?'Name'…)` поддерживается одинаково. | [RT-FMT-001](cases/RT-FMT-001.md), [RT-FMT-005](cases/RT-FMT-005.md) |
| `REQ-FMT-07` | Некорректный формат (невалидный regex / неизвестное индексируемое поле) пропускается при открытии файла без аварии. | [RT-FMT-006](cases/RT-FMT-006.md) |
| `REQ-FMT-08` | TimeField/TimeFormat: явное поле времени (с учётом регистра) и формат времени; по умолчанию поле `Time` без учёта регистра. | [RT-FMT-005](cases/RT-FMT-005.md) |
| `REQ-IDX-01` | Поля из IndexedFields доступны как фильтруемые колонки с перечнем значений. | [RT-FMT-001](cases/RT-FMT-001.md) |
| `REQ-IDX-02` | Неиндексируемые поля отображаются как колонки, но без фильтра значений. | [RT-FMT-001](cases/RT-FMT-001.md) |
| `REQ-IDX-03` | Ширина колонок: двойной клик по разделителю заголовка подгоняет ширину; Message расширяется и сохраняет ширину. | [RT-IDX-001](cases/RT-IDX-001.md) |
| `REQ-FLT-01` | Фильтр значений колонки через всплывающее окно: флажки значений, Select All, Deselect All, поиск Search..., Search Results Only. | [RT-FLT-001](cases/RT-FLT-001.md) |
| `REQ-FLT-02` | Контекстное меню ячейки индексируемого поля: Exclude и Exclude All But. | [RT-FLT-002](cases/RT-FLT-002.md) |
| `REQ-FLT-03` | Чипы фильтров над сеткой: `Поле: значение` для одного значения, `Поле: N hidden` для нескольких, удаление чипа (Remove filter) и Clear all filters. | [RT-FLT-001](cases/RT-FLT-001.md), [RT-FLT-002](cases/RT-FLT-002.md) |
| `REQ-FLT-04` | Фильтры по нескольким колонкам комбинируются (AND по колонкам). | [RT-FLT-002](cases/RT-FLT-002.md) |
| `REQ-FLT-05` | Фильтр, исключающий все значения, даёт пустую сетку без ошибок; снятие фильтра восстанавливает строки. | [RT-FLT-003](cases/RT-FLT-003.md) |
| `REQ-FLT-06` | Фильтры согласованы с поиском, таймлайном и метками (скрытые строки не попадают в навигацию поиска). | [RT-FLT-003](cases/RT-FLT-003.md) |
| `REQ-FLT-07` | Esc закрывает всплывающее окно фильтра. | [RT-FLT-001](cases/RT-FLT-001.md) |
| `REQ-TML-01` | Таймлайн (минимап) отображает плотность всего файла и позволяет выбрать диапазон времени перетаскиванием маркеров. | [RT-TML-001](cases/RT-TML-001.md) |
| `REQ-TML-02` | При наведении на таймлайн рисуется вертикальная линия и время под курсором (или `Line N` в режиме номеров строк) (#23). | [RT-TML-002](cases/RT-TML-002.md) |
| `REQ-TML-03` | Без меток времени (или при немонотонном времени с датой) таймлайн работает в режиме номеров строк. | [RT-TML-002](cases/RT-TML-002.md), [RT-TML-004](cases/RT-TML-004.md) |
| `REQ-TML-04` | Нормализация времени без даты: переход через полночь (откат > 1 ч = новые сутки), малые откаты выравниваются. | [RT-TML-003](cases/RT-TML-003.md) |
| `REQ-TML-05` | Положение таймлайна: View → Timeline (Timeline at top / At the bottom / Hidden), сохраняется (TimelineAtTop, TimelineVisible). | [RT-TML-005](cases/RT-TML-005.md) |
| `REQ-TML-06` | Метки и результаты поиска отображаются на таймлайне; клик по таймлайну переходит к позиции. | [RT-TML-001](cases/RT-TML-001.md) |
| `REQ-TML-07` | Одинаковое время у всех строк и файл из одной строки не ломают таймлайн. | [RT-TML-004](cases/RT-TML-004.md) |
| `REQ-SRCH-01` | Поиск по подстроке без учёта регистра по умолчанию; запуск через ~0,5 с после ввода; счётчик `{N} matches` и позиция `{i} of {N}`. | [RT-SRCH-001](cases/RT-SRCH-001.md) |
| `REQ-SRCH-02` | Переключатель Match case. | [RT-SRCH-001](cases/RT-SRCH-001.md) |
| `REQ-SRCH-03` | Переключатель Use Regular Expressions; невалидный regex показывается как ошибка поля без аварии. | [RT-SRCH-002](cases/RT-SRCH-002.md) |
| `REQ-SRCH-04` | Список результатов поиска с разобранными полями; выбор результата переходит к строке в сетке. | [RT-SRCH-001](cases/RT-SRCH-001.md) |
| `REQ-SRCH-05` | Поиск по строкам продолжения многострочной записи и по Unicode-тексту. | [RT-SRCH-004](cases/RT-SRCH-004.md) |
| `REQ-SRCH-06` | Несколько вкладок поиска (New search), закрытие вкладки поиска. | [RT-SRCH-005](cases/RT-SRCH-005.md) |
| `REQ-SRCH-07` | Автодополнение по истории поисков (сохраняется в AutocompleteCache.json между запусками). | [RT-SRCH-006](cases/RT-SRCH-006.md) |
| `REQ-SRCH-08` | Подсветка совпадений в сетке; при regex с `.*` поглощённый текст приглушён (25% светлая / 30% тёмная тема). | [RT-SRCH-002](cases/RT-SRCH-002.md), [RT-ALT-003](cases/RT-ALT-003.md) |
| `REQ-SRCH-09` | Esc в поле поиска очищает текст; Alt+X очищает активный поиск без перевода фокуса. | [RT-SRCH-007](cases/RT-SRCH-007.md) |
| `REQ-SRCH-10` | Большое число совпадений (тысячи) обрабатывается без зависаний, прогресс поиска виден. | [RT-SRCH-008](cases/RT-SRCH-008.md) |
| `REQ-SRCH-11` | Множественный выбор результатов поиска (Shift/Ctrl) и операции над ними (копирование, метки) (#43). | [RT-SRCH-009](cases/RT-SRCH-009.md) |
| `REQ-SRCH-12` | Специальные символы regex в обычном (не regex) режиме ищутся буквально. | [RT-SRCH-003](cases/RT-SRCH-003.md) |
| `REQ-SAV-01` | Сохранение текущего поиска (Save current search…) с именем; подтверждение замены при совпадении имени. | [RT-SAV-001](cases/RT-SAV-001.md) |
| `REQ-SAV-02` | Применение сохранённого поиска из выпадающего списка с фильтром. | [RT-SAV-001](cases/RT-SAV-001.md) |
| `REQ-SAV-03` | Окно Manage saved searches…: фильтр, создание (Ctrl+N), дублирование, удаление (Delete) с отложенным удалением и Restore search, Save (Ctrl+S), Cancel. | [RT-SAV-002](cases/RT-SAV-002.md), [RT-SAV-003](cases/RT-SAV-003.md) |
| `REQ-SAV-04` | Валидация: пустой шаблон (`Enter a search query.`), невалидный regex; подтверждение `Discard unsaved changes?` при переключении. | [RT-SAV-002](cases/RT-SAV-002.md) |
| `REQ-SAV-05` | Сохранённые поиски сохраняются между запусками (SavedSearches.json). | [RT-SAV-001](cases/RT-SAV-001.md) |
| `REQ-ALT-01` | Удержание Alt подсвечивает текст под курсором, который будет добавлен в поиск. | [RT-ALT-001](cases/RT-ALT-001.md) |
| `REQ-ALT-02` | Alt+клик: первый клик начинает regex-поиск, последующие добавляют `.*слово`. | [RT-ALT-001](cases/RT-ALT-001.md) |
| `REQ-ALT-03` | Пары `key = value` / `key: value`: курсор на ключе/разделителе выделяет пару, на значении — только значение; фигурные скобки сохраняются. | [RT-ALT-002](cases/RT-ALT-002.md) |
| `REQ-ALT-04` | Кавычки, пути Windows (`\\?\Volume{GUID}\`, `\Device\HarddiskVolume1\`), скобки `[] <> ()` (содержимое без скобок, с вложенностью), апостроф внутри слова. | [RT-ALT-002](cases/RT-ALT-002.md) |
| `REQ-ALT-05` | Существующий не-regex текст в поле экранируется перед добавлением; для форматированного JSON/XML добавляется исходная подстрока. | [RT-ALT-001](cases/RT-ALT-001.md), [RT-ALT-003](cases/RT-ALT-003.md) |
| `REQ-NAV-01` | F3/Shift+F3 переходят к следующему/предыдущему совпадению от текущей строки с переходом через конец/начало. | [RT-NAV-001](cases/RT-NAV-001.md) |
| `REQ-NAV-02` | Переход к совпадению или метке центрирует строку в видимой области. | [RT-NAV-001](cases/RT-NAV-001.md) |
| `REQ-NAV-03` | Клавиши Up/Down/PageUp/PageDown, Shift+Up/Down (расширение выделения), Ctrl+A. | [RT-NAV-002](cases/RT-NAV-002.md) |
| `REQ-NAV-04` | Горизонтальная прокрутка сетки, включая вторую ось колеса/тачпада (#22). | [RT-NAV-003](cases/RT-NAV-003.md) |
| `REQ-ZM-01` | Масштаб текста: Ctrl+= / Ctrl+Shift+= увеличивают, Ctrl+- уменьшает, Ctrl+0 сбрасывает к 12, Ctrl+колесо; диапазон 6–48. | [RT-ZM-001](cases/RT-ZM-001.md) |
| `REQ-ZM-02` | Меню View → Text size (Decrease/Reset/Increase) и значение LogFontSize сохраняется между запусками. | [RT-ZM-001](cases/RT-ZM-001.md) |
| `REQ-MRK-01` | Пометка строк булавкой и клавишей Space (переключение для выделения: если есть непомеченные — помечаются все). | [RT-MRK-001](cases/RT-MRK-001.md) |
| `REQ-MRK-02` | Панель Marked lines: группы по файлам, Clear all для группы, Copy lines; навигация стрелками без Enter; переход центрирует строку. | [RT-MRK-001](cases/RT-MRK-001.md) |
| `REQ-MRK-03` | Метки сохраняются при смене профиля/перечитывании документа и отображаются на таймлайне. | [RT-MRK-002](cases/RT-MRK-002.md) |
| `REQ-MRK-04` | Точка-метка на таймлайне/булавке центрирована (#21). | [RT-MRK-002](cases/RT-MRK-002.md) |
| `REQ-JSN-01` | JSON-объекты и массивы в сообщении распознаются, форматируются с отступами и сворачиваются; в свёрнутом виде занимают одну строку. | [RT-JSN-001](cases/RT-JSN-001.md) |
| `REQ-JSN-02` | Контекстное меню Folding: Expand All, Collapse All, Expand recursively, Collapse recursively. | [RT-JSN-001](cases/RT-JSN-001.md) |
| `REQ-JSN-03` | Состояние сворачивания общее для сетки, результатов поиска и Marked lines одного документа. | [RT-JSN-002](cases/RT-JSN-002.md) |
| `REQ-JSN-04` | Многострочный JSON в строках продолжения (в т.ч. с табуляцией) распознаётся как часть Message и форматируется (#31). | [RT-JSN-003](cases/RT-JSN-003.md) |
| `REQ-JSN-05` | Некорректный JSON отображается как текст без аварии; текст до и после JSON сохраняется; несколько JSON в строке. | [RT-JSN-001](cases/RT-JSN-001.md) |
| `REQ-JSN-06` | Производительность раскрытия больших и глубоких JSON (#38). | [RT-JSN-004](cases/RT-JSN-004.md) |
| `REQ-JSN-07` | XML-фрагменты (с `<?xml ...?>` или без) форматируются и сворачиваются, если у корня есть дочерние элементы; обобщения `List<int>`, `<br/>`, элемент только с текстом — не XML (#49). | [RT-JSN-005](cases/RT-JSN-005.md) |
| `REQ-JSN-08` | Unicode и escape-последовательности в JSON отображаются корректно; JSON внутри XML и XML внутри JSON. | [RT-JSN-005](cases/RT-JSN-005.md) |
| `REQ-B64-01` | Base64 всего значения ячейки (в т.ч. в кавычках) от 8 символов и фрагменты от 16 символов (в т.ч. JWT и «приклеенные» к тексту) декодируются кнопкой BIN. | [RT-B64-001](cases/RT-B64-001.md) |
| `REQ-B64-02` | Кнопка BIN расположена у правого края колонки Component; без колонки Component и в Marked lines — под булавкой. | [RT-B64-001](cases/RT-B64-001.md), [RT-B64-006](cases/RT-B64-006.md) |
| `REQ-B64-03` | BIN переключает всю строку (все поля, Base64 и PEM); контекстное меню ячейки Decode Base64 / Decode PEM переключает виды раздельно. | [RT-B64-001](cases/RT-B64-001.md) |
| `REQ-B64-04` | Декодируется только валидный UTF-8 без управляющих символов; GUID, hex, слова и бинарные данные не декодируются; URL-safe алфавит и отсутствие паддинга поддерживаются. | [RT-B64-002](cases/RT-B64-002.md) |
| `REQ-B64-05` | PEM-блоки (реальные и экранированные переводы строк): сертификат → сводка, текст → текст, прочее → hex dump первых 4 КБ; несовпадающие маркеры не декодируются. | [RT-B64-003](cases/RT-B64-003.md) |
| `REQ-B64-06` | DER X.509, SubjectPublicKeyInfo и CryptoAPI PUBLICKEYBLOB в Base64 без маркеров распознаются как ключи/сертификаты. | [RT-B64-003](cases/RT-B64-003.md) |
| `REQ-B64-07` | Base64/PEM внутри JSON и XML декодируются с сохранением валидности; ключи JSON не декодируются; PEM в JSON раскрывается в JSON-объект по полям (#77). | [RT-B64-004](cases/RT-B64-004.md) |
| `REQ-B64-08` | Copy копирует отображаемый (декодированный) текст, Copy as native — исходный. | [RT-B64-005](cases/RT-B64-005.md) |
| `REQ-B64-09` | Состояние BIN сохраняется при прокрутке и пересоздании строк (#78). | [RT-B64-007](cases/RT-B64-007.md) |
| `REQ-B64-10` | Настройки Settings → View → Binary data detection: Detect binary data (BIN), PEM blocks, certificates and keys, Base64 text, HEX применяются к открытым документам (#53). | [RT-B64-008](cases/RT-B64-008.md) |
| `REQ-HEX-01` | HEX-последовательности от 9 байт (слитно, через пробел, `-`, `:`, `,`, с префиксом `0x`) декодируются в UTF-8 или UTF-16LE. | [RT-B64-008](cases/RT-B64-008.md), [RT-HEX-001](cases/RT-HEX-001.md) |
| `REQ-HEX-02` | Значения до 64 бит, токены только из цифр, хэши, GUID и непечатаемые данные не декодируются. | [RT-HEX-001](cases/RT-HEX-001.md) |
| `REQ-HEX-03` | HEX в JSON/XML декодируется с экранированием; HEX, дающий JSON/XML, форматируется. | [RT-HEX-002](cases/RT-HEX-002.md) |
| `REQ-HEX-04` | HEX-кодированные DER/PEM сертификаты показываются как сводка (#75); Decode HEX в контекстном меню. | [RT-B64-005](cases/RT-B64-005.md), [RT-HEX-002](cases/RT-HEX-002.md) |
| `REQ-TRN-01` | Transformations формата: правило Base64Decode заменяет найденную группу декодированным текстом только в подходящих строках. | [RT-TRN-001](cases/RT-TRN-001.md) |
| `REQ-TRN-02` | Base64DecodeFormatJson декодирует и форматирует JSON (в т.ч. для XOR-формата). | [RT-XOR-001](cases/RT-XOR-001.md) |
| `REQ-XOR-01` | Файлы, замаскированные однобайтовым XOR (XorMask формата), открываются и ищутся как обычные. | [RT-XOR-001](cases/RT-XOR-001.md) |
| `REQ-XOR-02` | Файл с другим XOR-ключом не определяется форматом с XorMask и не вызывает аварию. | [RT-XOR-001](cases/RT-XOR-001.md) |
| `REQ-BIG-01` | Строки длиннее BigLineSize (по умолчанию 4096 в регрессионной конфигурации) обрезаются (`Truncate`, многоточие `...`) или переносятся (`Wrap`) по BigLineSize символов. | [RT-JSN-004](cases/RT-JSN-004.md), [RT-BIG-001](cases/RT-BIG-001.md), [RT-BIG-002](cases/RT-BIG-002.md) |
| `REQ-BIG-02` | Строки длиной ≤ BigLineSize отображаются полностью; копирование возвращает исходный полный текст. | [RT-BIG-001](cases/RT-BIG-001.md) |
| `REQ-BIG-03` | Валидация BigLineSize: `Big line size must be a positive integer.`; изменение применяется после сохранения. | [RT-BIG-002](cases/RT-BIG-002.md), [RT-SET-004](cases/RT-SET-004.md) |
| `REQ-CLR-01` | Правила цвета профиля применяются к тексту строки; срабатывает первое подходящее правило; правило без цвета оставляет цвет по умолчанию. | [RT-CLR-001](cases/RT-CLR-001.md) |
| `REQ-CLR-02` | ForegroundColor и BackgroundColor (именованные цвета и `#AARRGGBB`) поддерживаются; правила видны в обеих темах. | [RT-CLR-001](cases/RT-CLR-001.md), [RT-CLR-002](cases/RT-CLR-002.md) |
| `REQ-CLR-03` | Добавление/изменение/удаление правил в Settings → Profiles → Colors (и в YAML) применяется к открытым документам. | [RT-CLR-002](cases/RT-CLR-002.md) |
| `REQ-THR-01` | Group by thread (View) объединяет подряд идущие строки одного Thread, приглушает повтор Thread и рисует разделители; настройка сохраняется (GroupByThread). | [RT-THR-001](cases/RT-THR-001.md) |
| `REQ-THR-02` | Show thread group separators (Settings → View) скрывает/показывает разделители, приглушение сохраняется. | [RT-THR-001](cases/RT-THR-001.md) |
| `REQ-THR-03` | При группировке строки других потоков приглушены, а панель поиска показывает только совпадения потока текущей строки; смена строки переключает фокус (#56). | [RT-THR-002](cases/RT-THR-002.md) |
| `REQ-STK-01` | Распознаются кадры `N# module!GUID,age!RVA`, `N# module!RVA`, `N# module+0xRVA`; у ячейки появляется SYM под BIN и пункт Resolve call stack. | [RT-STK-001](cases/RT-STK-001.md) |
| `REQ-STK-02` | До нажатия SYM ничего не загружается; во время загрузки кадр показывает `resolving symbols…`; результат `module!Function+0x.. [file @ line]`; состояние сохраняется при прокрутке. | [RT-STK-001](cases/RT-STK-001.md), [RT-STK-002](cases/RT-STK-002.md) |
| `REQ-STK-03` | Окно Show call stack: кадры (модуль, функция, исходник), Copy call stack, Reload symbols, просмотр исходника с подсветкой строки, Open in editor; следует теме и акценту. | [RT-STK-002](cases/RT-STK-002.md), [RT-STK-003](cases/RT-STK-003.md) |
| `REQ-STK-04` | Ошибки: `no symbols`, `symbol not found`, `Symbol path is not configured: …`; попытки поиска PDB в подсказке. | [RT-STK-001](cases/RT-STK-001.md) |
| `REQ-STK-05` | Settings → Symbols: Resolve call stacks in log messages, Symbol path (по строке на элемент), Source path, Symbol cache folder, Allow source server commands; применяется без перезапуска (#89). | [RT-STK-002](cases/RT-STK-002.md), [RT-STK-003](cases/RT-STK-003.md) |
| `REQ-VM-01` | Значение из списка маппинга отображается как `value(mapping)`; совпадение только целым токеном; длиннейшее значение выигрывает; при равных — первый список. | [RT-VM-001](cases/RT-VM-001.md) |
| `REQ-VM-02` | Enabled, Ignore case и Fields (через запятую, пусто = все поля) у списка. | [RT-VM-001](cases/RT-VM-001.md) |
| `REQ-VM-03` | В JSON строки маппятся внутри кавычек, числа — комментарием `5 /*five*/`, ключи не маппятся; в XML — текст, атрибуты, CDATA с экранированием; сворачивание сохраняется. | [RT-VM-002](cases/RT-VM-002.md) |
| `REQ-VM-04` | Маппинг меняет только отображение и копирование; поиск, фильтры, цвета и индексы работают по исходным значениям. | [RT-VM-002](cases/RT-VM-002.md) |
| `REQ-VM-05` | Import JSON... (объект списка, массив списков, плоский объект `{value: mapping}`; замена по имени), Export list..., Export all...; ошибки импорта. | [RT-VM-001](cases/RT-VM-001.md), [RT-VM-003](cases/RT-VM-003.md) |
| `REQ-VM-06` | Предпросмотр Preview, валидация (пустое значение, дубликаты) и применение после Save к открытым строкам без переоткрытия (#58). | [RT-VM-001](cases/RT-VM-001.md), [RT-VM-003](cases/RT-VM-003.md) |
| `REQ-GRD-01` | Ctrl+C / Copy копирует выделенные строки полностью (без многоточий) в исходном виде (#33). | [RT-GRD-001](cases/RT-GRD-001.md) |
| `REQ-GRD-02` | Копирование более 64 000 000 символов предлагает сохранить выделение в файл (Save selection, `selection.txt`). | [RT-GRD-002](cases/RT-GRD-002.md) |
| `REQ-GRD-03` | Массовые операции (копирование, метки) дольше 1 с показывают карточку прогресса с отменой (Esc/Cancel); UI остаётся отзывчивым. | [RT-GRD-002](cases/RT-GRD-002.md) |
| `REQ-GRD-04` | Высота ползунка вертикальной прокрутки не меняется при прокрутке через строки разной высоты (#67). | [RT-GRD-003](cases/RT-GRD-003.md) |
| `REQ-GRD-05` | Ширина колонки Message подгоняется по содержимому при загрузке. | [RT-GRD-001](cases/RT-GRD-001.md) |
| `REQ-SET-01` | Окно Settings: вкладки View, Profiles (Colors, Log formats), Value mappings, Symbols, Debug; Save/Cancel; изменения применяются после Save. | [RT-SET-001](cases/RT-SET-001.md) |
| `REQ-SET-02` | Ручное изменение `appsettings.yaml` подхватывается без перезапуска (цвета, форматы, обнаружение бинарных данных, маппинги, символы, профили). | [RT-SET-002](cases/RT-SET-002.md) |
| `REQ-SET-03` | Некорректный YAML не приводит к аварии: приложение запускается/продолжает работу с последней корректной конфигурацией и сообщает об ошибке. | [RT-SET-003](cases/RT-SET-003.md) |
| `REQ-SET-04` | Проблемы при Save перечисляются; доступно автоисправление `Fix all and save`, отдельное `Apply this fix`, переход к полю по ссылке; невалидный regex формата требует ручного исправления (#91). | [RT-SET-004](cases/RT-SET-004.md) |
| `REQ-SET-05` | Комментарии и неизвестные ключи YAML сохраняются при записи настроек через окно. | [RT-SET-001](cases/RT-SET-001.md) |
| `REQ-LNG-01` | Языки интерфейса: System language, English, Русский, Deutsch, Français, Español, 日本語, Polski, Português (Brasil); смена после перезапуска (`The interface language changes after restarting the application.`). | [RT-LNG-001](cases/RT-LNG-001.md) |
| `REQ-LNG-02` | Содержимое логов, имена полей/колонок, имена профилей и ключи YAML не переводятся; неподдерживаемый системный язык → English. | [RT-LNG-001](cases/RT-LNG-001.md) |
| `REQ-PRF-01` | Селектор профиля в заголовке: переключение сразу применяется ко всем открытым документам и сохраняется (SelectedProfile). | [RT-PRF-001](cases/RT-PRF-001.md) |
| `REQ-PRF-02` | Settings → Profiles: New, Copy (`{0} copy`), Remove (остаётся минимум один), уникальные непустые имена. | [RT-PRF-002](cases/RT-PRF-002.md) |
| `REQ-PRF-03` | Смена форматов/цветов перечитывает открытые логи с сохранением меток; переименование без изменения конфигурации не перечитывает. | [RT-PRF-001](cases/RT-PRF-001.md), [RT-PRF-002](cases/RT-PRF-002.md) |
| `REQ-PRF-04` | Профиль, который не удаётся применить, показывает `Profile change failed` / `Cannot apply profile '{0}': {1}`, сохраняется последняя рабочая конфигурация. | [RT-PRF-003](cases/RT-PRF-003.md) |
| `REQ-PRF-05` | Старая конфигурация без Profiles отображается как `Default`; наследование legacy-настроек для ручных профилей, `InheritLegacySettings: false`, явные пустые списки. | [RT-PRF-003](cases/RT-PRF-003.md) |
| `REQ-THM-01` | View → Theme → Light/Dark (и переключатель в заголовке) применяются ко всему приложению сразу и сохраняются в `Theme.json`. | [RT-THM-001](cases/RT-THM-001.md) |
| `REQ-THM-02` | Accent color: Blue, Purple, Pink, Red, Orange, Green, Teal — применяется сразу и сохраняется. | [RT-THM-001](cases/RT-THM-001.md) |
| `REQ-DMP-01` | Debug → Enable crash dumps / Max dumps count (положительное целое) настраивает WER LocalDumps для LogGrokX.exe (через UAC), отключение удаляет ключ; требуется перезапуск. | [RT-SET-004](cases/RT-SET-004.md), [RT-DMP-001](cases/RT-DMP-001.md) |
| `REQ-SUP-01` | Окно Support (`?`): Version, Commit, Branch, Runtime, OS, Architecture; Check for updates, Copy diagnostics, Open logs folder, Download the latest release, Report an issue, Source code on GitHub. | [RT-SUP-001](cases/RT-SUP-001.md) |
| `REQ-MRG-01` | Merged files view (View) объединяет все открытые документы по времени; колонки выравниваются без учёта регистра; Level/Severity → Severity. | [RT-MRG-001](cases/RT-MRG-001.md) |
| `REQ-MRG-02` | Порядок: по времени; при равном времени — по порядку источника, затем по номеру строки; источники без времени или с немонотонным временем — в конце; простой текст → Message. | [RT-MRG-001](cases/RT-MRG-001.md) |
| `REQ-MRG-03` | В объединённом виде работают поиск, фильтр времени, группировка, JSON, метки; строки подкрашены по источнику; режим только на сессию. | [RT-MRG-002](cases/RT-MRG-002.md) |
| `REQ-UPD-01` | Режимы UpdateMode: disabled / check / install; проверка при первом запуске в новый календарный день и по часовому таймеру; без лимита GitHub API (#36, #40). | [RT-UPD-001](cases/RT-UPD-001.md) |
| `REQ-UPD-02` | Диалог `Update available` с release notes, `Update`, `Later`, `Skip this version`; пропущенная версия не предлагается повторно (#27). | [RT-UPD-002](cases/RT-UPD-002.md) |
| `REQ-UPD-03` | Индикатор в заголовке: `Update to {0}`, `Downloading {0:0}%`, `Restart: {0}`; скрывается настройкой Update indicator in title bar (#99). | [RT-UPD-003](cases/RT-UPD-003.md) |
| `REQ-UPD-04` | Update: загрузка установщика текущей архитектуры с проверкой SHA256SUMS.txt; Restart сохраняет открытые файлы и открывает их после установки; тихая установка при выходе; What's new после обновления. | [RT-UPD-002](cases/RT-UPD-002.md), [RT-UPD-003](cases/RT-UPD-003.md) |
| `REQ-UPD-05` | Портативная сборка не обновляется сама — открывает страницу релиза; проверка через системный прокси с учётными данными Windows (#29). | [RT-UPD-001](cases/RT-UPD-001.md), [RT-UPD-003](cases/RT-UPD-003.md) |
| `REQ-INS-01` | Установщик: выбор языка (8 языков), для пользователя/для всех, ярлык на рабочем столе, ассоциация `.log`, Install updates automatically. | [RT-INS-001](cases/RT-INS-001.md) |
| `REQ-INS-02` | Обновление установкой поверх сохраняет `appsettings.yaml`; удаление оставляет `appsettings.yaml` и удаляет ассоциацию. | [RT-INS-001](cases/RT-INS-001.md) |

## Дефекты → кейсы

| Issue | Название | Кейсы |
|---|---|---|
| [#21](https://github.com/zhenyatnk/LogGrokX/issues/21) | Точка mark выравнивается по центру по вертикали | [RT-MRK-002](cases/RT-MRK-002.md) |
| [#22](https://github.com/zhenyatnk/LogGrokX/issues/22) | Горизонтальное скроллирование вторым колесом мыши | [RT-NAV-003](cases/RT-NAV-003.md) |
| [#23](https://github.com/zhenyatnk/LogGrokX/issues/23) | При наведении на timeline показывать время | [RT-TML-002](cases/RT-TML-002.md) |
| [#27](https://github.com/zhenyatnk/LogGrokX/issues/27) | Предложение обновиться при открытии, если есть новые версии | [RT-UPD-001](cases/RT-UPD-001.md), [RT-UPD-002](cases/RT-UPD-002.md) |
| [#29](https://github.com/zhenyatnk/LogGrokX/issues/29) | Проблемы с проверкой обновления через прокси | [RT-UPD-001](cases/RT-UPD-001.md) |
| [#31](https://github.com/zhenyatnk/LogGrokX/issues/31) | Перенос строки с табуляцией в сообщении воспринимается как неформатированный | [RT-JSN-003](cases/RT-JSN-003.md) |
| [#33](https://github.com/zhenyatnk/LogGrokX/issues/33) | При копировании строка обрезается тремя точками | [RT-BIG-001](cases/RT-BIG-001.md), [RT-GRD-001](cases/RT-GRD-001.md) |
| [#35](https://github.com/zhenyatnk/LogGrokX/issues/35) | Форматирование base64 строки | [RT-B64-001](cases/RT-B64-001.md) |
| [#36](https://github.com/zhenyatnk/LogGrokX/issues/36) | Проверять обновление при первом запуске в новом дне | [RT-UPD-001](cases/RT-UPD-001.md) |
| [#38](https://github.com/zhenyatnk/LogGrokX/issues/38) | Раскрытие JSON «тормозит» | [RT-JSN-004](cases/RT-JSN-004.md) |
| [#40](https://github.com/zhenyatnk/LogGrokX/issues/40) | Частые ошибки проверки обновления 403 (rate limit exceeded) | [RT-UPD-001](cases/RT-UPD-001.md) |
| [#43](https://github.com/zhenyatnk/LogGrokX/issues/43) | Нельзя выделить несколько строк в поисковой выдаче | [RT-SRCH-009](cases/RT-SRCH-009.md) |
| [#48](https://github.com/zhenyatnk/LogGrokX/issues/48) | Форматирование HEX строки | [RT-HEX-001](cases/RT-HEX-001.md) |
| [#49](https://github.com/zhenyatnk/LogGrokX/issues/49) | Распознавание XML и форматирование как JSON | [RT-JSN-005](cases/RT-JSN-005.md) |
| [#53](https://github.com/zhenyatnk/LogGrokX/issues/53) | Отключение детектирования BIN данных в настройках | [RT-B64-008](cases/RT-B64-008.md) |
| [#56](https://github.com/zhenyatnk/LogGrokX/issues/56) | Изменение отображения режима подсветки потоков | [RT-THR-002](cases/RT-THR-002.md) |
| [#58](https://github.com/zhenyatnk/LogGrokX/issues/58) | Маппинг значений | [RT-VM-001](cases/RT-VM-001.md) |
| [#65](https://github.com/zhenyatnk/LogGrokX/issues/65) | Ctrl+F не работает сразу после старта | [RT-LCH-007](cases/RT-LCH-007.md) |
| [#67](https://github.com/zhenyatnk/LogGrokX/issues/67) | Размер полосы прокрутки по вертикали меняется при прокрутке | [RT-GRD-003](cases/RT-GRD-003.md) |
| [#73](https://github.com/zhenyatnk/LogGrokX/issues/73) | Утечка памяти при закрытии загружающегося лога | [RT-DOC-005](cases/RT-DOC-005.md) |
| [#75](https://github.com/zhenyatnk/LogGrokX/issues/75) | BIN не раскрывает сертификат, если HEX->PEM | [RT-HEX-002](cases/RT-HEX-002.md) |
| [#77](https://github.com/zhenyatnk/LogGrokX/issues/77) | hex(base64)->pem в JSON раскрывать по полям | [RT-B64-004](cases/RT-B64-004.md), [RT-HEX-002](cases/RT-HEX-002.md) |
| [#78](https://github.com/zhenyatnk/LogGrokX/issues/78) | Состояние BIN теряется после прокрутки | [RT-B64-007](cases/RT-B64-007.md) |
| [#89](https://github.com/zhenyatnk/LogGrokX/issues/89) | Отображение имён функций для call stack | [RT-STK-001](cases/RT-STK-001.md), [RT-STK-002](cases/RT-STK-002.md), [RT-STK-003](cases/RT-STK-003.md) |
| [#91](https://github.com/zhenyatnk/LogGrokX/issues/91) | Автоисправление проблем appsettings при сохранении | [RT-SET-004](cases/RT-SET-004.md) |
| [#95](https://github.com/zhenyatnk/LogGrokX/issues/95) | Дефекты окна сохранённых поисков | [RT-SAV-002](cases/RT-SAV-002.md), [RT-SAV-003](cases/RT-SAV-003.md) |
| [#97](https://github.com/zhenyatnk/LogGrokX/issues/97) | Добавление слова в строку поиска по клику | [RT-ALT-002](cases/RT-ALT-002.md) |
| [#99](https://github.com/zhenyatnk/LogGrokX/issues/99) | Отображение в заголовке информации о новой версии | [RT-UPD-003](cases/RT-UPD-003.md) |

## Список кейсов

| ID | Название | Приоритет | Тип |
|---|---|---|---|
| [RT-ALT-001](cases/RT-ALT-001.md) | Alt+клик: построение regex из слов | P1 | Функциональный |
| [RT-ALT-002](cases/RT-ALT-002.md) | Alt+клик: пары key=value, кавычки, пути, скобки | P1 | Краевой |
| [RT-ALT-003](cases/RT-ALT-003.md) | Alt+клик в форматированном JSON/XML | P2 | Краевой |
| [RT-B64-001](cases/RT-B64-001.md) | Base64 целого значения и фрагментов, кнопка BIN | P1 | Функциональный |
| [RT-B64-002](cases/RT-B64-002.md) | Base64: негативные случаи | P1 | Негативный |
| [RT-B64-003](cases/RT-B64-003.md) | PEM: сертификат, закрытый ключ, текст | P1 | Функциональный |
| [RT-B64-004](cases/RT-B64-004.md) | Base64/PEM внутри JSON и XML | P1 | Регрессия дефекта |
| [RT-B64-005](cases/RT-B64-005.md) | Copy и Copy as native для декодированных значений | P1 | Функциональный |
| [RT-B64-006](cases/RT-B64-006.md) | BIN под булавкой без колонки Component и в Marked lines | P2 | Функциональный |
| [RT-B64-007](cases/RT-B64-007.md) | BIN сохраняется при прокрутке | P1 | Регрессия дефекта |
| [RT-B64-008](cases/RT-B64-008.md) | Настройки обнаружения двоичных данных | P1 | Регрессия дефекта |
| [RT-BIG-001](cases/RT-BIG-001.md) | Большие строки: граница BigLineSize, Truncate | P1 | Краевой |
| [RT-BIG-002](cases/RT-BIG-002.md) | Большие строки: Wrap и валидация размера | P2 | Краевой |
| [RT-CLR-001](cases/RT-CLR-001.md) | Правила цвета Classic: первое совпадение | P1 | Функциональный |
| [RT-CLR-002](cases/RT-CLR-002.md) | Пользовательское правило цвета | P2 | Функциональный |
| [RT-DMP-001](cases/RT-DMP-001.md) | Аварийные дампы WER | P3 | Функциональный |
| [RT-DOC-001](cases/RT-DOC-001.md) | Открытие файлов через Ctrl+O с мультивыбором | P1 | Функциональный |
| [RT-DOC-002](cases/RT-DOC-002.md) | Drag & drop файлов и папки | P1 | Функциональный |
| [RT-DOC-003](cases/RT-DOC-003.md) | Контекстное меню вкладки | P2 | Функциональный |
| [RT-DOC-004](cases/RT-DOC-004.md) | Работа вкладок AvalonDock: перестановка, плавающее окно, закрытие | P2 | Функциональный |
| [RT-DOC-005](cases/RT-DOC-005.md) | Закрытие вкладки во время загрузки большого файла | P1 | Регрессия дефекта |
| [RT-ENC-001](cases/RT-ENC-001.md) | Определение кодировок UTF-8/UTF-16 | P1 | Функциональный |
| [RT-ENC-002](cases/RT-ENC-002.md) | Смешанные окончания строк и отсутствие финального перевода строки | P2 | Краевой |
| [RT-ENC-003](cases/RT-ENC-003.md) | Пустой файл, файл из пустых строк, двоичный файл | P2 | Краевой |
| [RT-FLT-001](cases/RT-FLT-001.md) | Всплывающее окно фильтра колонки | P1 | Функциональный |
| [RT-FLT-002](cases/RT-FLT-002.md) | Exclude / Exclude All But из контекстного меню и комбинирование фильтров | P1 | Функциональный |
| [RT-FLT-003](cases/RT-FLT-003.md) | Исключение всех значений и взаимодействие с поиском/метками | P2 | Краевой |
| [RT-FMT-001](cases/RT-FMT-001.md) | Разбор TAB-формата Classic и колонки | P1 | Функциональный |
| [RT-FMT-002](cases/RT-FMT-002.md) | Многострочные записи | P1 | Функциональный |
| [RT-FMT-003](cases/RT-FMT-003.md) | Порог автоопределения формата 5% | P1 | Краевой |
| [RT-FMT-004](cases/RT-FMT-004.md) | Неоднозначный формат: выигрывает первый в списке | P2 | Краевой |
| [RT-FMT-005](cases/RT-FMT-005.md) | Профиль Regression: форматы RegA/RegB/RegN и TimeField | P1 | Функциональный |
| [RT-FMT-006](cases/RT-FMT-006.md) | Некорректный формат в выбранном профиле | P1 | Негативный |
| [RT-FMT-007](cases/RT-FMT-007.md) | Профиль Plain text и формат Venn | P2 | Функциональный |
| [RT-GRD-001](cases/RT-GRD-001.md) | Копирование строк без обрезки | P1 | Регрессия дефекта |
| [RT-GRD-002](cases/RT-GRD-002.md) | Копирование больших выделений: прогресс, отмена, лимит 64 млн | P1 | Нефункциональный |
| [RT-GRD-003](cases/RT-GRD-003.md) | Стабильная высота ползунка прокрутки | P2 | Регрессия дефекта |
| [RT-HEX-001](cases/RT-HEX-001.md) | HEX: форматы разделителей и UTF-16 | P1 | Функциональный |
| [RT-HEX-002](cases/RT-HEX-002.md) | HEX в JSON/XML и HEX-сертификаты | P1 | Регрессия дефекта |
| [RT-IDX-001](cases/RT-IDX-001.md) | Автоширина колонок и ширина Message | P3 | Функциональный |
| [RT-INS-001](cases/RT-INS-001.md) | Установка, обновление поверх и удаление | P2 | Функциональный |
| [RT-JSN-001](cases/RT-JSN-001.md) | Форматирование и сворачивание JSON | P1 | Функциональный |
| [RT-JSN-002](cases/RT-JSN-002.md) | Общее состояние сворачивания в сетке, поиске и Marked lines | P2 | Функциональный |
| [RT-JSN-003](cases/RT-JSN-003.md) | Многострочный JSON с табуляцией в строках продолжения | P1 | Регрессия дефекта |
| [RT-JSN-004](cases/RT-JSN-004.md) | Большой и глубокий JSON | P2 | Нефункциональный |
| [RT-JSN-005](cases/RT-JSN-005.md) | XML: распознавание и негативные случаи | P1 | Функциональный |
| [RT-LCH-001](cases/RT-LCH-001.md) | Первый запуск без аргументов: экран приветствия | P1 | Функциональный |
| [RT-LCH-002](cases/RT-LCH-002.md) | Открытие нескольких файлов из командной строки | P1 | Функциональный |
| [RT-LCH-003](cases/RT-LCH-003.md) | Относительный путь и путь с пробелами/кириллицей в командной строке | P2 | Краевой |
| [RT-LCH-004](cases/RT-LCH-004.md) | Несуществующий путь в командной строке игнорируется | P2 | Негативный |
| [RT-LCH-005](cases/RT-LCH-005.md) | Единственный экземпляр: передача путей в запущенное окно | P1 | Функциональный |
| [RT-LCH-006](cases/RT-LCH-006.md) | Ключ --version | P3 | Функциональный |
| [RT-LCH-007](cases/RT-LCH-007.md) | Ctrl+F сразу после старта без клика по окну | P1 | Регрессия дефекта |
| [RT-LNG-001](cases/RT-LNG-001.md) | Языки интерфейса | P2 | Функциональный |
| [RT-LRG-001](cases/RT-LRG-001.md) | Открытие файла ~1 ГБ: потоковая загрузка и отзывчивость | P1 | Нефункциональный |
| [RT-LRG-002](cases/RT-LRG-002.md) | Поиск и фильтрация на большом файле, отмена/смена поиска | P1 | Нефункциональный |
| [RT-LRG-003](cases/RT-LRG-003.md) | Файл, открытый на запись другим процессом, и дописывание | P1 | Краевой |
| [RT-LRG-004](cases/RT-LRG-004.md) | Эксклюзивно заблокированный файл | P2 | Негативный |
| [RT-LRG-005](cases/RT-LRG-005.md) | Удаление и переименование открытого файла | P3 | Негативный |
| [RT-MRG-001](cases/RT-MRG-001.md) | Объединённый просмотр: порядок и колонки | P1 | Функциональный |
| [RT-MRG-002](cases/RT-MRG-002.md) | Объединённый просмотр: поиск, фильтры, метки | P2 | Функциональный |
| [RT-MRK-001](cases/RT-MRK-001.md) | Пометка строк: булавка, Space, групповое переключение | P1 | Функциональный |
| [RT-MRK-002](cases/RT-MRK-002.md) | Метки при перечитывании и визуальная точка | P2 | Регрессия дефекта |
| [RT-NAV-001](cases/RT-NAV-001.md) | F3/Shift+F3: навигация, wrap-around, центрирование | P1 | Функциональный |
| [RT-NAV-002](cases/RT-NAV-002.md) | Клавиатурная навигация и выделение | P2 | Функциональный |
| [RT-NAV-003](cases/RT-NAV-003.md) | Горизонтальная прокрутка | P2 | Регрессия дефекта |
| [RT-PRF-001](cases/RT-PRF-001.md) | Переключение профилей и сохранение меток | P1 | Функциональный |
| [RT-PRF-002](cases/RT-PRF-002.md) | Управление профилями: New, Copy, Remove, переименование | P1 | Функциональный |
| [RT-PRF-003](cases/RT-PRF-003.md) | Неприменимый профиль и legacy-конфигурации | P1 | Негативный |
| [RT-SAV-001](cases/RT-SAV-001.md) | Сохранение и применение сохранённого поиска | P1 | Функциональный |
| [RT-SAV-002](cases/RT-SAV-002.md) | Окно Manage saved searches: создание, дублирование, удаление | P1 | Функциональный |
| [RT-SAV-003](cases/RT-SAV-003.md) | Оформление окна сохранённых поисков | P2 | Регрессия дефекта |
| [RT-SET-001](cases/RT-SET-001.md) | Окно настроек: вкладки, Save и Cancel | P1 | Функциональный |
| [RT-SET-002](cases/RT-SET-002.md) | Горячая перезагрузка appsettings.yaml | P1 | Функциональный |
| [RT-SET-003](cases/RT-SET-003.md) | Некорректный YAML | P1 | Негативный |
| [RT-SET-004](cases/RT-SET-004.md) | Автоисправление настроек | P1 | Регрессия дефекта |
| [RT-SRCH-001](cases/RT-SRCH-001.md) | Базовый поиск, счётчик и Match case | P1 | Функциональный |
| [RT-SRCH-002](cases/RT-SRCH-002.md) | Regex-поиск и невалидное выражение | P1 | Функциональный |
| [RT-SRCH-003](cases/RT-SRCH-003.md) | Буквальный поиск спецсимволов regex | P2 | Краевой |
| [RT-SRCH-004](cases/RT-SRCH-004.md) | Поиск в строках продолжения и Unicode | P2 | Функциональный |
| [RT-SRCH-005](cases/RT-SRCH-005.md) | Несколько вкладок поиска | P2 | Функциональный |
| [RT-SRCH-006](cases/RT-SRCH-006.md) | Автодополнение истории поиска | P3 | Функциональный |
| [RT-SRCH-007](cases/RT-SRCH-007.md) | Очистка поиска: Esc и Alt+X | P2 | Функциональный |
| [RT-SRCH-008](cases/RT-SRCH-008.md) | 5000 совпадений | P2 | Нефункциональный |
| [RT-SRCH-009](cases/RT-SRCH-009.md) | Множественный выбор результатов поиска | P2 | Регрессия дефекта |
| [RT-STK-001](cases/RT-STK-001.md) | Call stack: распознавание и ошибки без символов | P1 | Функциональный |
| [RT-STK-002](cases/RT-STK-002.md) | Call stack: разрешение реальных символов Microsoft | P2 | Функциональный |
| [RT-STK-003](cases/RT-STK-003.md) | Call stack: исходники и Allow source server commands | P3 | Функциональный |
| [RT-SUP-001](cases/RT-SUP-001.md) | Окно Support | P2 | Функциональный |
| [RT-THM-001](cases/RT-THM-001.md) | Темы и акцентный цвет | P2 | Функциональный |
| [RT-THR-001](cases/RT-THR-001.md) | Группировка по потокам и разделители | P1 | Функциональный |
| [RT-THR-002](cases/RT-THR-002.md) | Фокус на потоке: приглушение и фильтр поиска | P1 | Регрессия дефекта |
| [RT-TML-001](cases/RT-TML-001.md) | Таймлайн: выбор диапазона времени | P1 | Функциональный |
| [RT-TML-002](cases/RT-TML-002.md) | Подсказка времени при наведении на таймлайн | P2 | Регрессия дефекта |
| [RT-TML-003](cases/RT-TML-003.md) | Переход через полночь и малые откаты времени | P1 | Краевой |
| [RT-TML-004](cases/RT-TML-004.md) | Немонотонное время с датой и граничные файлы | P2 | Краевой |
| [RT-TML-005](cases/RT-TML-005.md) | Положение и видимость таймлайна, сохранение настройки | P2 | Функциональный |
| [RT-TRN-001](cases/RT-TRN-001.md) | Transformations: Base64Decode | P2 | Функциональный |
| [RT-UPD-001](cases/RT-UPD-001.md) | Проверка обновлений: режимы и расписание | P1 | Регрессия дефекта |
| [RT-UPD-002](cases/RT-UPD-002.md) | Диалог обновления: Update, Later, Skip this version | P1 | Регрессия дефекта |
| [RT-UPD-003](cases/RT-UPD-003.md) | Индикатор обновления в заголовке, install-режим и портативная сборка | P2 | Регрессия дефекта |
| [RT-VM-001](cases/RT-VM-001.md) | Маппинг значений: токены, регистр, поля, приоритеты | P1 | Регрессия дефекта |
| [RT-VM-002](cases/RT-VM-002.md) | Маппинг в JSON/XML и независимость поиска/фильтров | P1 | Краевой |
| [RT-VM-003](cases/RT-VM-003.md) | Импорт/экспорт и ошибки маппинга | P2 | Негативный |
| [RT-XOR-001](cases/RT-XOR-001.md) | XOR-маскированные логи | P1 | Функциональный |
| [RT-ZM-001](cases/RT-ZM-001.md) | Масштаб текста: сочетания клавиш, меню, границы | P1 | Функциональный |
