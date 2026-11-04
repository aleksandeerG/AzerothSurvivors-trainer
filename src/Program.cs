using System;
using System.Windows.Forms;
namespace AzerothTrainer
{
    static class Program
    {
        [STAThread]static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,System.ResolveEventArgs request)
            {
                if(new System.Reflection.AssemblyName(request.Name).Name!="Iced")return null;
                using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Iced.dll"))
                {var bytes=new byte[checked((int)stream.Length)];int at=0;while(at<bytes.Length){int count=stream.Read(bytes,at,bytes.Length-at);if(count==0)throw new System.IO.EndOfStreamException();at+=count;}return System.Reflection.Assembly.Load(bytes);}
            };
            return Run(args);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            if(args.Length>0&&args[0]=="--verify-game")return TrainerDiagnostics.VerifyGame();
            if(args.Length>0&&args[0]=="--verify-files")return TrainerDiagnostics.VerifyFiles(args.Length>1?args[1]:null);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrainerWindow());return 0;
        }
    }
}
