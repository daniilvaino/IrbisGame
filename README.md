# Дело «Ирбис» — интерактивный учебник KQL

Локальная веб-игра для изучения Kusto Query Language без Docker и облака. Вы аналитик центра мониторинга
вымышленной компании «Стеклянный Маяк»: с файлового сервера утекли чертежи, и 30 уровней KQL-запросов
шаг за шагом раскрывают, кто это сделал.

Запросы выполняют настоящие in-process движки KQL: **BabyKusto** и **KustoLoco**. Разбор текста, диагностика
и подсказки — официальный парсер `Microsoft.Azure.Kusto.Language`. Своего парсера или интерпретатора KQL в проекте нет,
неподдерживаемые операторы не эмулируются: если движок чего-то не умеет, вы видите его ошибку как есть.

## Запуск

Нужны **.NET SDK 9** (или новее) и **Node.js 18+** (для сборки фронтенда).

```
dotnet run --project src/KustoGame.Server
```

Откройте http://localhost:5080. Первая сборка сама выполнит `npm install` и `npm run build` в `src/KustoGame.Web`
(около минуты); дальше фронтенд пересобирается, только если изменились его исходники. Один процесс раздаёт и API, и статику.

Разработка фронтенда с горячей перезагрузкой: запустите сервер как выше, затем `npm run dev` в `src/KustoGame.Web`
и откройте http://localhost:5173 (запросы `/api` проксируются на 5080).

### Почему .NET 9, а не 8

ТЗ просило ASP.NET Core 8, но KustoLoco собирается под net8 только до версии 1.1.4. В ней нет `has`, `in`, `between`
для real, `mv-expand` и `arg_max`, и половина глав была бы недоступна на обоих движках. Поэтому сервер собран под
`net9.0` с **KustoLoco.Core 1.4.9**. Код — тот же minimal API.

### Как уживаются два движка

Оба движка зависят от `Microsoft.Azure.Kusto.Language`, но разных версий: BabyKusto 0.1.8 собран под 11.3, KustoLoco 1.4.9 —
под 12.4. С новой версией BabyKusto ломается молча: `sort by … desc` падает с «Unexpected ordering kind», а
`$left/$right` в `join` не распознаётся, потому что сдвинулись значения `SyntaxKind`. Поэтому BabyKusto загружается
в отдельный `AssemblyLoadContext` со своей копией Kusto.Language 11.3 (`src/KustoGame.Engines.BabyKusto`, загрузчик —
`Engines/IsolatedEngineLoader.cs`). С хостом его связывает только сборка контрактов без типов Kusto.Language.
Тест `BabyKusto_runs_against_its_own_Kusto_Language` следит, чтобы изоляция не потерялась.

Оба движка грузятся при старте с одинаковыми данными, примерно за 0,1–0,3 с каждый.

## Переключение движка

* **Конфигом** (движок по умолчанию для всех): `src/KustoGame.Server/appsettings.json` → `"Engine": "KustoLoco"`,
  либо `dotnet run --project src/KustoGame.Server -- --Engine KustoLoco`, либо переменная окружения `Engine=KustoLoco`.
* **В интерфейсе**: выпадающий список «Движок» в шапке. Выбор хранится в браузере и не трогает конфиг.

`GET /api/engine` возвращает движок по умолчанию и состояние обоих движков. Во вкладке «Движки» есть таблица поддержки,
а в песочнице кнопка «⇄ Сравнить движки» выполняет один запрос на обоих движках и показывает результаты рядом.

| Настройка (`appsettings.json`) | По умолчанию | Смысл |
|---|---|---|
| `Engine` | `BabyKusto` | движок по умолчанию: `BabyKusto` или `KustoLoco` |
| `QueryTimeoutSeconds` | `10` | таймаут запроса |
| `MaxRows` | `500` | строк в ответе (`totalRows` считается полностью) |
| `LevelsPath` | пусто | папка уровней; по умолчанию `levels/` репозитория |
| `WorldDate` | пусто | дата привязки мира `yyyy-MM-dd`; по умолчанию сегодня (UTC) |
| `Urls` | `http://localhost:5080` | адрес сервера |

## Что умеет интерфейс

* **Редактор** Monaco + `@kusto/monaco-kusto`: подсветка KQL, автодополнение таблиц, колонок, операторов и функций
  (схема приходит с `GET /api/schema`), подчёркивание синтаксических и семантических ошибок до запуска, подсказки
  сигнатур функций при наборе `(` и `,`, запуск по **Ctrl+Enter**. В monaco-kusto нет signature help: подсказки строит сервер
  по синтаксическому дереву и символам Kusto.Language (`POST /api/signature`).
* **Результаты**: типы колонок в заголовке, счётчик строк, время выполнения, сортировка по клику.
* **Пошагово**: под редактором лента стадий конвейера (`T` → `T | where …` → …) с изменением числа строк «было → стало».
  Клик по стадии показывает её промежуточный результат и подсвечивает оператор в редакторе.
* **Схема**: таблицы, колонки, типы и описания. Клик вставляет имя в редактор, 👁 показывает первые 20 строк.
* **Ошибка движка** показывается дословно, с позицией в запросе. Сообщение вида
  `Kusto.Language.Syntax.TopOperator` или `Function hourofday(...)` сопоставляется с узлом синтаксического дерева,
  и место подчёркивается в редакторе.
* **Проверка ответа**: что лишнее, чего не хватает, каких колонок нет. После прохождения ваш запрос и эталон
  с результатами видны рядом.
* **Прогресс**: очки (каждая подсказка −25% от базовых очков уровня, минимум 25%; «сдаться» — 0), хранятся в
  `localStorage`, кнопки «Экспорт» и «Импорт» сохраняют и загружают JSON.

## API

| Метод | Описание |
|---|---|
| `POST /api/query` `{query, engine?}` | `{engine, columns, rows, totalRows, truncated, elapsedMs, error, diagnostics}`; таймаут 10 с, не больше 500 строк; управляющие команды (`.show`, `.drop`…) → 400 |
| `POST /api/query/steps` `{query, engine?}` | результат каждого префикса конвейера; разбиение по `PipeExpression` синтаксического дерева |
| `POST /api/check` `{levelId, query, engine?}` | `{passed, message, diff, error, solution}`; эталон выполняется тем же движком |
| `POST /api/levels/{id}/reveal` | эталон уровня («сдаться») |
| `GET /api/levels` | главы и уровни; плейсхолдеры дат подставлены, эталоны не отдаются |
| `GET /api/schema` | таблицы, колонки, типы, описания |
| `GET /api/tables/{name}/preview` | `T \| take 20` через движок |
| `GET /api/engine`, `GET /api/engine/matrix` | движок по умолчанию и состояние; таблица поддержки |
| `POST /api/signature` `{query, offset}`, `POST /api/diagnostics` | подсказка сигнатуры, диагностика Kusto.Language |

Параметр `engine` у запросов необязателен: без него используется движок из конфига.

## Таблица поддержки операторов

Получена тест-матрицей `src/KustoGame.Server/Matrix/OperatorMatrix.cs` на маленьком наборе данных с заранее известными
ответами. ✅ — результат верный, ⚠️ — запрос выполнился, но ответ неверный, ❌ — движок вернул ошибку.
Пересобрать таблицу: `dotnet run --project src/KustoGame.Server -- --matrix`. Та же матрица отображается во вкладке «Движки».

| Возможность | Пример | BabyKusto 0.1.8 | KustoLoco 1.4.9 |
|---|---|---|---|
| take | `M \| take 2 \| count` | ✅ | ✅ |
| project | `M \| where Id == 1 \| project Id, Name` | ✅ | ✅ |
| project-away / project-rename | `M \| where Id == 1 \| project-away Props, Raw, Ts, Flag \| project-rename Title = Name \| project Id, Title` | ✅ | ✅ |
| where (and, ==, >) | `M \| where Value > 2 and Category == "A" \| project Id` | ✅ | ✅ |
| sort by | `M \| sort by Value desc \| take 2 \| project Id` | ✅ | ✅ |
| top | `M \| top 2 by Value asc \| project Id` | ❌ | ✅ |
| distinct | `M \| distinct Category` | ✅ | ✅ |
| count | `M \| count` | ✅ | ✅ |
| summarize count() by | `M \| summarize C = count() by Category` | ✅ | ✅ |
| summarize sum/avg/min/max | `M \| summarize S = sum(Value), A = avg(Value), Mn = min(Value), Mx = max(Value)` | ✅ | ✅ |
| dcount | `M \| summarize dcount(Category)` | ✅ | ✅ |
| countif | `M \| summarize countif(Flag)` | ✅ | ✅ |
| arg_max | `M \| summarize arg_max(Value, Id) by Category \| project Category, Id` | ❌ | ✅ |
| make_set | `M \| summarize S = make_set(Category) \| project L = array_length(S)` | ✅ | ✅ |
| bin() | `M \| summarize C = count() by D = bin(Ts, 1d)` | ✅ | ✅ |
| has | `M \| where Name has "two" \| project Id` | ❌ | ✅ |
| has (без учёта регистра) | `M \| where Name has "GAMMA" \| project Id` | ❌ | ✅ |
| has (кириллица) | `M \| where Name has "пароль" \| project Id` | ❌ | ✅ |
| !has | `M \| where Name !has "two" \| count` | ❌ | ✅ |
| contains | `M \| where Name contains "lph" \| project Id` | ✅ | ✅ |
| startswith | `M \| where Name startswith "del" \| project Id` | ✅ | ✅ |
| endswith | `M \| where Name endswith "three" \| project Id` | ✅ | ✅ |
| in (...) | `M \| where Category in ("A", "C") \| count` | ❌ | ✅ |
| matches regex | `M \| where Name matches regex @"^[a-z]+-[a-z]+$" \| project Id` | ✅ | ✅ |
| parse | `M \| where Id == 4 \| parse Name with First "-" Second \| project First, Second` | ❌ | ❌ |
| extract() | `M \| where Id == 4 \| project X = extract(@"^(\w+)-(\w+)$", 2, Name)` | ✅ | ✅ |
| split() | `M \| where Id == 4 \| project P = tostring(split(Name, "-")[1])` | ❌ | ✅ |
| =~ / != | `M \| where Category =~ "a" and Id != 1 \| project Id` | ❌ | ✅ |
| !contains | `M \| where Name !contains "a" \| count` | ✅ | ✅ |
| has_any | `M \| where Name has_any ("two", "three") \| count` | ❌ | ✅ |
| extend | `M \| where Id == 1 \| extend Double = Value * 2 \| project Double` | ✅ | ✅ |
| case() | `M \| extend Size = case(Value < 2, "S", Value < 5, "M", "L") \| summarize count() by Size` | ❌ | ✅ |
| iff() | `M \| extend F = iff(Flag, "yes", "no") \| summarize count() by F` | ✅ | ✅ |
| ago() / now() | `print R = ago(1d) < now()` | ✅ | ✅ |
| between (числа) | `M \| where Value between (2.0 .. 4.5) \| project Id` | ❌ | ✅ |
| between (datetime) | `M \| where Ts between (datetime(2024-01-01) .. datetime(2024-01-02)) \| project Id` | ❌ | ✅ |
| datetime - datetime | `M \| where Id == 4 \| project D = Ts - datetime(2024-01-01)` | ✅ | ✅ |
| datetime + timespan | `print T = datetime(2024-01-01) + 36h` | ✅ | ✅ |
| hourofday() | `M \| where Id == 2 \| project H = hourofday(Ts)` | ❌ | ❌ |
| startofday() | `M \| where Id == 4 \| project D = startofday(Ts)` | ✅ | ✅ |
| datetime_part() | `M \| where Id == 2 \| project H = datetime_part("hour", Ts)` | ❌ | ✅ |
| dayofweek() | `M \| where Id == 4 \| project D = dayofweek(Ts)` | ✅ | ✅ |
| bin() по часу | `M \| where Ts < datetime(2024-01-02) \| summarize C = count() by H = bin(Ts, 1h)` | ✅ | ✅ |
| format_datetime() | `M \| where Id == 4 \| project F = format_datetime(Ts, "yyyy-MM-dd HH:mm")` | ❌ | ✅ |
| let | `let x = 2; M \| where Id == x \| project Id` | ✅ | ✅ |
| join kind=inner | `M \| join kind=inner N on Id \| project Id, Label` | ✅ | ✅ |
| join kind=leftouter | `M \| join kind=leftouter N on Id \| project Id, Label` | ✅ | ✅ |
| join kind=leftanti | `M \| join kind=leftanti N on Id \| project Id` | ✅ | ✅ |
| join kind=rightanti | `M \| join kind=rightanti N on Id \| project Id` | ✅ | ✅ |
| join (подзапрос, $left/$right) | `M \| join kind=inner (N \| project Key = Id, Label) on $left.Id == $right.Key \| project Id, Label` | ✅ | ✅ |
| union | `union M, N \| count` | ✅ | ✅ |
| union (подзапросы) | `union (M \| project Id), (N \| project Id) \| count` | ✅ | ✅ |
| parse_json | `M \| where Id == 1 \| extend J = parse_json(Raw) \| project Ip = tostring(J.ip)` | ⚠️ неверный результат | ✅ |
| todynamic | `M \| where Id == 2 \| extend J = todynamic(Raw) \| project N = toint(J.n)` | ❌ | ❌ |
| dynamic: точка | `M \| where Id == 1 \| project K = tostring(Props.k)` | ⚠️ неверный результат | ✅ |
| dynamic: ["key"] | `M \| where Id == 1 \| project K = tostring(Props["k"])` | ⚠️ неверный результат | ✅ |
| dynamic: вложенность | `M \| where Id == 5 \| project V = tostring(Props.inner.v)` | ⚠️ неверный результат | ✅ |
| array_length() | `M \| where Id == 4 \| project L = array_length(Props.arr)` | ✅ | ✅ |
| mv-expand | `M \| mv-expand A = Props.arr \| count` | ❌ | ✅ |
| mv-expand + to typeof | `M \| where Id == 4 \| mv-expand A = Props.arr to typeof(long) \| summarize S = sum(A)` | ❌ | ✅ |
| tostring / tolong | `M \| where Id == 3 \| project S = tostring(Id), L = tolong("42")` | ✅ | ✅ |
| strcat / strlen | `M \| where Id == 1 \| project S = strcat(Category, "-", Id), L = strlen(Name)` | ✅ | ✅ |
| isempty / isnotempty | `M \| join kind=leftouter N on Id \| summarize countif(isempty(Label))` | ✅ | ✅ |
| isnotempty после leftouter с подзапросом | `M \| join kind=leftouter (N \| where Id > 0) on Id \| summarize countif(isnotempty(Label))` | ❌ | ✅ |
| != "" после leftouter с подзапросом | `M \| join kind=leftouter (N \| where Id > 0) on Id \| summarize countif(Label != "")` | ✅ | ✅ |

Что важно знать:

* **BabyKusto** не реализует `top`, семейство `has`, `in`, `between`, `case()`, `split()`, `=~`, `mv-expand`, `arg_max`,
  `datetime_part`/`format_datetime`/`hourofday`, `todynamic`. Его `tostring()` от dynamic-значения возвращает строку
  вместе с JSON-кавычками (`"x"` вместо `x`), поэтому сравнения со свойствами JSON на нём не работают.
  После `join kind=leftouter` с подзапросом правые колонки становятся dynamic, и `isnotempty` падает.
* **KustoLoco** не реализует оператор `parse`, функции `hourofday()` и `todynamic()`. `between` с real-колонкой требует
  real-границ (`5000.0`, а не `5000`).
* Ни один движок не поддерживает `parse`, поэтому глава 3 учит `extract()`, а в теории объяснено, что такое `parse`.
  Вместо `hourofday()` используется `Timestamp - startofday(Timestamp) < 6h`, вместо `todynamic()` — `parse_json()`.

## Уровни и движки

7 глав, 30 уровней. На BabyKusto доступно 22 уровня, на KustoLoco — все 30. Недоступные на текущем движке уровни видны
в списке с пометкой «нужен KustoLoco», их содержимое скрыто до переключения.

| Глава | Уровни | Только KustoLoco |
|---|---|---|
| 1. Первый день в SOC | take, project, where, sort, top | top |
| 2. Считаем | count, summarize … by, sum/avg/dcount, bin() | — |
| 3. Ищем по тексту | has, contains, startswith, matches regex, extract() | has |
| 4. Вычисления и время | extend, case(), iff(), ago(), between, datetime-арифметика | case(), between |
| 5. Связываем таблицы | join inner, leftouter, leftanti, union | — |
| 6. Динамические данные | parse_json, свойства dynamic, mv-expand | все три |
| 7. Дело «Ирбис» | три шага расследования | шаг 3 (свойства dynamic) |

Пометки `engines:` в уровнях не ставятся вручную наугад, их проверяют тесты: эталон должен давать непустой результат
на каждом указанном движке, а на неуказанном — падать или давать другой результат. Иначе тест требует открыть уровень
и для этого движка.

## Данные

Пять таблиц, детерминированный генератор (`World/WorldGenerator.cs`, seed `20240314`):

| Таблица | Строк | Что внутри |
|---|---|---|
| `Employees` | 5 000 | справочник; `Profile` — dynamic |
| `NetworkLogs` | 50 000 | прокси и межсетевой экран |
| `EmailEvents` | 20 000 | почтовый шлюз; `Attachments` — dynamic-массив, `Headers` — JSON-строка |
| `BadgeEvents` | ≈43 000 | проходы по пропускам |
| `Transactions` | 15 000 | корпоративные карты; `Details` — dynamic |

Типы колонок: datetime, string, long, real, bool, dynamic. Мир привязан к началу текущих суток UTC и покрывает 30 предыдущих
дней, поэтому `ago()` работает как на живых логах. Содержимое не зависит от даты, сдвигаются только календарные даты.
Улики заложены явно (фишинг за 6 дней до «сегодня», утечка ночью 2 дня назад), IP-адреса взяты из документационных
диапазонов RFC 5737.

## Как добавить уровень

Создайте файл `levels/NN-name.yaml`. Код править не нужно: сервер перечитывает папку при изменении файлов.

```yaml
id: 31-my-level            # уникальный; по умолчанию — имя файла
chapter: 5                 # номер главы из levels/chapters.yaml
order: 5                   # порядок внутри главы
title: Название уровня
topic: join kind=inner     # тег оператора
engines: [BabyKusto, KustoLoco]   # где эталон даёт правильный результат
points: 120
theory: |                  # 3–6 строк; `код` и **жирный** форматируются
  ...
example: |                 # пример, который можно вставить и выполнить одним кликом
  Employees | take 5
task: |
  Задача в терминах сюжета. Даты: {{D-2}} — «два дня назад» (yyyy-MM-dd), {{TODAY}} — дата мира.
hints:                     # 2–3 подсказки, открываются по очереди
  - |-
    ...
solution: |                # эталон; выполняется тем же движком, что и запрос игрока
  Employees | where Floor > 5
check:
  mode: set                # set | ordered | scalar | shape
  columnNames: ignore      # ignore (по умолчанию) | strict
  columnOrder: ignore      # ignore | strict
  extraColumns: deny       # deny (по умолчанию) | allow
```

Режимы проверки:

* `set` сравнивает строки как мультимножество. Колонки сопоставляются по имени, а если имя другое — по совпадающему
  содержимому, поэтому `project N = Name` тоже засчитывается.
* `ordered` учитывает порядок строк.
* `scalar` ждёт одно значение (подойдёт любая ячейка единственной строки).
* `shape` проверяет только колонки и число строк (для `take`).

Числа real сравниваются с точностью 6 значащих цифр. После добавления уровня запустите `dotnet test`: тесты проверят,
что эталон выполняется на указанных движках и проходит собственную проверку, а пример запускается.

## Тесты

```
dotnet test
```

Что проверяется:

* каждый эталон даёт непустой результат на заявленных движках и проходит свою проверку;
* каждый пример выполняется;
* список `engines` не уже, чем нужно;
* управляющие команды (`.show`, `.drop`, `.set-or-append`, `.create`) отклоняются через API на обоих движках;
* стадии конвейера: `|` внутри строк, комментариев и подзапросов `join` не режет запрос, `let` сохраняется в каждом префиксе,
  позиции указывают на операторы;
* сравнение результатов (set, ordered, scalar, shape, лишние и недостающие колонки, переименование);
* детерминированность мира и наличие улик;
* базовые операторы на обоих движках и изоляция BabyKusto;
* подсказки сигнатур.

## Структура

```
src/KustoGame.Server/             ASP.NET Core minimal API (net9.0)
  Engines/                        IKqlEngine-хост, KustoLoco-адаптер, загрузчик изолированного движка
  Analysis/                       Kusto.Language: диагностика, стадии, сигнатуры; выполнение с таймаутом
  Levels/                         загрузка YAML, проверка ответа, сравнение результатов
  World/                          генератор данных и сюжета
  Matrix/                         тест-матрица операторов
  Api/                            эндпоинты
src/KustoGame.Engines.Contracts/  IKqlEngine и нормализация значений (без Kusto.Language)
src/KustoGame.Engines.BabyKusto/  адаптер BabyKusto + Kusto.Language 11.3 (грузится в свой AssemblyLoadContext)
src/KustoGame.Web/                Vite + React + TypeScript + Monaco + @kusto/monaco-kusto
levels/                           главы и уровни (YAML)
tests/KustoGame.Tests/            xUnit
docs/PLAN.md                      исходный план
```

## Ограничения

* Ни один движок не принимает `CancellationToken`. По таймауту клиент сразу получает ошибку, но вычисление
  дорабатывает в фоне.
* Сортировка кликом по заголовку работает с загруженными строками (не больше 500); полный порядок задаётся `sort by`.
* В режиме «Пошагово» у каждой стадии показываются первые 200 строк, счётчик строк при этом полный.
