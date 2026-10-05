using System;
using System.Windows;

namespace Soot;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new PetWindow());
    }
}
