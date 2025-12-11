# КПО ДЗ №3

Система приёма домашних работ: загрузка файла, регистрация сдачи, проверка на плагиат и генерация облака слов через QuickChart.

---

## Стек
- .NET 8 (Minimal API)
- EF Core + SQLite (FileAnalysis)
- Docker / docker-compose
- QuickChart WordCloud API

---

## Архитектура

Система состоит из 3 сервисов:

1) **Gateway** — единая точка входа для клиента.
- Принимает сдачу (`/submit`) и оркестрирует вызовы остальных сервисов.
- Проксирует часть API (получение отчётов/облака слов/файлов), чтобы клиенту хватало одного адреса.

2) **FileStorage** — хранение файлов на диске.
- При загрузке вычисляет **MD5-хэш содержимого** и преобразует его в `Guid`, это `fileId`.
- Сохраняет файл в `Store/<fileId>` и метаданные в `Store/<fileId>.meta.json`.

3) **FileAnalysis** — хранение сдач и результатов анализа (SQLite).
- Сохраняет информацию о сдаче (кто/группа/задание/когда/`fileId`).
- Создаёт `AnalysisReport` и предоставляет отчёты/агрегацию по заданию.
- Для облака слов скачивает текст из FileStorage и отправляет его в QuickChart.

---

## Запуск

В корне репозитория:

```bash
docker compose up --build
```

После запуска:

- Gateway Swagger: `http://localhost:5260/swagger`
- FileStorage Swagger: `http://localhost:5070/swagger`
- FileAnalysis Swagger: `http://localhost:5170/swagger`

### Порты
- **Gateway**: `5260 => 8080`
- **FileStorage**: `5070 => 8080`
- **FileAnalysis**: `5170 => 8080`

---

## Взаимодействие сервисов

### 1) Сдача работы
1. Клиент отправляет `POST /submit` в **Gateway** (multipart/form-data).
2. Gateway загружает файл в **FileStorage**: `POST /files` получает `fileId`.
3. Gateway регистрирует сдачу в **FileAnalysis**: `POST /submissions` (JSON с `studentName`, `group`, `assignment`, `fileId`).
4. FileAnalysis сохраняет сдачу в SQLite и формирует `AnalysisReport`.
5. Gateway возвращает клиенту JSON-ответ (как пришёл из FileAnalysis).

### 2) Просмотр результатов
- `GET /submissions/{id}` — данные сдачи (с опциональным `Report`).
- `GET /submissions/{id}/report` — чистый `AnalysisReport`.
- `GET /assignments/{assignment}/reports` — агрегация по заданию: количество сдач, сколько помечены как плагиат, средняя похожесть, список элементов.

### 3) Скачивание файла
- `GET /files/{fileId}` (через Gateway)  проксирование на FileStorage.

### 4) Облако слов
- `GET /submissions/{id}/wordcloud` (через Gateway) FileAnalysis:
  1) скачивает файл из FileStorage,
  2) читает текст (ограничение: первые 10 000 символов),
  3) отправляет конфиг в `https://quickchart.io/wordcloud`,
  4) возвращает PNG (`image/png`).

---

## Алгоритм плагиата (как реализовано)

1) **FileStorage** делает `fileId` детерминированным: `fileId = Guid(MD5(fileBytes))`.
2) **FileAnalysis** при создании сдачи ищет **другие** сдачи, где:
- `Assignment` совпадает,
- `FileId` совпадает,
- `SubmissionId` отличается (исключаем текущую сдачу).

Если совпадения есть:
- `IsPlagiarized = true`
- `Similarity = 100.0`
- `Comment = "Обнаружено совпадение файла с работами: ..."`

Если совпадений нет:
- `IsPlagiarized = false`
- `Similarity = 0.0`
- `Comment = "Совпадений по этому файлу не найдено"`


---

## API (через Gateway)

### POST `/submit`
**Content-Type:** `multipart/form-data`

Поля формы:
- `file` — файл (обязательно)
- `studentName` — имя студента (обязательно)
- `assignment` — название задания (обязательно)
- `group` — группа (опционально)



### Остальные ручки Gateway
- `GET /submissions/{id}`
- `GET /submissions/{id}/report`
- `GET /submissions/{id}/wordcloud` возвращает `image/png`
- `GET /files/{fileId}`
- `GET /assignments/{assignment}/reports`

---

## Устойчивость к падению микросервисов

- Gateway оборачивает обращения к FileStorage/FileAnalysis в `try/catch`.
- Если сервис недоступен (ошибка сети), Gateway возвращает **502 Bad Gateway** и `ProblemDetails`.

---



## Что можно показывать в Swagger

1) `POST /submit` (через Gateway)
2) `GET /submissions/{id}`
3) `GET /submissions/{id}/report`
4) `GET /submissions/{id}/wordcloud`
5) `GET /assignments/{assignment}/reports`
6) `GET /files/{fileId}`
