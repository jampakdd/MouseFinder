# Mouse Finder

A tiny, dependency-free Windows tray app inspired by macOS's “shake mouse pointer to locate” feature. A shake must last 350 ms, reverse direction at least six times, and span at least half the current screen's width or height before Windows' native cursor becomes larger.

![Mouse Finder icon](MouseFinder-preview.png)

## Requirements

- Windows 10 or Windows 11 (x64)
- .NET 10 SDK to build and install

## Install and run at startup

Right-click `Install-Startup.ps1` and choose **Run with PowerShell**, or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Startup.ps1
```

That builds one small executable in `app\`, adds a shortcut to your Windows Startup folder, and launches it. It uses the .NET desktop runtime already installed on this PC.

The icon is already included. To regenerate it from `MouseFinder.svg`, install the optional Node development dependency and run `npm run generate-icon`.

To remove it, exit Mouse Finder from its tray menu and delete `Mouse Finder.lnk` from `shell:startup`.

## Notes

- Works over the entire Windows virtual desktop, including monitors positioned left of or above the primary monitor.
- Only observes pointer position; it does not hook or modify mouse input, speed, sensitivity, position, or clicks.
- Smoothly scales the native Windows cursor up to 4× over 175 ms and begins a 40 ms shrink when rolling movement speed falls below 500 pixels/second or direction reversals stop for 200 ms.
- Loads every configured Windows cursor role from the active theme—including arrow, hand, I-beam, working, busy, help, pen, unavailable, and resize variants—then enlarges each with nearest-neighbor scaling and its exact hotspot.
- Once shrinking begins, it completes without reversal, clears the previous shake, and waits 500 ms before accepting a brand-new shake gesture.
- Windows may show a SmartScreen prompt for an unsigned locally built executable.
