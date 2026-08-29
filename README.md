# DL:TB Save Manager

A simple Windows tool to automatically manage save files, snapshots and backups for Dying Light The Beast.

---

## Quick start

1. Run `DLBeastSaveManager.exe`.
2. It finds your save folder automatically. Double check the path in the header is correct.
3. Disable Steam Cloud saves and open the game.
4. Backups are automatically created over time and can later be restored/rolled back to.

## Save File and Settings Locations

| | |
|---|---|
| Saves (Steam) | `<Steam>\userdata\<accountId>\3008130\remote\out\save` |
| Saves (Epic) | `%USERPROFILE%\Documents\dying light the beast\out\storage` |
| Snapshots | `%LOCALAPPDATA%\DLBeastSaveManager\backups` |
| Settings | `%APPDATA%\DLBeastSaveManager\settings.json` |

Each snapshot created is one zip holding **every file** in the save folder plus a manifest to differentiate save slots. 

## Steam Cloud (read before use)

DL:TB uses **Steam Cloud Saves** by default. This will likely interfere with the save tool and it is highly recommended to disable this before use.

This can be done by:

> Steam -> Library -> right-click *Dying Light: The Beast* -> Properties -> General -> untick **"Keep game saves in the Steam Cloud"**

The tool will also show a warning regarding this, if it detects that cloud saves are not disabled.

## Default Hotkeys

| Key | Action |
|---|---|
| `F9` | Backup now |
| `F10` | Pin the newest snapshot |

Both are rebindable in Settings. **Keybinds might not work in exclusive fullscreen.**

## Settings
Many aspects of the tool are able to be configured. The most important being:
- **Retention:** keeps the last 30 snapshots by default.
- **Backup folder:** the location where saves will be backed up to.
