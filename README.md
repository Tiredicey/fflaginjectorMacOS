## FlagInjector for macOS — User Guide

A powerful native utility for injecting and managing FFlags in Roblox on macOS.

## 📥 Download & Setup

### 1. Download the correct version
| Your Mac | Download |
| :--- | :--- |
| **Apple Silicon** (M1, M2, M3, M4) | FlagInjector-macOS-arm64.zip |
| **Intel** | FlagInjector-macOS-x64.zip |

### 2. Unzip
Double-click the .zip in Finder (Downloads) to extract FlagInjector-macOS-<arch>.app.

### 3. Remove quarantine
Open Terminal (⌘ + Space → type "Terminal") and run (or drag the .app into Terminal after the command):
```bash
xattr -cr /Users/YourName/Downloads/FlagInjector-macOS-arm64.app
```
No output means success.

### 4. Make executable
```bash
chmod +x /Users/YourName/Downloads/FlagInjector-macOS-arm64.app/Contents/MacOS/FlagInjector
```

### 5. Launch
- Option A (recommended): Double‑click the .app in Finder.  
- Option B (Terminal, with root):  
```bash
# Apple Silicon
sudo /Users/YourName/Downloads/FlagInjector-macOS-arm64.app/Contents/MacOS/FlagInjector

# Intel
sudo /Users/YourName/Downloads/FlagInjector-macOS-x64.app/Contents/MacOS/FlagInjector
```

## 🔐 Why it needs your password
FlagInjector uses macOS Mach VM APIs (task_for_pid) to write into Roblox process memory, which requires root privileges.

## 🚀 How to use

### Injecting flags
1. Launch Roblox (or let FlagInjector auto-detect when you join a game).  
2. Open FlagInjector.  
3. Browse or search available FFlags.  
4. Add flags and set values.  
5. Click **Apply** (or press ⌘ + Shift + A).  
6. The Watchdog will automatically re-apply flags if Roblox reverts them.

### Importing flags
Click **Import** (⌘ + O) or drag-and-drop a .json file. Supported formats:

Flat object format:
```json
{
  "FFlagEnableInGameMenuControls": "true",
  "FIntTaskPoolThreadSleepTimeMS": "1"
}
```

Array format:
```json
[
  { "Name": "FFlagEnableInGameMenuControls", "Value": "true" },
  { "Name": "FIntTaskPoolThreadSleepTimeMS", "Value": "1" }
]
```

### Exporting flags
Click **Export** (⌘ + S) to save current configuration as .json or ClientAppSettings.json.

## ⌨️ Keyboard shortcuts
| Shortcut | Action |
| :--- | :--- |
| ⌘ + Shift + A | Apply flags |
| ⌘ + O | Import JSON |
| ⌘ + S | Export JSON |
| ⌘ + Z | Undo |
| ⌘ + Y | Redo |
| ⌘ + Q | Quit |

## 🖥️ CLI arguments
Launch via Terminal with flags:
```bash
sudo ./FlagInjector --import /path/to/flags.json --auto-apply --minimized
```
| Argument | Description |
| :--- | :--- |
| --import <path> | Import JSON on launch |
| --preset <name> | Load a saved preset on launch |
| --auto-apply | Auto apply flags when Roblox is detected |
| --minimized | Start minimized |

## ❓ Troubleshooting
| Problem | Solution |
| :--- | :--- |
| "App is damaged..." | Run xattr -cr (Step 3). |
| Cannot attach to Roblox | Launch with sudo or provide password. |
| "Command not found" | Point to executable inside Contents/MacOS/. |
| No output after xattr | Normal; no output = success. |
| Password invisible | Terminal hides input; type and press Enter. |

## 🔒 Verify your download
File: FlagInjector-macOS-arm64.zip  
SHA-256: f944e46fd60eb7721d6a2b6f2e2a297b8309b5470b8cf8e761b9a9766edc6317

Verify:
```bash
shasum -a 256 ~/Downloads/FlagInjector-macOS-arm64.zip
```

## 📋 Requirements
- macOS (Apple Silicon or Intel)  
- Root privileges (administrator password)  
- No additional dependencies (self-contained)
