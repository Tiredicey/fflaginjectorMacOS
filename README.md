

```markdown

\# FlagInjector for macOS — User Guide



A powerful, native utility for injecting and managing FFlags in Roblox on macOS.



---



\## 📥 Download \& Setup



\### 1. Download the Correct Version

| Your Mac | Download |

| :--- | :--- |

| \*\*Apple Silicon\*\* (M1, M2, M3, M4) | `FlagInjector-macOS-arm64.zip` |

| \*\*Intel\*\* | `FlagInjector-macOS-x64.zip` |



\### 2. Unzip

Double-click the `.zip` in your Downloads folder to extract `FlagInjector-macOS-<arch>.app`.



\### 3. Remove Quarantine

macOS prevents unsigned apps from running by default. Open \*\*Terminal\*\* (⌘ + Space → type "Terminal") and run:





xattr -cr /Users/YourName/Downloads/FlagInjector-macOS-arm64.app

```

> \*\*Tip:\*\* You can type `xattr -cr ` and then drag the `.app` file directly into the Terminal window to auto-fill the path. If there is no output, it was successful.



\### 4. Make Executable

In the same Terminal window, run:



```bash

chmod +x /Users/YourName/Downloads/FlagInjector-macOS-arm64.app/Contents/MacOS/FlagInjector

```



\### 5. Launch

\* \*\*Option A (Recommended):\*\* Double-click the `.app` in Finder.

\* \*\*Option B (Terminal):\*\* Run the executable directly with root privileges:



```bash

\# Apple Silicon

sudo /Users/YourName/Downloads/FlagInjector-macOS-arm64.app/Contents/MacOS/FlagInjector



\# Intel

sudo /Users/YourName/Downloads/FlagInjector-macOS-x64.app/Contents/MacOS/FlagInjector

```



---



\## 🔐 Why It Needs Your Password

FlagInjector utilizes macOS \*\*Mach VM APIs\*\* (`task\_for\_pid`) to write FFlags into the active Roblox process memory. Accessing the memory of another process requires \*\*root privileges\*\* on macOS.



---



\## 🚀 How to Use



\### Injecting Flags

1\.  Launch \*\*Roblox\*\* (or let FlagInjector auto-detect when you join a game).

2\.  Open \*\*FlagInjector\*\*.

3\.  Browse or search for available FFlags.

4\.  Add flags and set your desired values.

5\.  Click \*\*Apply\*\* (or press `⌘ + Shift + A`).

6\.  \*The Watchdog feature will automatically re-apply flags if Roblox attempts to revert them.\*



\### Importing Flags

Click \*\*Import\*\* (`⌘ + O`) or drag-and-drop a `.json` file onto the app. Supported formats:



\*\*Flat Object Format:\*\*

```json

{

&nbsp; "FFlagEnableInGameMenuControls": "true",

&nbsp; "FIntTaskPoolThreadSleepTimeMS": "1"

}

```



\*\*Array Format:\*\*

```json

\[

&nbsp; { "Name": "FFlagEnableInGameMenuControls", "Value": "true" },

&nbsp; { "Name": "FIntTaskPoolThreadSleepTimeMS", "Value": "1" }

]

```



\### Exporting Flags

Click \*\*Export\*\* (`⌘ + S`) to save your current configuration as a standard `.json` or as a `ClientAppSettings.json` file.



---



\## ⌨️ Keyboard Shortcuts



| Shortcut | Action |

| :--- | :--- |

| `⌘ + Shift + A` | Apply flags |

| `⌘ + O` | Import JSON |

| `⌘ + S` | Export JSON |

| `⌘ + Z` | Undo |

| `⌘ + Y` | Redo |

| `⌘ + Q` | Quit |



---



\## 🖥️ CLI Arguments

You can launch FlagInjector via Terminal with specific flags:



```bash

sudo ./FlagInjector --import /path/to/flags.json --auto-apply --minimized

```



| Argument | Description |

| :--- | :--- |

| `--import <path>` | Import JSON on launch |

| `--preset <name>` | Load a saved preset on launch |

| `--auto-apply` | Automatically apply flags when Roblox is detected |

| `--minimized` | Start the application minimized |



---



\## ❓ Troubleshooting



| Problem | Solution |

| :--- | :--- |

| \*\*"App is damaged..."\*\* | Run the `xattr -cr` command (Step 3). |

| \*\*Cannot attach to Roblox\*\* | Ensure you provided your password or launched with `sudo`. |

| \*\*"Command not found"\*\* | Ensure you are pointing to the executable inside `Contents/MacOS/`. |

| \*\*No output after xattr\*\* | This is normal; no output means the command worked. |

| \*\*Password invisible\*\* | Terminal does not show characters while typing passwords. Type and press Enter. |



---



\## 🔒 Verify Your Download

\*\*File:\*\* `FlagInjector-macOS-arm64.zip`  

\*\*SHA-256:\*\* `f944e46fd60eb7721d6a2b6f2e2a297b8309b5470b8cf8e761b9a9766edc6317`



\*\*Verify via Terminal:\*\*

```bash

shasum -a 256 ~/Downloads/FlagInjector-macOS-arm64.zip

```



---



\## 📋 Requirements

\* macOS (Apple Silicon or Intel)

\* Root privileges (Administrator password)

\* No additional dependencies (Self-contained)





