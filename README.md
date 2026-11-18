# S Pen Tablet

Use a Samsung Galaxy S26 Ultra + S Pen as a pressure-sensitive drawing tablet for Windows (Krita via Windows Ink).

- `android/`: phone app (Kotlin). Reads the S Pen and sends samples over USB.
- `windows/`: PC tray app (C# / .NET 10). Receives the samples and creates a virtual Windows pen.
- `docs/GUIDE.md`: the full build guide (architecture, protocol, code, setup, milestones, troubleshooting).

Status: planning done; start at **Milestone 1** in the guide.
