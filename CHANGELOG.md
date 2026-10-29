# Changelog

All notable changes to ChildLock are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

## 0.1.0 - 2026-10-29

- First release of ChildLock: lock the keyboard and mouse with a hotkey (default Ctrl+Alt+L), the tray menu, or automatically after a set idle time.
- Blocks every key (including Win, Alt+Tab, Alt+F4, media and volume keys) and all mouse and touchpad movement, clicks and scrolling. In testing, 0 of 166 synthesized key and mouse events got through while locked.
- Unlock by typing a secret word (default `unlock`, not case-sensitive). 1,000,000 random key presses never unlocked it.
- Countdown before locking (default 3 seconds, 0 to 10), cancelled with Esc or the hotkey.
- Small click-through "Locked" badge in the top-right corner; the screen otherwise stays as it is and the PC stays awake while locked.
- Ctrl+Alt+Del or Windows locking always unlocks ChildLock, so you can never get stuck.
- Runs with admin rights so input to admin windows is blocked too; optional start at sign-in through Task Scheduler with no admin prompt.
- Settings window that follows the Windows light or dark theme; running ChildLock again opens it.
- Optional lock and unlock chime, pause hotkey, and a reminder of the unlock word in the tray tooltip.
- One 254 KB exe, about 11 MB of private memory, no measurable CPU when idle, and about 6 microseconds per input event while locked.
