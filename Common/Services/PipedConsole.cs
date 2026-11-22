using System.Text;
using Spectre.Console;

namespace Catalyst.Common.Services;

/// <summary>
/// Makes piped output portable for the Syncfully Workbench, scripts and CI: UTF-8 text without a byte order mark,
/// so box drawing and non-ASCII spec titles survive the pipe, and one line per message, because Spectre otherwise
/// wraps at 80 columns and splits paths. The width is bounded so rules and tables stay readable; a path longer
/// than the width can still wrap, which consumers treat as a best-effort success line.
/// </summary>
internal static class PipedConsole
{
    public const int Width = 140;

    /// <summary>Must run before anything touches <see cref="AnsiConsole"/>, which captures the encoding when it creates its output writer.</summary>
    public static void Configure()
    {
        if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            return;
        }

        TryUseUtf8Output();

        if (Console.IsOutputRedirected)
        {
            AnsiConsole.Profile.Width = Width;
        }
    }

    private static bool TryUseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
