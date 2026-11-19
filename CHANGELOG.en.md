# Changelog

[中文](CHANGELOG.md) | English

What changed in each version, newest first.

## Unreleased

- Fixed: one broken fingering file in the folder (for example a field of the wrong type or a number that is too large) disabled the whole mod, again on every launch. Now only that file is skipped and listed on the Custom Fingering page
- Fixed: the mod could not read a fingering whose author or comment happened to be a date and time (for example 2026-11-18T00:00:00Z)

## 1.3.1 · 2026-11-19

- Automatic updates: when the game starts, the mod checks for a new version, downloads and installs it in the background, and the update takes effect after you restart the game; your settings and fingerings are kept. You can turn it off on F8 → General. If the download fails, the About page lets you retry, or you can update from the group files or the download page as before. Update checks used to rely on UMM, which often never ran on some networks; the mod now checks by itself
- Fixed: in the fingering editor, music and notes fell out of sync after pausing and resuming or scrolling during playback (OGG and MP3 songs of custom levels). The song is now decoded in full before it plays, so every jump lands exactly, and custom levels get a waveform too. For levels opened before, click Edit this level's fingering in the game once more
- Fixed: with the game installed under Program Files on drive C (Steam's default location), Save to game in the fingering editor was blocked by the browser (it said the folder contains system files). The editor now saves to Documents\RhythmHint\Fingerings and the game moves the file into its fingering folder automatically

## 1.3.0 · 2026-11-19

- The settings window is redesigned: F8 opens it with pages on the left (Overview, Appearance, Fingering, Custom fingering, Calibration, General, About), icons and animations; a short guide plays the first time it opens, and the question mark in the top right shows it again. The window sits in the middle of the area the hint panels leave free and moves aside when they switch sides
- Custom fingering: make your own fingering for a song, or use one someone shared. Put it in Mods/RhythmHint/Fingerings and it's used automatically for that song; if the chart is updated, parts that no longer match use auto fingering, with a notice when the level starts
- New fingering editor: F8 → Custom fingering → Edit this level's fingering opens the level in your browser with its music, both auto styles for comparison, beat lines and a track preview (experimental); drag notes to change lanes, and saved changes take effect in the game right away; five languages
- The fingering editor can be closed halfway and keeps your changes; History on the left lists the levels you recently opened from the game, click one to pick up where you left off. The Custom fingering page can create a desktop shortcut, so the editor opens without the game
- The fingering editor uses the game's visual calibration, plays hit sounds on the musical beat, and shows a faint path through hidden tiles; practice speed changes the pitch of the music
- Estimated difficulty (experimental): in a level, the left side of the BPM box shows a TUF difficulty tier estimated from the level's tiles. It's for reference only and not an official TUF rating; turn it off on the Appearance page
- New fingering style option on the Fingering page: Standard (default) and Alternate first (experimental: fast short runs alternate both index fingers, with fewer hand changes around chords). Alternate first was contributed by Linxi-1105
- The main-hand reset gap now defaults to 130 ms instead of 165 ms: fast sections around 75 ms alternate both index fingers, bursts within 65 ms still roll on one hand, and notes 130 ms or more apart are tapped with one finger. It follows a frame-by-frame study of an Avantgarde perfect clear and matches the keys in that video 86% of the time (44% before). Settings still at 165 ms are moved to 130 ms automatically
- The four-lane panel can go on the right and the taiko bar at the bottom
- With the taiko bar off, its whole strip is gone and the game view no longer makes room for it; BPM and time left stay in the four-lane panel's column
- The on-screen keyboard for custom keys adds the numpad, F1–F12, editing keys such as Insert and Home, and mouse side buttons; the hotkey that opens the settings window can be customized the same way

## 1.2.1 · 2026-11-18

- Fixed: with Fit game view beside panel on, only the top edge of the autoplay button in the editor's bottom-right corner could be clicked during playback
- Fixed: in the level editor, notes on the lanes and the taiko bar kept moving after you died

## 1.2.0 · 2026-11-17

- Interface in Simplified Chinese, Traditional Chinese, English, Korean and Japanese; follows the game language by default, or pick one on F8 → Status
- Triple and quad chords are detected by the angle between tiles, so very fast streams are no longer taken as doubles; triples and quads are purple
- On the Fingering page, the chord time threshold (ms) is replaced by Max chord angle (°)
- New Chord hand setting on the Fingering page: chords can go to the other hand
- On the taiko bar, later notes are drawn on top of earlier ones
- The settings window's title bar tells you when a new version is out; right-click the mod on the Mods tab of the UMM installer and choose Update to update in one click
- The Status page now shows the author, the QQ group and the GitHub link

## 1.1.0 · 2026-11-17

- Custom keys: F8 → Display → Custom, pick your four keys on the on-screen keyboard; Shift, Space and arrow keys work too
- Round note style like the taiko bar, with judgment rings instead of key boxes
- The two millisecond settings on the Fingering page show the equivalent straight-path BPM
- Settings window re-laid out with shorter descriptions
- Key presets reduced to D F J K; use Custom for other keys (existing settings are kept)
- Fixed: the editor's autoplay button didn't respond during autoplay
- Fixed: pseudo-double notes moved past the judgment line after the first tile was hit

## 1.0.0 · 2026-11-16

- Fingering reworked around the gap between hits: right-hand J when slow, J F alternating when faster, same-hand rolls K J D F when very fast, back to the right hand after a pause
- Doubles go to the right hand and singles right next to them move to the left, so no more J → J+K → J and no index-then-outer-finger moves
- New Dominant hand setting (F8 → Fingering): left-handed players can mirror the whole layout
- Fit game view beside panel: the game view, combo, judgments, accuracy bar and pause menu move out from under the panel
- The panel hides when you pause with Esc
- Each settings page can be reset to its defaults
- The release zip can be installed directly with the UMM installer
