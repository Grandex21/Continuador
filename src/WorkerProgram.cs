using System;

namespace Continuador
{
    // Entry point used exclusively by the Windows Task Scheduler.  This
    // assembly is compiled as WinExe so Windows never allocates a console.
    internal static class WorkerProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0 &&
                    string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the process alive long enough for the integration test
                    // to detect an accidentally allocated console host.
                    System.Threading.Thread.Sleep(1000);
                    return 0;
                }

                ScheduleManager.ProcessDue(null);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    StateStore.AppendTextLog("Error del worker silencioso: " + ex);
                }
                catch
                {
                }
                return 4;
            }
        }
    }
}
