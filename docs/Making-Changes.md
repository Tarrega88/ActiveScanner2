# Making changes to Active Scanner

## One-time setup

1. Create a GitHub account: https://github.com/
   - Send your username to Michael so you can be given push access to the repo.
2. Install **Git**: https://git-scm.com/install/windows (default options are fine).
3. Install **VS Code**: https://code.visualstudio.com/
4. Install the **.NET 8 SDK**: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
   - Pick the **SDK**, not the "Runtime".
5. Close any open terminals (or restart the PC) so the new tools are picked up.
6. In a terminal, tell Git who you are:
   ```
   git config --global user.name "Your Name"
   git config --global user.email "you@example.com"
   ```

## Get the code

7. Create `C:\dev`, right-click it, and choose **Open in Terminal**.
8. Run:
   ```
   git clone https://github.com/Tarrega88/ActiveScanner2.git
   cd ActiveScanner2
   code .
   ```
   (`code .` opens VS Code in the current folder.)
9. In VS Code, install the **C# Dev Kit** and **GitHub Copilot** extensions if prompted, and sign in with GitHub. You can then ask the AI chat (right side) to explain the program or help make changes.

## Test a change

10. In VS Code's terminal (**Terminal → New Terminal**), run:
    ```
    dotnet run
    ```
    This builds and launches the app.

## Build an exe to use or share

11. Bump the version in `ActiveScanner.csproj`, `HelpWindow.xaml`, `installer\ActiveScanner.iss`, and `build-installer.ps1` (you can ask the AI to do this).
12. Run:
    ```
    .\publish-singlefile.ps1
    ```
    Output: `publish\single-file\ActiveScanner.exe` — a single file that runs without .NET installed.

## Save your changes to GitHub

13. Run:
    ```
    git pull
    git add -A
    git commit -m "Short description of what changed"
    git push
    ```
    (`git pull` first grabs anyone else's changes.)
14. Optional: on GitHub, go to **Releases → Draft a new release** and attach the exe so others can download it.
