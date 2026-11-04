using System;
using System.Windows.Forms;
namespace AzerothTrainer
{
    static class Program
    {
        [STAThread]static int Main(string[] args)
        {
            if(args.Length>0&&args[0]=="--verify-game")return TrainerDiagnostics.VerifyGame();
            if(args.Length>0&&args[0]=="--verify-files")return TrainerDiagnostics.VerifyFiles(args.Length>1?args[1]:null);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrainerWindow());return 0;
        }
    }
}
