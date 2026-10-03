using System;
using System.Threading;
using System.Windows.Forms;

namespace MSCNodeIDE.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Необработанные ошибки внутри UI показываем понятным сообщением,
            // а не стандартным диалогом .NET. Стек уходит в лог главного окна.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                var form = Form.ActiveForm;
                var text = "Внутренняя ошибка интерфейса:\n\n" + e.Exception.Message;
                MessageBox.Show(text, "Ошибка MSCNodeIDE",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (form is MainForm main) main.ReportInternalError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                MessageBox.Show(
                    "Критическая ошибка:\n\n" + (e.ExceptionObject as Exception)?.Message,
                    "Ошибка MSCNodeIDE", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}