# GML Backend в Pterodactyl: полный гайд по созданию сервера

Этот гайд описывает, как импортировать egg, создать сервер и корректно запустить backend/web-инфраструктуру GML в **одном контейнере**:

- `Gml.Web.Api`
- `Gml.Web.Client`
- `Gml.Web.Skin.Service`
- `Gml.Web.Proxy` (публичная точка входа)

Важно: это решение для **backend-части** GML. Desktop GUI launcher в контейнере не запускается.

---

## 1. Что должно быть готово заранее

1. У вас есть доступ к панели Pterodactyl (admin).
2. Docker image уже доступен в GHCR, например:
   - `ghcr.io/<your-org-or-user>/gml-backend-pterodactyl:latest`
3. Файл egg:
   - `egg-gml-backend-single.json`

---

## 2. Импорт egg в Pterodactyl

1. Зайдите в `Admin -> Nests`.
2. Откройте нужный Nest (или создайте новый, например `GML`).
3. Нажмите `Import Egg`.
4. Вставьте содержимое `egg-gml-backend-single.json`.
5. Сохраните.

После импорта проверьте:

- startup: `/opt/gml/entrypoint.sh`
- stop command: `CTRL+C` (в egg это `\u0003`)
- startup done regex:  
  `Now listening on:\s+http://0\.0\.0\.0:\d+`

---

## 3. Создание сервера

1. Откройте `Admin -> Servers -> Create New`.
2. Заполните базовые поля (`Name`, `Owner`, `Node`).
3. На шаге Nest/Egg выберите импортированный egg:
   - `Gml Backend (Single Container)`
4. В `Docker Image` укажите ваш image:
   - `ghcr.io/<your-org-or-user>/gml-backend-pterodactyl:latest`
5. Выберите allocation (порт), который будет использоваться как основной HTTP порт.

Рекомендуемые ресурсы (минимум для старта):

- CPU: от 200%
- RAM: от 2 GB (лучше 3-4 GB)
- Disk: от 8-10 GB

---

## 4. Переменные сервера (обязательно проверить)

В разделе Startup/Variables выставьте:

- `SECURITY_KEY`  
  Уникальный ключ минимум 32 символа.
- `PROJECT_NAME`  
  Например: `GmlBackendPanel`
- `PROJECT_DESCRIPTION`  
  Свободный текст.
- `PROJECT_POLICYNAME`  
  Например: `GmlServerPolicy`
- `PROJECT_PATH`  
  Рекомендуется оставить: `/home/container/data/GmlBackend`
- `SWAGGER_ENABLED`  
  `false` для production.
- `MARKET_ENDPOINT`  
  По умолчанию: `https://gml-market.recloud.tech`
- `TZ`  
  Например: `Europe/Moscow`
- `SERVICE_TEXTURE_ENDPOINT`  
  Оставить: `http://127.0.0.1:8085`
- `PUBLIC_PANEL_PORT`  
  Обычно `8080` (внутри контейнера).
- `SHOP_INTERNAL_URL` *(опционально)*  
  Внутренний URL магазина, например `https://shop.example/internal/player/{uuid}`. Пусто = нули в сайдбаре.
- `SHOP_INTERNAL_KEY` *(опционально)*  
  Секрет заголовка магазина. Не попадает в лаунчер.
- `SHOP_INTERNAL_HEADER`  
  По умолчанию `X-Internal-Key`.

Опционально:

- `S3_ENABLED`
- `MINIO_ROOT_USER`
- `MINIO_ROOT_PASSWORD`

---

## 5. Что с портами и как ходит трафик

Снаружи публикуется только один порт через proxy:

- `Proxy`: `0.0.0.0:${PUBLIC_PANEL_PORT}` (обычно `8080`)

Внутренние сервисы:

- `API`: `127.0.0.1:8082`
- `Frontend`: `127.0.0.1:8081`
- `Skin Service`: `127.0.0.1:8085`
- `Economy`: `127.0.0.1:8086` (`GET /api/v1/users/me`)

То есть пользователь/desktop launcher ходит на один публичный URL proxy.

Чтобы развести **дашборд** и **API** по поддоменам (без правил Pangolin на пути):

- `PUBLIC_PANEL_HOST=launcher.andline.pro`
- `PUBLIC_API_HOST=api.andline.pro`

В Pangolin два ресурса на **один** newt и один порт `127.0.0.1:${PUBLIC_PANEL_PORT}`:

| Ресурс | Хост | Куда |
|---|---|---|
| Дашборд | `launcher.andline.pro` | панель + `/api` (браузер бьёт в тот же хост) |
| Лаунчер | `api.andline.pro` | `/api`, `/ws`, `/skins`, файлы |

Desktop launcher при сборке: Host = `https://api.andline.pro` (без `/` в конце). Старый exe с Host `https://launcher.andline.pro` нужно пересобрать.

---

## 6. Данные и персистентность

Все данные хранятся в `/home/container`:

- `/home/container/data/GmlBackend`
- `/home/container/data/backups`
- `/home/container/data/TextureService`
- `/home/container/data/database`

`entrypoint.sh` подготавливает каталоги и symlink’и автоматически при запуске.

---

## 7. Первый запуск и проверка

1. Нажмите `Start`.
2. В консоли дождитесь строки вида:
   - `Now listening on: http://0.0.0.0:<port>`
3. Проверьте снаружи:
   - `http://<server-ip-or-domain>:<external-allocated-port>/`
4. Проверка API:
   - `http://<server-ip-or-domain>:<external-allocated-port>/api/v1/...`

Если включили Swagger:

- `http://<server-ip-or-domain>:<external-allocated-port>/swagger`

---

## 8. Как подключить desktop launcher

Этот сервер обслуживает backend/web stack.  
Desktop `Gml.Launcher` собирается отдельно и в его конфиге указываются:

- Host API = публичный API (`https://api.andline.pro` при split, иначе URL proxy)
- FolderName = нужное значение проекта

Не запускайте desktop GUI в контейнере Pterodactyl.

---

## 9. Типовые проблемы

1. `SECURITY_KEY is required`  
   Заполните `SECURITY_KEY` в переменных сервера.

2. Сервер запустился, но пустая страница/ошибки API  
   Проверьте, что внешний порт открыт и вы обращаетесь к proxy-порту сервера.

3. После перезапуска потерялись данные  
   Проверьте, что используете стандартный volume `/home/container` и не меняли `PROJECT_PATH` на временный путь.

4. Ошибки image pull  
   Убедитесь, что образ опубликован в GHCR и доступен node Docker daemon (для private пакетов нужен docker login на node).

---

## 10. Рекомендации для production

- Отключить Swagger (`SWAGGER_ENABLED=false`).
- Поставить reverse proxy уровня домена (Cloudflare/Nginx/Caddy) и HTTPS.
- Делать резервные копии `/home/container/data`.
- Обновлять image тегами (`v1.x.x`), а не только `latest`.

