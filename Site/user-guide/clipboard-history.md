# Clipboard History

The Clipboard History plugin records what you copy on this machine and lets you search and re-paste any of it. Everything stays local.

## Opening the panel

Press `Win+V` (configurable in Settings → Hotkeys) or type the trigger keyword `cb` in the search box and choose **Open clipboard panel**. The panel opens at your mouse position.

If `Win+V` does nothing, Windows' own clipboard history still owns the key: open **Settings → Plugins → Clipboard History → Configure** and use **Open Windows clipboard settings** to turn the system feature off.

## What gets recorded

- **Text** you copy, up to the configured length.
- **Images** you copy (screenshots, browser images), shown as thumbnails.
- **Files and folders** you copy in Explorer.

Copies made while a password manager is in the foreground are skipped. The default exclusion list covers Keepass, KeepassXC, 1Password, Bitwarden and Enpass, and you can extend it in the settings.

## Working with the panel

- Type to filter the list; matching uses the same fuzzy and pinyin tiers as the main search.
- The chips on top filter by kind; the favorites chip shows pinned entries only.
- The list is grouped: pinned entries first, then today, yesterday, and older dates.
- `Enter` pastes the selected entry into the window you were using; `Ctrl+Enter` pastes it as plain text; `1`–`9` paste the first nine rows; `Del` removes an entry; `Ctrl+P` pins or unpins; `Esc` closes the panel.
- Select several rows with `Ctrl`+click or `Shift`+click to paste them merged: texts join with line breaks and file paths are combined.
- Pasted pinned entries keep the panel open, so a snippet you keep at hand can be pasted repeatedly.

## Settings

- **Record clipboard history**: turns capture on or off; existing history is kept.
- **Keep history for**: `1`, `7` or `30` days, or forever. Old unpinned entries are removed automatically; pinned entries are never aged out.
- **Maximum entries**: the unpinned cap.
- **Maximum text length**: how much of a single text is stored.
- **Excluded apps**: process names, comma separated.

The history is stored locally in your user data directory. Clearing the panel's history deletes it and the stored images for good.
