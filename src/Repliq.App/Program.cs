namespace Repliq.App;

static class Program
{
    /// <summary>
    ///  The main entry point for the Repliq application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }    
}