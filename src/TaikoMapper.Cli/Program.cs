// Entry point: set up console output and dispatch the command line.

using System.Text;
using TaikoMapper.Beatmap.IO;
using TaikoMapper.Cli;

// Render Unicode (→ ★ · —) correctly in the Windows console instead of '␦' boxes.
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* output redirected — ignore */ }

// Keep osu!framework's logger from printing to the console / a Logs folder.
OsuLogging.Silence();

return App.Run(args);
