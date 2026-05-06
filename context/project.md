# Проект

- Название: autorecord.
- Цель: Windows-приложение для автоматической записи микрофона и системного звука во время встреч из Яндекс.Календаря.
- Стек: .NET WPF, NAudio, iCal-ссылка Яндекс.Календаря, Windows Task Scheduler.
- Текущая структура:
  - `context/` — проектный контекст.
  - `docs/superpowers/specs/2026-05-06-autorecord-design.md` — утвержденная дизайн-спека MVP.
  - `docs/superpowers/plans/2026-05-06-autorecord-mvp.md` — план реализации MVP.
  - `.worktrees/mvp/` — рабочее дерево реализации на ветке `feature/mvp`.
  - `.worktrees/mvp/src/Autorecord.Core/` — core-логика календаря, расписания, записи, настроек и автозапуска.
  - `.worktrees/mvp/src/Autorecord.App/` — WPF GUI, tray, уведомления и app host.
  - `.worktrees/mvp/tests/Autorecord.Core.Tests/` — автоматические тесты core-логики.
