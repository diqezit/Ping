#nullable enable

namespace PingTestTool
{
    public interface ILoggingService
    {
        void Information(string messageTemplate, params object[] propertyValues);
        void Warning(string messageTemplate, params object[] propertyValues);
        void Error(Exception ex, string messageTemplate, params object[] propertyValues);
        void Error(string messageTemplate, params object[] propertyValues);
        void Fatal(Exception ex, string messageTemplate, params object[] propertyValues);
    }

    public class SerilogLoggingService : ILoggingService
    {
        public void Information(string messageTemplate, params object[] propertyValues)
            => Log.Information(messageTemplate, propertyValues);

        public void Warning(string messageTemplate, params object[] propertyValues)
            => Log.Warning(messageTemplate, propertyValues);

        public void Error(Exception ex, string messageTemplate, params object[] propertyValues)
            => Log.Error(ex, messageTemplate, propertyValues);

        public void Error(string messageTemplate, params object[] propertyValues)
            => Log.Error(messageTemplate, propertyValues);

        public void Fatal(Exception ex, string messageTemplate, params object[] propertyValues)
            => Log.Fatal(ex, messageTemplate, propertyValues);
    }
}
