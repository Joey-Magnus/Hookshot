Sample files for the 2.x Builder to launcher hand-over (docs/launcher-upgrade.md). Both the upgrader's tests and the
launcher's tests read them, so the two sides agree.

- `hookshot-launcher.txt`: a launcher release's manifest (section 1), with an unknown platform line to skip.
- `builder-settings.json` + `import-windows.json`: what the Windows Builder saved, and the import file it hands over
  for a folder whose `hookshot_version.txt` says `version=v2.3.0-rc.2`, whose `language.txt` says `fr-FR`, and with
  `work.txt` holding `D:\HookshotWork`.
- `builder.ini` + `import-linux.json`: the same for the Linux Builder, a folder stamped `version=nightly-2025-1a2b3c4d`
  (no saved track: the track comes from the version).
