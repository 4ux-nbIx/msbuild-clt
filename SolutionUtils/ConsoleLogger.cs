namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;

    #endregion


    internal class ConsoleLogger : ILogger
    {
        public void WriteError(string message)
        {
            WriteMessage(message, ConsoleColor.Red);
        }

        public void WriteInfo(string message)
        {
            Console.WriteLine(message);
        }

        public void WriteWarning(string message)
        {
            WriteMessage(message, ConsoleColor.Yellow);
        }

        private static void WriteMessage(string message, ConsoleColor foregroundColor)
        {
            Console.ForegroundColor = foregroundColor;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}