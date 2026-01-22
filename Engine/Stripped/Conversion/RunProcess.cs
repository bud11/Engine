





namespace Engine.Stripped;


using System.Diagnostics;
using System.Text;



public static class RunProcess
{

    public static async Task Run(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        p.OutputDataReceived += (s, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        p.Start();

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new Exception($"\"{exe} {args}\" failed:\n{stdOut}\n{stdErr}");
    }


}


