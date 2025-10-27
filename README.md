# BlazorWasmDotNet8AspNetCoreHosted

## Огляд

Це рішення демонструє повноцінну систему планування модулів: клієнт на Blazor WebAssembly (`Client`), серверне ASP.NET Core API (`Server`) та спільну бібліотеку DTO (`Shared`). Цільова платформа — .NET 8. Для зберігання даних використовується MySQL 8.x із провайдером Pomelo.EntityFrameworkCore.MySql. У середовищі розробки доступний Swagger для дослідження API.

## Структура рішення

- `BlazorWasmDotNet8AspNetCoreHosted.Client` — вебклієнт Blazor WASM, Razor-компоненти та клієнтські сервіси.
- `BlazorWasmDotNet8AspNetCoreHosted.Server` — ASP.NET Core хост, контролери, DI-налаштування, контекст EF Core та міграції.
- `BlazorWasmDotNet8AspNetCoreHosted.Shared` — спільні DTO й контракти, що використовуються клієнтом і сервером.
- `database-setup.md` для детального налаштування бази даних.

## Попередні вимоги

- Встановлений .NET SDK 8.0.100 або новіший (`dotnet --list-sdks`).
- Доступ до локального екземпляра MySQL 8.x (або сумісного керованого сервісу).
- Node.js/npm знадобиться лише за умови зміни фронтенд-інструментів; для запуску поточного рішення не є обов’язковим.

## Швидкий старт

1. Клонуйте репозиторій.
2. Перевірте, що рішення коректно збирається:
   ```
   dotnet build BlazorWasmDotNet8AspNetCoreHosted.Server.sln
   ```
3. Налаштуйте рядок підключення до бази даних (розділ нижче).
4. Застосуйте міграції:
   ```
   dotnet ef database update --project BlazorWasmDotNet8AspNetCoreHosted.Server
   ```
5. Запустіть серверну частину:
   ```
   dotnet run --project BlazorWasmDotNet8AspNetCoreHosted.Server
   ```

## Налаштування бази даних

Реальні облікові дані навмисно не зберігаються в репозиторії. Перед запуском підставте валідний рядок підключення, скориставшись одним із способів:

- **Secret Manager (рекомендований локально):**
  ```
  cd BlazorWasmDotNet8AspNetCoreHosted.Server
  dotnet user-secrets init
  dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Port=3306;Database=schedule_db;User=schedule_user;Password=YOUR_PASSWORD;CharSet=utf8mb4;TreatTinyAsBoolean=true;AllowPublicKeyRetrieval=True;SslMode=None"
  ```
- **Змінна середовища:**
  ```
  setx ConnectionStrings__Default "Server=localhost;Port=3306;Database=schedule_db;User=schedule_user;Password=YOUR_PASSWORD;CharSet=utf8mb4;TreatTinyAsBoolean=true;AllowPublicKeyRetrieval=True;SslMode=None"
  ```

Детальні покрокові інструкції зосереджені у `database-setup.md`.

> **Застереження:** наявні міграції створюють лише структуру таблиць. Початкові дані не додаються автоматично. Якщо Вам потрібні довідники або демонстраційні значення, реалізуйте сидинг окремо.

## Робота з Entity Framework Core

- Додати міграцію:
  ```
  dotnet ef migrations add <MigrationName> --project BlazorWasmDotNet8AspNetCoreHosted.Server
  ```
- Оновити базу даних:
  ```
  dotnet ef database update --project BlazorWasmDotNet8AspNetCoreHosted.Server
  ```
- Скасувати останню міграцію (якщо вона ще не застосована до бази):
  ```
  dotnet ef migrations remove --project BlazorWasmDotNet8AspNetCoreHosted.Server
  ```

Design-time фабрика `DesignTimeDbContextFactory` зчитує `ConnectionStrings:Default` із шарів конфігурації (`appsettings*.json`, Secret Manager, змінні середовища). Перед запуском інструментів EF переконайтеся, що рядок доступний.

## Запуск окремих проєктів

- **Сервер:** `dotnet run --project BlazorWasmDotNet8AspNetCoreHosted.Server`
- **Клієнт:** хоститься сервером, тому додатковий запуск не потрібен.
- **Публікація:** `dotnet publish BlazorWasmDotNet8AspNetCoreHosted.Server -c Release`

## Усунення несправностей

- **`Unable to connect to any of the specified MySQL hosts`** — перевірте, чи запущено MySQL та чи правильні хост і порт у рядку підключення.
- **`Connection string 'Default' is not configured`** — додайте рядок через Secret Manager, змінні середовища або локальний конфіг перед запуском EF чи сервера.
- **CORS або 404 для API** — впевніться, що сервер активний, а клієнт звертається до актуального базового URL.