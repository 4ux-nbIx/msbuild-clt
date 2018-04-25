namespace MsBuild.Utils
{
    internal interface ILogger
    {
        void WriteInfo(string message);
        void WriteError(string message);
        void WriteWarning(string message);
    }
}