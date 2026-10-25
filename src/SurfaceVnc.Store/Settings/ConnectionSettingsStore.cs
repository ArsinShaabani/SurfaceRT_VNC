using System;
using Windows.Storage;

namespace SurfaceVnc.Store
{
    /// <summary>
    /// Remembers the last host/port between launches.  The password is deliberately *not*
    /// stored: keeping it in the app's local settings would leave it readable in plain text.
    /// </summary>
    public static class ConnectionSettingsStore
    {
        private const string HostKey = "LastHost";
        private const string PortKey = "LastPort";
        private const string KeepAwakeKey = "KeepScreenAwake";

        public static void Save(string host, int port)
        {
            try
            {
                ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
                settings.Values[HostKey] = host == null ? string.Empty : host;
                settings.Values[PortKey] = port;
            }
            catch (Exception)
            {
                // Local settings are unavailable in some container configurations; not fatal.
            }
        }

        public static string LoadHost()
        {
            try
            {
                object value = ApplicationData.Current.LocalSettings.Values[HostKey];
                string host = value as string;
                return host == null ? string.Empty : host;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static int LoadPort()
        {
            try
            {
                object value = ApplicationData.Current.LocalSettings.Values[PortKey];
                if (value is int) return (int)value;
            }
            catch (Exception)
            {
            }
            return Core.ConnectionSettings.DefaultPort;
        }

        /// <summary>When true the app keeps the screen awake while a session is running.</summary>
        public static bool LoadKeepScreenAwake()
        {
            try
            {
                object value = ApplicationData.Current.LocalSettings.Values[KeepAwakeKey];
                if (value is bool) return (bool)value;
            }
            catch (Exception)
            {
            }
            return true;
        }

        public static void SaveKeepScreenAwake(bool value)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[KeepAwakeKey] = value;
            }
            catch (Exception)
            {
            }
        }
    }
}
