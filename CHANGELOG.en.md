# Changelog

[中文](CHANGELOG.md) | English

What changed in each version, newest first.

## Unreleased

- Fixed: with Fit game view beside panel on, only the top edge of the autoplay button in the editor's bottom-right corner could be clicked during playback

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
