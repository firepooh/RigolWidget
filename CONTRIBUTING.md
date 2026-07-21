# Contributing to RigolWidget

Thanks for your interest! This is a small hobby project, and contributions are welcome.

## Especially wanted: hardware test reports

Only **DP832** has been verified on real hardware. If you own another DP800-series supply
(**DP831 / DP821 / DP811** or an **A** variant), testing it and reporting back is the single
most useful contribution right now:

- Does it connect and show the correct model name in the title?
- Are the CH1/CH2 ratings/clamps correct for your model?
- Do measurement, output toggle, setpoints, and OCP/OVP work?

Please open a **Device test report** issue with what worked and what didn't (and the `*IDN?`
string if you can grab it).

## Reporting bugs

Open an issue with:
- Model and firmware (`*IDN?`), OS version, and which build you ran (standalone / framework-dependent)
- Steps to reproduce and what you expected vs. what happened
- Relevant lines from the log at `%LOCALAPPDATA%\RigolWidget\rigolwidget.log`

## Building from source

```
dotnet build -c Release
```

- Requirements: .NET 8 SDK, Windows (x64), and a VISA runtime (e.g. NI-VISA) to run against real hardware.
- Project layout:
  - `Visa/` — VISA P/Invoke, connection/reconnect, DP832 SCPI wrapper, model rating table
  - `Mcp/` — embedded MCP server, tools, shared context
  - `Windows/` — device selection window
  - `MainWindow.xaml(.cs)` — the widget UI
  - `Services/` — settings, edge snapping

## Pull requests

- Keep changes focused; match the existing code style.
- UI strings, tool descriptions, and comments are in **English**.
- If you change SCPI behavior, note which model(s) you tested on.

## Scope

This tool intentionally covers only CH1/CH2 control + monitoring for DP800-series supplies.
Big new directions (e.g. multi-instrument support) are tracked in the README roadmap — feel free
to open an issue to discuss before starting large work.
