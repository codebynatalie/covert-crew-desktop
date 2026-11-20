# Covert Crew Desktop

A tiny spy family and their dog live on your Windows desktop:

- **Agent Victor:** a secret agent in a fedora and trench coat.
- **Goldie:** an inventor mom with goggles and a wrench.
- **Pip:** a curious kid in a yellow raincoat.
- **Biscuit:** a dachshund in a red bandana.

They walk along the taskbar, climb the screen edges and hop onto the top of your open windows. If you move a window while someone is standing on it, they ride along.

## Using it

- **Open it:** double-click **Covert Crew** on the Desktop, or find it in the Start menu.
- **Cursor:**
  - Drag to pick someone up, and drop or throw them anywhere.
  - Click to poke, and double-click for a trick.
  - Wiggle the mouse back and forth over them to pet them.
- **Options:** right-click the hat icon by the clock. From there you can:
  - Gather everyone, start a dance party, say hi or call nap time.
  - Choose who's home and change their size.
  - Rename anyone, or give them your own picture.
  - Turn search reactions, cheering and window climbing on or off.
  - Start the crew with Windows.
  - Hide the crew, or quit.

## Privacy

- **Search reactions:** they react to what you search in your browser by reading the browser window's title, for example "pizza - Google Search". Nothing is sent anywhere.
- **Cheering:** the app only checks *whether* you're busy (how long since the last input), never what you type.
- **Full-screen apps:** the crew steps aside while a video or game is full-screen.

Settings and custom pictures are stored in `%APPDATA%\CovertCrew`.

## Files

| Path | What it is |
|---|---|
| `bin\CovertCrew.exe` | The app (the Desktop and Start menu shortcuts point here) |
| `src\` | C# / WPF source code |
| `art\` | Character drawings (SVG), built into the app |
| `icons\`, `app.ico` | App icon |
| `build.ps1` | Rebuilds the app with the C# compiler built into Windows |

## Rebuild

After changing the code, run:

```
powershell -ExecutionPolicy Bypass -File C:\code\Forgers-In-Desktop\build.ps1
```

The Chrome extension version is a separate project in `C:\code\Forgers-In-Tab`.
