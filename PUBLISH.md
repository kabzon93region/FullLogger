# Publish to GitHub — Full Logger

**Статус:** `ready`  
**GitHub:** Release + zip  
**Версия:** `1.4.1`  
**Deployment:** `(universal)`

## 1. Подготовка (уже сделано этим скриптом)

Папка: `github-repos/FullLogger/`

## 2. Создать репозиторий и запушить

```powershell
cd github-repos/FullLogger
git init
git add .
git commit -m "Source backup Full Logger v1.4.1"
git branch -M main
git remote add origin https://github.com/kabzon93region/FullLogger.git
git push -u origin main
```

Или автоматически:

```powershell
python CURSORAIMODING/tools/publish/publish_github_release.py FullLogger --create-repo
```

## 3. GitHub Release

Прикрепить zip (только игровые файлы, без INSTALL.md):

`\\Servant\data\Games\EscapeFromTarkov4\CURSORAIMODING\releases\FullLogger_(universal)_v1.4.1_2026-06-29.zip`

```powershell
gh release create v1.4.1 "\\Servant\data\Games\EscapeFromTarkov4\CURSORAIMODING\releases\FullLogger_(universal)_v1.4.1_2026-06-29.zip" ^
  --title "Full Logger v1.4.1" ^
  --notes-file CHANGELOG.md
```

## Описание репозитория (suggested)

Полное логирование сессии BepInEx/Unity/игры для отладки модов.

SPT 4.0 + Fika 2.3 headless stack. Deployment: `(universal)`.
