# Быстрый деплой сейчас: собрать, залить в GHCR, запустить в Pterodactyl

Этот файл — практический чеклист “с нуля до работающего сервера”.

---

## 0. Что уже лежит в папке

В каталоге `gml-pterodactyl` должны быть:

- `Dockerfile`
- `entrypoint.sh`
- `supervisord.conf`
- `supervisor-gml.conf`
- `proxy.appsettings.json`
- `egg-gml-backend-single.json`
- `.github/workflows/build-and-push-ghcr.yml`

---

## 1. Подготовить GitHub репозиторий

1. Создайте новый репозиторий, например `gml-pterodactyl`.
2. Загрузите туда все файлы из этой папки.

Пример через git:

```bash
git init
git add .
git commit -m "Add GML backend single-container pterodactyl stack"
git branch -M main
git remote add origin https://github.com/<YOU>/gml-pterodactyl.git
git push -u origin main
```

---

## 2. Опубликовать Docker image в GHCR (быстрый ручной путь)

### 2.1 Создать PAT в GitHub

Создайте Personal Access Token с правами:

- `write:packages`
- `read:packages`
- `repo` (если репозиторий приватный)

### 2.2 Локальная сборка и push

В папке проекта выполните:

```bash
docker build -t ghcr.io/<YOU>/gml-backend-pterodactyl:latest .
echo <YOUR_GHCR_PAT> | docker login ghcr.io -u <YOU> --password-stdin
docker push ghcr.io/<YOU>/gml-backend-pterodactyl:latest
```

Проверьте, что image появился в GitHub Packages.

---

## 3. Автосборка через GitHub Actions (рекомендуется)

Workflow уже есть:  
`.github/workflows/build-and-push-ghcr.yml`

Что нужно:

1. Запушить репозиторий в GitHub.
2. Убедиться, что у репозитория есть доступ к Packages.
3. Сделать push в `main`/`master` или запуск `workflow_dispatch`.

После выполнения образ появится как:

- `ghcr.io/<owner>/gml-backend-pterodactyl:<tag>`
- `ghcr.io/<owner>/gml-backend-pterodactyl:latest` (для default branch)

---

## 4. Подставить ваш image в egg

Откройте `egg-gml-backend-single.json` и замените:

- `ghcr.io/YOUR_GH_USER_OR_ORG/gml-backend-pterodactyl:latest`

на ваш реальный путь:

- `ghcr.io/<YOU>/gml-backend-pterodactyl:latest`

---

## 5. Импорт egg и создание сервера в Pterodactyl

1. `Admin -> Nests -> Import Egg`  
   Вставьте `egg-gml-backend-single.json`.
2. `Admin -> Servers -> Create New`
3. Выберите этот egg.
4. В Docker image укажите ваш GHCR image.
5. Настройте переменные:
   - обязательно задать `SECURITY_KEY`
   - остальные можно оставить по умолчанию
6. Нажмите `Create Server`.
7. Запустите сервер (`Start`).

---

## 6. Минимальная проверка, что всё работает

В консоли Pterodactyl ожидайте строку:

- `Now listening on: http://0.0.0.0:<port>`

Проверки:

1. Главная:
   - `http://<host>:<allocated-port>/`
2. API через proxy:
   - `http://<host>:<allocated-port>/api/...`
3. Health:
   - `http://<host>:<allocated-port>/health`

---

## 7. Частые вопросы “почему не стартует”

1. Не задан `SECURITY_KEY`  
   -> добавить в переменные сервера.

2. Image не тянется с GHCR  
   -> проверьте тег и права доступа пакета (public/private).

3. Порт недоступен  
   -> проверьте allocation, firewall и NAT.

4. После рестарта пропадают файлы  
   -> убедитесь, что данные пишутся в `/home/container/data/...`.

---

## 8. Что дальше сделать сразу после первого запуска

1. Включить домен и HTTPS перед proxy.
2. Сделать backup политики для `/home/container/data`.
3. Фиксировать релизы тегами (`v1.0.0`, `v1.0.1`) и обновлять сервер на конкретный тег.

---

## 9. Важно про desktop launcher

Этот контейнер не запускает GUI launcher.  
Он дает backend/web API для экосистемы GML. Desktop-клиент собирается отдельно и настраивается на URL вашего backend/proxy.

