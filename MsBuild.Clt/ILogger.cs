namespace MsBuild.Clt
{
    internal interface ILogger
    {
        void WriteInfo(string message);
        void WriteError(string message);
        void WriteWarning(string message);
    }
}