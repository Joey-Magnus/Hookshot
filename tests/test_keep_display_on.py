"""The Windows window keeps the display on while the game runs (issue 1532): python -I -B tests/test_keep_display_on.py

Windows counts no controller input as user activity, so with a pad it turned the display off at its power timeout mid-game;
the SC_SCREENSAVE answer in the window procedure stops only the screen saver. host/display.c's window thread asks for the
display with SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED ...) once the window is shown and before its message
loop (the thread lives as long as the window, and so does the request), unless HOS_ALLOW_DISPLAY_SLEEP=1. On Linux SDL does
the same ("Playing a game"). Source only: no Windows API is called here.
"""
import re
import sys
from pathlib import Path

src = (Path(__file__).resolve().parents[1] / "host/display.c").read_text(encoding="utf-8")
shown = src.find("ShowWindow(hwnd, SW_SHOW);")
call = src.find("SetThreadExecutionState(")
loop = src.find("while (GetMessageA(&m, NULL, 0, 0) > 0)")
failures = []
if min(shown, call, loop) < 0:
    failures.append("ShowWindow, SetThreadExecutionState or the message loop not found in host/display.c")
elif not shown < call < loop:
    failures.append("SetThreadExecutionState is not between the window's ShowWindow and its message loop")
else:
    flags = src[call:src.find(")", call)]
    for flag in ("ES_CONTINUOUS", "ES_DISPLAY_REQUIRED"):
        if flag not in flags:
            failures.append("SetThreadExecutionState without " + flag)
    if not re.search(r'HOS_ALLOW_DISPLAY_SLEEP', src[shown:call + 200]):
        failures.append("no HOS_ALLOW_DISPLAY_SLEEP opt-out around the call")
if failures:
    print("\n".join(failures))
    sys.exit(1)
print("keep display on: ok")
