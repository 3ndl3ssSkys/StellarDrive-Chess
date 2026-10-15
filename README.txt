## Building

You need:

- **StellarDrive** (the demo) with **MelonLoader** installed
- **Git for Windows**, for Git Bash: https://git-scm.com/download/win
- **Mono**, for the C# compiler: https://www.mono-project.com/download/stable/
  (install to the default folder, `C:\Program Files\Mono`)

The build uses the game's own DLLs, so it won't work without the game installed.

### Steps

1. Clone the repo:

       git clone https://github.com/<you>/StellarDrive-Chess.git
       cd StellarDrive-Chess

2. Open Git Bash in that folder.

3. Tell the script where the game is (use your own path):

       export STELLARDRIVE_DIR="/c/Program Files (x86)/Steam/steamapps/common/StellarDrive Demo"

4. Build:

       bash build.sh chess --no-deploy

   You get `out/Chess.dll`.

5. Copy `out/Chess.dll` into the game's `Mods` folder.

If you leave out `--no-deploy`, the script copies the DLL into `Mods` for you.
The game has to be closed for that, because MelonLoader locks the files while it runs.

### If it fails

- **`csc: command not found`**: Mono isn't installed, or it's somewhere other than `C:\Program Files\Mono`.
- **Lots of "type or namespace could not be found" errors**: `STELLARDRIVE_DIR` is wrong. It should be the folder that contains `StellarDrive_Data` and `MelonLoader`.
- **Errors about `Chess.Common`**: the `src/Common` folder is missing.