# Задачи

- Выполнено: создана папка `context/` и файлы `project.md`, `decisions.md`, `tasks.md`, `session.md`.
- Выполнено: согласован и записан дизайн MVP в `docs/superpowers/specs/2026-05-06-autorecord-design.md`.
- Выполнено: подготовлен implementation plan в `docs/superpowers/plans/2026-05-06-autorecord-mvp.md`.
- Выполнено: создан git-репозиторий и worktree `.worktrees/mvp` на ветке `feature/mvp`.
- Выполнено: реализован MVP Windows WPF-приложения по плану.
- Выполнено: свежая проверка `dotnet test Autorecord.sln` — 53/53 passed.
- Выполнено: свежая проверка `dotnet build Autorecord.sln -c Release` — 0 warnings, 0 errors.
- Выполнено: smoke run `--minimized` стартовал без мгновенного падения; процесс затем остановлен.
- Выполнено: исправлена ошибка ручного обновления календаря до сохранения настроек (`fbfb519`).
- Выполнено: после исправления `dotnet build Autorecord.sln` — 0 warnings, 0 errors; `dotnet test Autorecord.sln` — 53/53 passed.
- Текущие задачи:
  - Повторная ручная проверка с новой/актуальной iCal-ссылкой.
  - Ручная проверка записи микрофона и системного вывода в один WAV.
  - Ручная проверка stop-dialog после тишины и сценария `Нет`.
  - Ручная проверка Windows Task Scheduler автозапуска после входа в систему.
- Следующий шаг: запустить приложение из `.worktrees/mvp`, ввести реальные настройки и выполнить ручную проверку.
