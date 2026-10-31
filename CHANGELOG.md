# Changelog

All notable changes to ChildLock are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

## 0.2.0 - 2026-10-31

New:

- Lock only the keyboard or only the mouse, from the tray (**Lock only**) or as the default for the hotkey and auto-lock. A mouse-only lock ends when you press the hotkey again.
- Hold the lock hotkey for 3 seconds (2 to 10, or off) to unlock without typing. A hotkey still held from locking never counts, and any other key starts the hold over.
- Cleaning mode: every screen goes black with a note that fades away, the keyboard and mouse are locked, and it ends by itself after 60 seconds (0 to 600, or never) by fading the screens back in.
- The badge shows on every monitor and follows monitor changes. It can be hidden (the countdown always shows), and can show a dot for each correct character of the unlock word.
- The unlock word can be a phrase: any length up to 64, letters, digits and spaces. Settings warns about short, common or sequential words instead of refusing them.
- Optional: ask for the unlock word before Settings, pausing the hotkey or exiting.
- Optional: hide the unlock word from the tray tooltip.
- The tray icon, menu and notifications now use PureTech.Tray: native menus that follow Windows dark mode, toasts with buttons such as **Open Settings**, and an icon that comes back after Explorer restarts or starts late at sign-in. ChildLock adds a Start menu shortcut so its toasts show under its own name.
- Declining the admin prompt no longer stops ChildLock: it runs without admin rights, stops asking, and explains what that means. **Run as administrator** in Settings turns it back on and restarts ChildLock elevated.
- Start at sign-in works without admin rights too (per-user startup entry), and switches to the elevated task once ChildLock runs as admin.
- `--uninstall` removes the sign-in entry, the Start menu shortcut and toast registration, and optionally your settings. `--no-elevate` skips the admin prompt once.
- Settings are grouped into Locking, Unlocking, Badge and sound, Cleaning mode and Startup.
- GitHub Actions builds and unit-tests every push on Windows, and publishes `ChildLock.exe` to a release for each version tag.

Fixed:

- Keys still held down when ChildLock unlocks (the last letter of the word, or the hotkey) no longer auto-repeat into the app that has focus.
- Auto-repeat of a key held down when the lock started (such as the `L` of Ctrl+Alt+L with no countdown) no longer counts as typing the unlock word.
- Exiting during a countdown no longer runs leftover work against the closed badge and tray icon.
- The badge moves to the right place after a resolution or monitor change.
- Starting a lock closes an open Settings window instead of leaving it clickable during a keyboard-only lock.
- A failed input hook is reported as such in the diagnostics log instead of as an exit.
- Small resource leaks in the badge and the Settings window, and the diagnostics log can no longer throw when its file is locked.

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
