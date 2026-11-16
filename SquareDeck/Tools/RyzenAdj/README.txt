RyzenAdj for SquareDeck's TDP control
=====================================

These files come from RyzenAdj (https://github.com/FlyGoat/RyzenAdj), an open-source tool for AMD Ryzen
power limits, licensed under the GNU LGPL v3. Its source code is available at the address above.

  ryzenadj.exe, libryzenadj.dll   RyzenAdj itself
  WinRing0x64.sys, WinRing0x64.dll, inpoutx64.dll   the low-level hardware access it uses

SquareDeck doesn't run these from here. "Set up TDP control" (Settings > Power and performance) copies them
into Program Files\SquareDeck\TDP, where only administrators can change them, and runs them from there.
